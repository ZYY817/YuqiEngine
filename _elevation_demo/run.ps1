$ErrorActionPreference = 'Stop'
$coreDir = 'D:\优化软件\YHRJ - 副本\GameOptimizer.Core\bin\Release\net8.0-windows10.0.19041.0'
$src = 'D:\优化软件\YHRJ - 副本\_elevation_demo\Program.cs'
$out = 'D:\优化软件\YHRJ - 副本\_elevation_demo\ElevationDemo.dll'
$refs = @(
  "$coreDir\GameOptimizer.Core.dll",
  (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'System.Runtime.dll'),
  (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'System.Console.dll'),
  (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'System.Private.CoreLib.dll'),
  (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'System.Linq.dll'),
  (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'System.Collections.dll')
)
# Use dotnet script alternative: compile with csc from sdk
$csc = Get-ChildItem 'C:\Program Files\dotnet\sdk' -Recurse -Filter csc.dll | Sort-Object FullName -Descending | Select-Object -First 1
Write-Host "csc: $($csc.FullName)"
# simpler: use a prebuilt runtimeconfig and csc.dll
$runtimeconfig = @'
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
    "configProperties": { "System.Runtime.TieredCompilation": true }
  }
}
'@
Set-Content -Path 'D:\优化软件\YHRJ - 副本\_elevation_demo\ElevationDemo.runtimeconfig.json' -Value $runtimeconfig -Encoding UTF8
# Reference netstandard/netcore ref assemblies from Core's deps is hard; use existing Shell output dir which has all deps
$shellDir = 'D:\优化软件\YHRJ - 副本\GameOptimizer.Shell\bin\Release\net8.0-windows10.0.19041.0'
$refArgs = @()
$refArgs += "-r:`"$shellDir\GameOptimizer.Core.dll`""
$refArgs += "-r:`"$shellDir\System.Runtime.dll`""
$refArgs += "-r:`"$shellDir\System.Console.dll`""
$refArgs += "-r:`"$shellDir\System.Linq.dll`""
$refArgs += "-r:`"$shellDir\System.Collections.dll`""
$refArgs += "-r:`"$shellDir\System.Private.CoreLib.dll`""
# Also Microsoft.Extensions etc may be needed at runtime only
& dotnet $csc.FullName -nologo -t:exe -out:$out @refArgs $src
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Write-Host '--- RUN DEMO ---'
# copy Core next to demo? use deps from shell dir via DOTNET_ADDITIONAL_DEPS - simplest: run from shell dir with custom dll
Copy-Item $out "$shellDir\ElevationDemo.dll" -Force
Copy-Item 'D:\优化软件\YHRJ - 副本\_elevation_demo\ElevationDemo.runtimeconfig.json' "$shellDir\ElevationDemo.runtimeconfig.json" -Force
Push-Location $shellDir
dotnet ElevationDemo.dll
Pop-Location
