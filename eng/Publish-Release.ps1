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
$releaseName = "YuqiEngine-v$Version-$Runtime"
$releaseRoot = Join-Path $repositoryRoot "artifacts\release\v$Version"
$publishDirectory = Join-Path $releaseRoot $releaseName
$archivePath = Join-Path $releaseRoot "$releaseName-portable.zip"
$hashPath = Join-Path $releaseRoot 'SHA256SUMS.txt'

if ((Test-Path $releaseRoot) -and -not $Force) {
    throw "发布目录已存在：$releaseRoot。为避免覆盖既有发布物，请使用新版本号。"
}

if (-not (Test-Path $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

$publishArguments = @(
    'publish',
    $shellProject,
    '--configuration', 'Release',
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

Copy-Item (Join-Path $repositoryRoot 'README.md') (Join-Path $publishDirectory 'README.md')
Copy-Item (Join-Path $repositoryRoot 'LICENSE') (Join-Path $publishDirectory 'LICENSE')

if (Test-Path $archivePath) {
    if (-not $Force) {
        throw "发布压缩包已存在：$archivePath。为避免覆盖既有发布物，请使用新版本号。"
    }

    [System.IO.File]::Delete($archivePath)
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open(
    $archivePath,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        ForEach-Object {
            $entryName = [System.IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/')
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
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Encoding ascii -NoNewline -Value "$archiveHash  $(Split-Path -Leaf $archivePath)"

Write-Host "发布包已生成：$archivePath"
Write-Host "SHA-256：$archiveHash"
