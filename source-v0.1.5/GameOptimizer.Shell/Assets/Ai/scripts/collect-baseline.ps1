<#
.SYNOPSIS
  READ-ONLY baseline snapshot for before/after game tuning compare.
#>
[CmdletBinding()]
param([string]$OutFile)

$ErrorActionPreference = 'Continue'
if (-not $OutFile) {
  $desktop = [Environment]::GetFolderPath('Desktop')
  if (-not $desktop) { $desktop = $env:TEMP }
  $OutFile = Join-Path $desktop ("GameTuning-Baseline-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$sb = New-Object System.Text.StringBuilder
function W([string]$t) { [void]$sb.AppendLine($t) }

W ('GameTuning BASELINE (READ-ONLY) @ ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
W ('User=' + [System.Environment]::UserName + ' Host=' + [System.Environment]::MachineName)

W ''
W '## Power'
try { W (powercfg /getactivescheme) } catch { W $_.Exception.Message }

W ''
W '## Game Mode / DVR / HAGS'
foreach ($p in @(
  'HKCU:\Software\Microsoft\GameBar',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR',
  'HKCU:\System\GameConfigStore',
  'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'
)) {
  W ('[' + $p + ']')
  try {
    if (Test-Path $p) {
      $item = Get-Item $p
      foreach ($n in $item.GetValueNames()) {
        if ($n) { W ('  ' + $n + ' = ' + $item.GetValue($n, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) + ' | type=' + $item.GetValueKind($n)) }
      }
    } else { W '  (missing)' }
  } catch { W ('  ' + $_.Exception.Message) }
}

W ''
W '## GPU'
try {
  Get-CimInstance Win32_VideoController | ForEach-Object {
    W ('{0} | drv {1} | {2} | {3}x{4}@{5}' -f $_.Name, $_.DriverVersion, $_.DriverDate,
      $_.CurrentHorizontalResolution, $_.CurrentVerticalResolution, $_.CurrentRefreshRate)
  }
} catch { W $_.Exception.Message }

W ''
W '## Network'
try {
  Get-NetAdapter | ForEach-Object {
    W ('{0} | {1} | {2} | {3}' -f $_.Name, $_.InterfaceDescription, $_.Status, $_.LinkSpeed)
  }
} catch { W $_.Exception.Message }

W ''
W '## C: volume'
try {
  $v = Get-Volume -DriveLetter C
  W ('SizeGB={0:N1} FreeGB={1:N1} FreePct={2:N1}' -f ($v.Size/1GB), ($v.SizeRemaining/1GB), (100.0*$v.SizeRemaining/$v.Size))
} catch { W $_.Exception.Message }

W ''
W '## Memory (Available / Commit)'
try {
  $avail = (Get-Counter '\Memory\Available MBytes').CounterSamples.CookedValue
  $commit = (Get-Counter '\Memory\Committed Bytes').CounterSamples.CookedValue
  $commitLimit = (Get-Counter '\Memory\Commit Limit').CounterSamples.CookedValue
  W ("AvailableMB: {0:N0} | CommittedGB: {1:N2} | CommitLimitGB: {2:N2}" -f $avail, ($commit/1GB), ($commitLimit/1GB))
} catch {
  $os = Get-CimInstance Win32_OperatingSystem
  W ("Fallback FreePhysicalMemoryKB: {0} (not true free)" -f $os.FreePhysicalMemory)
}

W ''
W '## Memory sticks (Speed vs ConfiguredClockSpeed)'
try {
  Get-CimInstance Win32_PhysicalMemory |
    Select-Object DeviceLocator, Manufacturer, Capacity, Speed, ConfiguredClockSpeed |
    Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }
} catch { W $_.Exception.Message }

W ''
W '## Top processes by CPU% (CIM snapshot)'
try {
  $null = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction Stop |
    Where-Object { $_.Name -notin '_Total','Idle' } |
    ForEach-Object {
      [pscustomobject]@{ Name = $_.Name; CPUPct = [math]::Round($_.PercentProcessorTime / [Environment]::ProcessorCount, 1) }
    } | Sort-Object CPUPct -Descending | Select-Object -First 12 |
    Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }
  W 'Note: CPU% is an instant snapshot. Cumulative CPU seconds are NOT used (misleading).'
} catch { W ("CPU sample: " + $_.Exception.Message) }

W '## TDR (4101) last 7 days (read-only)'
try {
  $ev = Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 4101; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 10 -ErrorAction SilentlyContinue
  if ($ev) { $ev | Select-Object -First 5 TimeCreated | Format-Table | Out-String | ForEach-Object { W $_ } }
  else { W 'None.' }
} catch { W ("TDR: " + $_.Exception.Message) }

W ''
W '## GPU throttle reasons (read-only)'
try {
  if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    W (nvidia-smi --query-gpu=name,temperature.gpu,clocks.gr,memory.used,utilization.gpu --format=csv,noheader)
    W (nvidia-smi -q -d PERFORMANCE | Out-String)
  }
} catch { W $_.Exception.Message }

W ''
W '## NOTE'
W 'Fill game metrics separately (avg FPS / 1% low or stutter count / aim feel 1-5 / audio crackles).'
W 'Same map, settings, duration before vs after. See SKILL.md validation protocol.'

$text = $sb.ToString()
try {
  $dir = Split-Path -Parent $OutFile
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  Set-Content -Path $OutFile -Value $text -Encoding UTF8
  Write-Host "Baseline written: $OutFile"
} catch {
  Write-Warning $_.Exception.Message
}
Write-Output $text
