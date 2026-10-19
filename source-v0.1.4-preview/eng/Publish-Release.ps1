[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.3-preview',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$shellProject = Join-Path $repositoryRoot 'GameOptimizer.Shell\GameOptimizer.Shell.csproj'
$installerScript = Join-Path $repositoryRoot 'installer\YuqiEngine.iss'
$releaseName = "YuqiEngine-v$Version-$Runtime"
$releaseRoot = Join-Path $repositoryRoot "artifacts\release\v$Version"
$portableRoot = Join-Path $releaseRoot 'portable'
$publishDirectory = Join-Path $portableRoot $releaseName
$archiveRootName = "$releaseName-portable"
$archivePath = Join-Path $portableRoot "$archiveRootName.zip"
$installerRoot = Join-Path $releaseRoot 'installer'
$installerBaseName = "$releaseName-setup"
$installerPath = Join-Path $installerRoot "$installerBaseName.exe"
$checksumsRoot = Join-Path $releaseRoot 'checksums'
$hashPath = Join-Path $checksumsRoot 'SHA256SUMS.txt'

if (Test-Path -LiteralPath $releaseRoot) {
    if (-not $Force) {
        throw "发布目录已存在：$releaseRoot。为避免覆盖既有发布物，请使用新版本号。"
    }

    # -Force only rebuilds the exact version directory under artifacts\release.
    [System.IO.Directory]::Delete($releaseRoot, $true)
}

@($portableRoot, $installerRoot, $checksumsRoot) |
    ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }

$publishArguments = @(
    'publish',
    $shellProject,
    '--configuration', 'Release',
    '--no-restore',
    '-m:1',
    '-p:BuildInParallel=false',
    '-p:UseSharedCompilation=false',
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishDirectory,
    "-p:Version=$Version",
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false'
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$requiredFiles = @(
    'YuqiEngine.exe',
    'YuqiEngine.deps.json',
    'YuqiEngine.runtimeconfig.json',
    'GameOptimizer.Core.dll',
    'GameOptimizer.Infrastructure.Windows.dll',
    'GameOptimizer.Modules.PerformanceHealth.dll',
    'Assets\Fonts\OFL-1.1.txt'
)

$missingFiles = $requiredFiles | Where-Object { -not (Test-Path (Join-Path $publishDirectory $_)) }
if ($missingFiles) {
    throw "发布目录缺少必要文件：$($missingFiles -join ', ')"
}

# README / LICENSE live at the repository root, one level above the source dir.
$publicFaceRoot = Split-Path -Parent $repositoryRoot
Copy-Item (Join-Path $publicFaceRoot 'README.md') (Join-Path $publishDirectory 'README.md')
Copy-Item (Join-Path $publicFaceRoot 'README_EN.md') (Join-Path $publishDirectory 'README_EN.md')
Copy-Item (Join-Path $publicFaceRoot 'LICENSE') (Join-Path $publishDirectory 'LICENSE')

# 便携版不需要调试符号；移除它们，使解压目录与 ZIP 的发布内容一致。
Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse |
    ForEach-Object { [System.IO.File]::Delete($_.FullName) }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open(
    $archivePath,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $publishPrefix = $publishDirectory.TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        ForEach-Object {
            # PS 5.1 (.NET Framework) lacks [Path]::GetRelativePath; the publish
            # root is a prefix of every file path by construction.
            $entryName = $_.FullName.Substring($publishPrefix.Length).Replace('\', '/')
            $entryName = "$archiveRootName/$entryName"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $_.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
}
finally {
    $archive.Dispose()
}

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "缺少安装器脚本：$installerScript"
}

$innoSetupCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$innoSetupCompiler = $innoSetupCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $innoSetupCompiler) {
    throw '未找到 Inno Setup 6。请安装后重新运行发布脚本。'
}

& $innoSetupCompiler "/DAppVersion=$Version" "/DSourceDir=$publishDirectory" "/DOutputDir=$installerRoot" "/DOutputBaseFilename=$installerBaseName" $installerScript
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "安装包生成失败，退出码：$LASTEXITCODE"
}

$hashLines = @($archivePath, $installerPath) |
    ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $_)"
    }
Set-Content -LiteralPath $hashPath -Encoding ascii -Value $hashLines

Write-Host "便携版：$archivePath"
Write-Host "安装版：$installerPath"
Write-Host "校验文件：$hashPath"
