[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
}

$requiredProjects = @(
    'GameOptimizer.Core\GameOptimizer.Core.csproj',
    'GameOptimizer.Infrastructure.Windows\GameOptimizer.Infrastructure.Windows.csproj',
    'GameOptimizer.ModuleContracts\GameOptimizer.ModuleContracts.csproj',
    'GameOptimizer.Modules.PerformanceHealth\GameOptimizer.Modules.PerformanceHealth.csproj',
    'GameOptimizer.Shell\GameOptimizer.Shell.csproj',
    'GameOptimizer.Tests\GameOptimizer.Tests.csproj'
)
$solutions = @('GameOptimizer.sln', 'GameOptimizer.Main.sln')
$evidencePattern = '(?i)(_decompile|\.bak$|_Backup\.|_New\.|\.verify_build|docs[\\/]RECOVERY|_evidence|_staging|_recovery-history|_temporary-validation)'

$rootPath = (Resolve-Path -LiteralPath $Root).Path
$targetsPath = Join-Path $rootPath 'Directory.Build.targets'
$targets = Get-Content -LiteralPath $targetsPath -Raw -Encoding UTF8

foreach ($itemType in 'Compile', 'Content', 'None', 'Page', 'ApplicationDefinition', 'EmbeddedResource', 'Resource') {
    $expected = '<' + $itemType + ' Remove="$(EvidenceIsolationGlobs)"'
    if (-not ([string]$targets).Contains($expected)) {
        throw "Directory.Build.targets is missing the evidence exclusion rule for $itemType."
    }
}

foreach ($project in $requiredProjects) {
    $projectPath = Join-Path $rootPath $project
    $projectText = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    [xml]$projectXml = $projectText
    $includes = $projectXml.SelectNodes('//*[@Include]') |
        Where-Object { $_.Name -in 'Compile', 'Content', 'Page', 'ApplicationDefinition', 'EmbeddedResource', 'Resource' } |
        ForEach-Object { $_.GetAttribute('Include') }

    $unsafeInclude = $includes | Where-Object { $_ -match $evidencePattern } | Select-Object -First 1
    if ($null -ne $unsafeInclude) {
        throw "$project explicitly includes an isolated evidence item: $unsafeInclude"
    }
}

foreach ($solution in $solutions) {
    $solutionPath = Join-Path $rootPath $solution
    $projectPaths = Get-Content -LiteralPath $solutionPath -Encoding UTF8 |
        Where-Object { $_ -like 'Project(*' } |
        ForEach-Object {
            $segments = $_.Split('"')
            if ($segments.Length -ge 6) { $segments[5].Replace('/', '\') }
        }

    if ($projectPaths.Count -ne $requiredProjects.Count) {
        throw "$solution has $($projectPaths.Count) formal projects; expected $($requiredProjects.Count)."
    }

    foreach ($projectPath in $projectPaths) {
        if ($projectPath -match $evidencePattern -or $projectPath -notin $requiredProjects) {
            throw "$solution contains a non-formal or evidence project: $projectPath"
        }
    }
}

@($targetsPath) + ($requiredProjects | ForEach-Object { Join-Path $rootPath $_ }) +
    ($solutions | ForEach-Object { Join-Path $rootPath $_ }) |
    ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
    Select-Object Path, Hash

Write-Host 'Evidence isolation verification passed. The script performs read-only source inspection only.'
