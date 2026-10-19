<#
.SYNOPSIS
  Game performance tuning - READ-ONLY diagnostic collector (v2).

.DESCRIPTION
  Collects data needed to CLASSIFY gaming bottlenecks:
  latency vs throughput vs thermal vs memory vs storage vs software conflict.
  Does NOT modify the system.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\collect-diagnostics.ps1
#>

[CmdletBinding()]
param(
  [string]$OutFile
)

$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
if (-not $OutFile) {
  $desktop = [Environment]::GetFolderPath('Desktop')
  if (-not $desktop) { $desktop = $env:TEMP }
  $OutFile = Join-Path $desktop ("GameTuning-Diagnostics-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$sb = New-Object System.Text.StringBuilder
function Add-Line([string]$Text) { [void]$sb.AppendLine($Text) }
function Add-Section([string]$Title) {
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine(('=' * 64))
  [void]$sb.AppendLine("## $Title")
  [void]$sb.AppendLine(('=' * 64))
}

Add-Line 'Game Performance Tuning - Diagnostics (READ-ONLY) v2'
Add-Line "Generated: $stamp"
Add-Line 'This script must not change any system setting.'
Add-Line 'NOTE: This script reads system-level facts only. UI settings (NVIDIA/AMD control panel, in-game settings, some toggles) may not be reflected here. Missing output does NOT mean a feature is off.'

# --- Identity / admin ---
Add-Section 'Identity and elevation'
try {
  $os = Get-CimInstance Win32_OperatingSystem
  $cs = Get-CimInstance Win32_ComputerSystem
  $hostName = [System.Environment]::MachineName
  if (-not $hostName) { $hostName = $env:COMPUTERNAME }
  Add-Line ("Host: {0}" -f $hostName)
  Add-Line ("OS: {0} | Build: {1} | Version: {2}" -f $os.Caption, $os.BuildNumber, $os.Version)
  Add-Line ("Manufacturer: {0} | Model: {1}" -f $cs.Manufacturer, $cs.Model)
  # 1 Desktop, 2 Mobile/Notebook
  Add-Line ("PCSystemType: {0} (1=Desktop 2=Mobile)" -f $cs.PCSystemType)
  Add-Line ("TotalRAM_GB: {0:N1}" -f ($cs.TotalPhysicalMemory / 1GB))
  $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  Add-Line ("IsAdmin: {0}" -f $isAdmin)
} catch {
  Add-Line ("Identity failed: {0}" -f $_.Exception.Message)
}

# --- CPU (hybrid awareness) ---
Add-Section 'CPU'
try {
  Get-CimInstance Win32_Processor | ForEach-Object {
    Add-Line ("Name: {0}" -f $_.Name)
    Add-Line ("Cores: {0} | LogicalProcessors: {1} | MaxClockMHz: {2}" -f $_.NumberOfCores, $_.NumberOfLogicalProcessors, $_.MaxClockSpeed)
    Add-Line ("CurrentClockMHz: {0} | LoadPercentage: {1}" -f $_.CurrentClockSpeed, $_.LoadPercentage)
    # Hybrid CPUs: 12th gen+ Intel often have P+E cores; expose as hint only
    if ($_.Name -match 'i[3579]-1[2-9]\d{3}|Core Ultra') { Add-Line 'Hint: Possible Intel hybrid (P+E cores). Do NOT disable E-cores by default.' }
    if ($_.Name -match 'Ryzen') { Add-Line 'Hint: AMD Ryzen. Prefer OEM/Chipset driver + power plan.' }
  }
} catch { Add-Line $_.Exception.Message }

# --- Memory snapshot (Available MBytes + Commit; FreePhysicalMemory is NOT true free) ---
Add-Section 'Memory snapshot (Available / Commit)'
try {
  $avail = (Get-Counter '\Memory\Available MBytes').CounterSamples.CookedValue
  $commit = (Get-Counter '\Memory\Committed Bytes').CounterSamples.CookedValue
  $commitLimit = (Get-Counter '\Memory\Commit Limit').CounterSamples.CookedValue
  Add-Line ("AvailableMB: {0:N0} | CommittedGB: {1:N2} | CommitLimitGB: {2:N2} | CommitPct: {3:N1}" -f `
    $avail, ($commit/1GB), ($commitLimit/1GB), (100.0*$commit/$commitLimit))
  if ($avail -lt 2048) { Add-Line 'WARN: Available memory < 2GB - memory pressure. Prefer closing apps / adding RAM over registry tweaks.' }
  if (($commit/$commitLimit) -gt 0.9) { Add-Line 'WARN: Commit charge > 90% of limit - consider more RAM or smaller workload.' }
} catch {
  Add-Line ("Available/Commit counters failed: {0}" -f $_.Exception.Message)
  try {
    $os2 = Get-CimInstance Win32_OperatingSystem
    Add-Line ("Fallback FreePhysicalMemoryKB: {0} (not a true free metric)" -f $os2.FreePhysicalMemory)
  } catch {}
}

# --- Memory sticks / XMP / dual-channel hints ---
Add-Section 'Memory sticks (Speed vs ConfiguredClockSpeed)'
try {
  Get-CimInstance Win32_PhysicalMemory |
    Select-Object DeviceLocator, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed |
    Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  $sticks = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue)
  if ($sticks.Count -eq 1) {
    Add-Line 'WARN: single stick - dual-channel upgrade is often a large FPS gain on Ryzen (guidance only).'
  }
  foreach ($s in $sticks) {
    if ($s.Speed -and $s.ConfiguredClockSpeed -and ($s.ConfiguredClockSpeed -lt $s.Speed)) {
      Add-Line ("WARN: {0} running {1} MHz < rated {2} MHz - check XMP/EXPO/DOCP in BIOS (guidance only; we do not flash BIOS)." -f $s.DeviceLocator, $s.ConfiguredClockSpeed, $s.Speed)
    }
  }
} catch { Add-Line $_.Exception.Message }

# --- GPU (all adapters) ---
Add-Section 'GPU adapters'
try {
  Get-CimInstance Win32_VideoController | ForEach-Object {
    Add-Line ("Name: {0}" -f $_.Name)
    Add-Line ("  Status: {0} | VideoProcessor: {1}" -f $_.Status, $_.VideoProcessor)
    Add-Line ("  DriverVersion: {0} | DriverDate: {1}" -f $_.DriverVersion, $_.DriverDate)
    if ($_.CurrentRefreshRate) { Add-Line ("  CurrentRefreshRate: {0} Hz | Mode: {1}x{2}" -f $_.CurrentRefreshRate, $_.CurrentHorizontalResolution, $_.CurrentVerticalResolution) }
  }
  Add-Line 'Note: Win32 AdapterRAM is unreliable above 4GB; ignore for modern dGPU capacity.'
} catch { Add-Line $_.Exception.Message }

# --- Power plan + battery ---
Add-Section 'Power plan and battery'
try {
  Add-Line (powercfg /getactivescheme)
  Add-Line 'Schemes:'
  Add-Line (powercfg /list)
} catch { Add-Line $_.Exception.Message }

try {
  $bats = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)
  $bats | ForEach-Object {
    Add-Line ("Battery: Status={0} | EstimatedCharge={1}% | {2}" -f $_.BatteryStatus, $_.EstimatedChargeRemaining, $_.Name)
  }
  if ($bats.Count -eq 0) {
    Add-Line 'Battery: none detected (likely desktop or AC-only reporting).'
  } else {
    # BatteryStatus: 1=discharging(on battery), 2=on AC power
    $onBattery = $bats | Where-Object { $_.BatteryStatus -eq 1 }
    if ($onBattery) { Add-Line 'WARN: currently running ON BATTERY - laptop likely in reduced-power mode; check plugged-in before gaming tests.' }
    else { Add-Line 'AC power: plugged in (BatteryStatus != 1).' }
  }
} catch { Add-Line $_.Exception.Message }

# --- Disk / volume ---
Add-Section 'Disks and volumes'
try {
  Get-PhysicalDisk | Select-Object DeviceId, FriendlyName, MediaType, BusType, HealthStatus, OperationalStatus, @{n='SizeGB';e={[math]::Round($_.Size/1GB,1)}} |
    Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("PhysicalDisk: {0}" -f $_.Exception.Message) }

try {
  Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter, FileSystemLabel, FileSystem,
    @{n='SizeGB';e={[math]::Round($_.Size/1GB,1)}},
    @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,1)}},
    @{n='FreePct';e={ if ($_.Size) { [math]::Round(100.0*$_.SizeRemaining/$_.Size,1) } } } |
    Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object { Add-Line $_ }
  $sys = Get-Volume -DriveLetter C -ErrorAction SilentlyContinue
  if ($sys -and $sys.Size -and ($sys.SizeRemaining/$sys.Size) -lt 0.15) {
    Add-Line 'WARN: System volume free space <15%. Restore point creation may fail. Warn user before backup.'
  }
} catch { Add-Line ("Volume: {0}" -f $_.Exception.Message) }

# --- Network ---
Add-Section 'Network adapters'
try {
  Get-NetAdapter | Sort-Object Name | ForEach-Object {
    Add-Line ("{0} | {1} | Status={2} | Link={3} | MediaConnect={4}" -f $_.Name, $_.InterfaceDescription, $_.Status, $_.LinkSpeed, $_.MediaConnectionState)
  }
} catch { Add-Line $_.Exception.Message }

try {
  Get-NetAdapter | ForEach-Object {
    $n = $_.Name
    try {
      $pm = Get-NetAdapterPowerManagement -Name $n -ErrorAction Stop
      Add-Line ("PM [{0}]: WakeOnMagicPacket={1} DeviceSleepOnDisconnect={2}" -f $n, $pm.WakeOnMagicPacket, $pm.DeviceSleepOnDisconnect)
    } catch {
      Add-Line ("PM [{0}]: (not available without elevation or driver-specific props) {1}" -f $n, $_.Exception.Message)
    }
    try {
      $adv = Get-NetAdapterAdvancedProperty -Name $n -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match 'Power|Energy|Interrupt|Jumbo|Speed|Wake|节能|电源' } |
        Select-Object -First 12 DisplayName, DisplayValue
      if ($adv) {
        Add-Line ("ADV [{0}]:" -f $n)
        $adv | ForEach-Object { Add-Line ("  {0} = {1}" -f $_.DisplayName, $_.DisplayValue) }
      }
    } catch {}
  }
} catch { Add-Line $_.Exception.Message }

# --- Audio / problem devices ---
Add-Section 'Audio and problem devices'
try {
  Get-PnpDevice -Class MEDIA -ErrorAction SilentlyContinue |
    Select-Object Status, FriendlyName, InstanceId |
    Format-Table -AutoSize | Out-String -Width 220 | ForEach-Object { Add-Line $_ }
} catch { Add-Line $_.Exception.Message }

try {
  Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue |
    Select-Object Status, FriendlyName |
    Format-Table -AutoSize | Out-String -Width 220 | ForEach-Object { Add-Line $_ }
} catch { Add-Line $_.Exception.Message }

try {
  Add-Line 'Devices with problems (Status<>OK):'
  Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object { $_.Status -ne 'OK' } |
    Select-Object -First 40 Status, Class, FriendlyName, Problem |
    Format-Table -AutoSize | Out-String -Width 220 | ForEach-Object { Add-Line $_ }
} catch { Add-Line $_.Exception.Message }

# --- Security features (READ ONLY - do not disable) ---
Add-Section 'Security features (read-only; NEVER disable for FPS)'
try {
  $dg = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction SilentlyContinue
  if ($dg) {
    Add-Line ("VBSStatus: {0} | SecurityServicesRunning: {1}" -f $dg.VirtualizationBasedSecurityStatus, ($dg.SecurityServicesRunning -join ','))
  } else {
    Add-Line 'DeviceGuard WMI not available (Home edition or not supported).'
  }
} catch { Add-Line ("DeviceGuard: {0}" -f $_.Exception.Message) }

try {
  $secureBoot = Confirm-SecureBootUEFI -ErrorAction SilentlyContinue
  Add-Line ("SecureBoot: {0}" -f $secureBoot)
} catch { Add-Line ("SecureBoot: query failed ({0})" -f $_.Exception.Message) }

try {
  $def = Get-Service WinDefend -ErrorAction SilentlyContinue
  if ($def) { Add-Line ("WindowsDefenderService: {0}" -f $def.Status) }
} catch { Add-Line $_.Exception.Message }

Add-Line 'Anti-cheat policy reminder: do NOT disable Secure Boot, TPM, HVCI/VBS, or Defender real-time protection for FPS.'

# --- Game Mode / DVR / HAGS ---
Add-Section 'Game Mode / DVR / HAGS (registry read-only)'
$regPaths = @(
  'HKCU:\Software\Microsoft\GameBar',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR',
  'HKCU:\System\GameConfigStore',
  'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR',
  'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'
)
foreach ($p in $regPaths) {
  try {
    Add-Line ("[{0}]" -f $p)
    if (Test-Path $p) {
      $item = Get-Item -Path $p
      foreach ($name in $item.GetValueNames()) {
        if ($name -eq '') { continue }
        $raw = $item.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $kind = $item.GetValueKind($name)
        Add-Line ("  {0} = {1} | type={2}" -f $name, $raw, $kind)
      }
    } else {
      Add-Line '  (path not found)'
    }
  } catch {
    Add-Line ("  {0}" -f $_.Exception.Message)
  }
}

# Explicit HAGS read with caveat
try {
  $hagsPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'
  if (Test-Path $hagsPath) {
    $hags = Get-ItemProperty -Path $hagsPath -Name HwSchMode -ErrorAction Stop
    Add-Line ('HAGS HwSchMode = {0} (2=On, 1=Off)' -f $hags.HwSchMode)
  }
} catch {
  Add-Line 'HAGS HwSchMode not found in registry. This may mean default/off or insufficient permissions; if you toggled it in Settings UI, trust the UI state rather than this output.'
}

# --- Startup ---
Add-Section 'Startup'
try {
  Get-CimInstance Win32_StartupCommand |
    Select-Object Name, Location, Command |
    Format-Table -AutoSize -Wrap | Out-String -Width 240 | ForEach-Object { Add-Line $_ }
} catch { Add-Line $_.Exception.Message }

foreach ($run in @(
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
)) {
  try {
    if (Test-Path $run) {
      Add-Line ("[{0}]" -f $run)
      $item = Get-Item $run
      foreach ($name in $item.GetValueNames()) {
        Add-Line ("  {0} = {1} | type={2}" -f $name, $item.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames), $item.GetValueKind($name))
      }
    }
  } catch { Add-Line $_.Exception.Message }
}

# --- GPU clocks / limits / VRAM (read-only) ---
Add-Section 'GPU clocks / limits / VRAM (read-only)'
try {
  if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    Add-Line (nvidia-smi --query-gpu=name,driver_version,temperature.gpu,clocks.gr,clocks.sm,clocks.mem,utilization.gpu,utilization.memory,memory.total,memory.used,pcie.link.gen.current,pcie.link.width.current --format=csv,noheader)
    Add-Line 'Clocks throttle reasons (PERFORMANCE):'
    Add-Line (nvidia-smi -q -d PERFORMANCE | Out-String)
    Add-Line 'Note: Thermal/Power throttle reasons = hardware limit, not a software tweak.'
  } else {
    Add-Line 'nvidia-smi not found (non-NVIDIA or minimal driver).'
  }
  # AMD optional CLI (read-only)
  $amdSmi = Get-Command 'amd-smi','amdsmi','rocm-smi' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($amdSmi) {
    Add-Line ('AMD CLI found: ' + $amdSmi.Name)
    try { Add-Line (Get-Command $amdSmi.Name | Out-String) } catch {}
  } else {
    Add-Line 'amd-smi/rocm-smi not found (typical for Adrenalin consumer driver). Use AMD Software UI for temp/clocks.'
  }
  Add-Line 'AMD Software check: ' + $(if (Test-Path "${env:ProgramFiles}\AMD\CNext\CNext\RadeonSoftware.exe") { 'RadeonSoftware present' } else { 'RadeonSoftware not in default path' })
} catch { Add-Line $_.Exception.Message }

# --- Top processes by CURRENT CPU% (CIM snapshot; locale-independent, no flaky counter API) ---
Add-Section 'Top processes by CPU% (sampled)'
try {
  $null = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  $sampled = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction Stop |
    Where-Object { $_.Name -notin '_Total','Idle' } |
    ForEach-Object {
      [pscustomobject]@{ Name = $_.Name; CPUPct = [math]::Round($_.PercentProcessorTime / [Environment]::ProcessorCount, 1) }
    } | Sort-Object CPUPct -Descending | Select-Object -First 15
  $sampled | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  Add-Line ('Note: CPU% is an instant snapshot divided by {0} LogicalProcessors. Cumulative CPU seconds are NOT used.' -f [Environment]::ProcessorCount)
} catch { Add-Line ("CPU sample failed: {0}" -f $_.Exception.Message) }

# --- Display driver TDR / reset (7 days) ---
Add-Section 'Display driver TDR / reset (7 days)'
try {
  Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 300 |
    Where-Object { $_.Id -eq 4101 -or $_.ProviderName -match 'nvlddmkm|Display|amdkmdag' -or $_.Message -match 'TDR|nvlddmkm' } |
    Select-Object -First 15 TimeCreated, Id, ProviderName |
    Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  Add-Line 'Note: Display 4101 / nvlddmkm TDR = GPU reset (freeze/black flash). Driver reinstall is a candidate, not a routine tweak.'
} catch { Add-Line ("TDR query: {0}" -f $_.Exception.Message) }

# --- Events ---
Add-Section 'System log errors (7 days, max 40)'
try {
  Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 2; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 40 |
    Select-Object TimeCreated, Id, ProviderName,
      @{n='Msg';e={ $m = ($_.Message -replace '\s+', ' '); if ($m.Length -gt 160) { $m.Substring(0,160) } else { $m } }} |
    Format-Table -AutoSize -Wrap | Out-String -Width 220 | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("EventLog: {0}" -f $_.Exception.Message) }

# --- Restore points (read-only) ---
Add-Section 'System restore (read-only)'
try {
  $rp = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue)
  Add-Line ("RestorePointCount: {0}" -f $rp.Count)
  $rp | Sort-Object CreationTime -Descending | Select-Object -First 8 SequenceNumber, CreationTime, Description |
    Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  Add-Line 'If count is 0: restore may be off or never used. Backup strategy = export + self-rescue guide; warn about disk space.'
} catch { Add-Line $_.Exception.Message }

# --- Virtualization / WSL hints ---
Add-Section 'Virtualization / containers (read-only hints)'
try {
  $feat = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -ErrorAction SilentlyContinue
  if ($feat) { Add-Line ("Hyper-V-All State: {0}" -f $feat.State) }
} catch {
  Add-Line 'Hyper-V query needs elevation or not applicable; skip.'
}
try {
  $hv = Get-CimInstance Win32_ComputerSystem
  Add-Line ("HypervisorPresent: {0}" -f $hv.HypervisorPresent)
} catch {}

# --- Top processes by memory (Working Set) ---
Add-Section 'Top processes by memory (WorkingSet)'
try {
  Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 15 Name, @{n='WorkingSetMB';e={[math]::Round($_.WorkingSet64/1MB)}}, Id |
    Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  Add-Line 'Note: vmmem = WSL2/Hyper-V VM memory; common cause of "memory full" on dev machines.'
} catch { Add-Line ("Memory top failed: {0}" -f $_.Exception.Message) }

# --- Top processes by disk IO + disk latency ---
Add-Section 'Disk IO top processes and latency'
try {
  $null = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 1
  $io = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction Stop |
    Where-Object { $_.Name -notin '_Total','Idle' -and $_.IODataBytesPersec -gt 0 } |
    ForEach-Object { [pscustomobject]@{ Name = $_.Name; IO_KBps = [math]::Round($_.IODataBytesPersec / 1KB, 0) } } |
    Sort-Object IO_KBps -Descending | Select-Object -First 10
  $io | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("Disk IO top failed: {0}" -f $_.Exception.Message) }
try {
  $lat = (Get-Counter '\PhysicalDisk(*)\Avg. Disk sec/Transfer' -SampleInterval 2 -MaxSamples 2 -ErrorAction SilentlyContinue).CounterSamples |
    Where-Object { $_.InstanceName -ne '_total' } | ForEach-Object {
      [pscustomobject]@{ Disk = $_.InstanceName; AvgMs = [math]::Round($_.CookedValue * 1000, 1) }
    }
  $lat | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
  Add-Line 'Note: idle snapshot. AvgMs >20 on the game disk under load = I/O bottleneck evidence.'
} catch { Add-Line ("Disk latency failed: {0}" -f $_.Exception.Message) }

# --- Key services state ---
Add-Section 'Key services state'
try {
  Get-Service -Name SysMain,WSearch,DoSvc,WinDefend -ErrorAction SilentlyContinue |
    Select-Object Name, Status, StartType | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("Service query failed: {0}" -f $_.Exception.Message) }

# --- Third-party antivirus ---
Add-Section 'Antivirus products'
try {
  Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop |
    Select-Object displayName, productState | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("SecurityCenter2 query failed (may need admin or not supported): {0}" -f $_.Exception.Message) }

# --- Pending reboot / Windows Update activity ---
Add-Section 'Pending reboot / update activity'
try {
  $pending = @()
  if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $pending += 'CBS RebootPending' }
  if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $pending += 'WU RebootRequired' }
  $pfro = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
  if ($pfro) { $pending += 'PendingFileRenameOperations' }
  if ($pending.Count) { Add-Line ("PENDING REBOOT: {0}" -f ($pending -join '; ')) } else { Add-Line 'No pending reboot detected.' }
} catch { Add-Line ("Pending reboot check failed: {0}" -f $_.Exception.Message) }

# --- Last boot + fast startup + uptime ---
Add-Section 'Boot / fast startup'
try {
  $os3 = Get-CimInstance Win32_OperatingSystem
  Add-Line ("LastBootUpTime: {0} | UptimeHours: {1:N1}" -f $os3.LastBootUpTime, ((Get-Date) - $os3.LastBootUpTime).TotalHours)
} catch {}
try {
  $hiberboot = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -ErrorAction SilentlyContinue
  if ($hiberboot) { Add-Line ("FastStartup(HiberbootEnabled): {0} (1=on; shutdown+poweron does NOT fully reset kernel session)" -f $hiberboot.HiberbootEnabled) }
} catch {}
try {
  $hf = Get-Item 'C:\hiberfil.sys' -Force -ErrorAction SilentlyContinue
  if ($hf) { Add-Line ("hiberfil.sys size GB: {0:N1}" -f ($hf.Length/1GB)) }
} catch {}

# --- Boot duration (Event 100) ---
try {
  $bootEvents = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Diagnostics-Performance/Operational'; Id = 100 } -MaxEvents 3 -ErrorAction SilentlyContinue
  if ($bootEvents) {
    $bootEvents | ForEach-Object { Add-Line ("Boot Event100: {0} | BootTime_ms={1}" -f $_.TimeCreated, ($_.Properties[1].Value)) }
  } else { Add-Line 'No Diagnostics-Performance Event 100 found.' }
} catch { Add-Line ("Boot event query failed: {0}" -f $_.Exception.Message) }

# --- Pagefile / memory compression state (read-only; do NOT change) ---
Add-Section 'Pagefile / memory compression (read-only)'
try {
  $cs2 = Get-CimInstance Win32_ComputerSystem
  Add-Line ("AutomaticManagedPagefile: {0}" -f $cs2.AutomaticManagedPagefile)
} catch {}
try {
  Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue | ForEach-Object {
    Add-Line ("PageFile: {0} | AllocatedMB={1} | CurrentUsageMB={2}" -f $_.Name, $_.AllocatedBaseSize, $_.CurrentUsage)
  }
} catch {}
try {
  $mm = Get-MMAgent -ErrorAction SilentlyContinue
  if ($mm) { Add-Line ("MemoryCompression: {0}" -f $mm.MemoryCompression) }
} catch { Add-Line 'Get-MMAgent not available.' }

# --- WHEA hardware errors (7 days) ---
Add-Section 'WHEA hardware errors (7 days)'
try {
  $whea = Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-WHEA-Logger'; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 20 -ErrorAction SilentlyContinue
  if ($whea) {
    $whea | Select-Object TimeCreated, Id, LevelDisplayName | Format-Table -AutoSize | Out-String | ForEach-Object { Add-Line $_ }
    Add-Line 'WARN: WHEA errors present - check for unstable OC/XMP/undervolt; first troubleshooting step = restore defaults.'
  } else { Add-Line 'No WHEA-Logger events in last 7 days.' }
} catch { Add-Line ("WHEA query failed: {0}" -f $_.Exception.Message) }

# --- CPU throttle counter ---
Add-Section 'CPU performance % (throttle check)'
try {
  $perf = (Get-Counter '\Processor Information(_Total)\% Processor Performance' -SampleInterval 2 -MaxSamples 2 -ErrorAction Stop).CounterSamples |
    Measure-Object -Property CookedValue -Average
  Add-Line ("Avg % Processor Performance: {0:N0} (<<100 = throttled/downclocked)" -f $perf.Average)
} catch { Add-Line ("CPU perf counter failed: {0}" -f $_.Exception.Message) }

# --- NVIDIA extended (power / fan / pstate / ReBAR / display) ---
Add-Section 'NVIDIA extended (power/fan/ReBAR/display)'
try {
  if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    Add-Line (nvidia-smi --query-gpu=name,power.draw,power.limit,fan.speed,pstate,temperature.memory,display_active --format=csv,noheader)
    $rebar = nvidia-smi -q 2>$null | Select-String -Pattern 'Resizable BAR' | Select-Object -First 3
    if ($rebar) { $rebar | ForEach-Object { Add-Line ("ReBAR: {0}" -f $_.Line.Trim()) } } else { Add-Line 'ReBAR: unable to determine from nvidia-smi; check NVIDIA Control Panel -> System Information.' }
    Add-Line 'Note: idle snapshot; re-run under game load for real util/clock data.'
  }
} catch { Add-Line ("nvidia-smi extended failed: {0}" -f $_.Exception.Message) }

# --- Firmware type ---
Add-Section 'Firmware / boot mode'
try { Add-Line ("FirmwareType: {0} (2=UEFI required for ReBAR/Secure Boot)" -f $env:firmware_type) } catch {}

# --- Overlay / injection process check ---
Add-Section 'Overlay / injection processes'
try {
  $overlays = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'discord|overwolf|rtss|rivatuner|GeForce|nvcontainer|obs| medal|xsplit' } | Select-Object -ExpandProperty Name -Unique
  if ($overlays) { Add-Line ("Overlay-capable processes running: {0}" -f ($overlays -join ', ')) } else { Add-Line 'No common overlay processes detected.' }
} catch {}

# --- Scheduled tasks with logon/boot triggers (non-Microsoft) ---
Add-Section 'Third-party scheduled tasks (logon/boot triggers)'
try {
  Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $_.TaskPath -notlike '\Microsoft*' -and
    ($_.Triggers | Where-Object { $_.CimClass.CimClassName -match 'Logon|Boot' })
  } | Select-Object TaskName, TaskPath, State | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object { Add-Line $_ }
} catch { Add-Line ("ScheduledTask query failed: {0}" -f $_.Exception.Message) }

Add-Section 'Agent checklist after reading this file'
Add-Line '1. Classify bottleneck BEFORE proposing changes (SKILL.md bottleneck table).'
Add-Line '2. Output proposed AND checked-not-doing items; walk ITEMS.md categories relevant to the bottleneck.'
Add-Line '3. If AI/IDE/compile processes dominate CPU, do NOT treat that as the gaming baseline.'
Add-Line '4. UI-only items: full click path; after user acts, READ BACK system state; never claim script applied UI toggles.'
Add-Line '5. User must confirm EACH plan item AND backup; backup verifiable (restore point in list OR per-key .reg paths in deliverable).'
Add-Line '6. Every write: before snapshot -> write -> read back -> rollback on mismatch; after rollback re-test to confirm baseline restored.'
Add-Line '7. NEVER disable Defender/anti-cheat (permanent ban). VBS/HVCI/Secure Boot/TPM: SAFETY section II only; research failure = forbid.'
Add-Line '8. NEVER apply unknown bulk/esports presets or network-stack/pagefile/HPET/UAC/Firewall/WindowsUpdate writes.'
Add-Line '9. References that EXIST: SKILL.md, ITEMS.md, SAFETY.md, README.md. Do not invent other paths.'
Add-Line '10. Deliverables must include snapshot/backup path used this round; no path = do not claim success.'
Add-Line '11. UI-only states this script CANNOT read - mark "needs user confirmation", never assume: HAGS, NVCP per-app settings, MUX/dGPU-direct mode, in-game settings (DLSS/cap), OEM power mode (Fn+Q class), VRR enabled in display settings.'

# --- Build SUMMARY block (re-queries key facts; all read-only; prepended to report) ---
$sumSb = New-Object System.Text.StringBuilder
function S([string]$t) { [void]$sumSb.AppendLine($t) }
S 'Game Performance Tuning - Diagnostics (READ-ONLY) v2'
S ("Generated: {0}" -f $stamp)
S ''
S ('=' * 64)
S '## SUMMARY - key signals, read first; full details below'
S ('=' * 64)
try {
  $sos = Get-CimInstance Win32_OperatingSystem
  $scs = Get-CimInstance Win32_ComputerSystem
  $isAdmin2 = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  $winVer = if ([int]$sos.BuildNumber -ge 22000) { 'Win11' } else { 'Win10' }
  S ("OS: {0} | Build {1} ({2})" -f $sos.Caption, $sos.BuildNumber, $winVer)
  S ("FormFactor: PCSystemType={0} (1=Desktop 2=Mobile) | Battery: {1}" -f $scs.PCSystemType, [bool](Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue))
  S ("CPU: {0}" -f (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name))
  S ("RAM: {0:N1} GB | IsAdmin: {1}" -f ($scs.TotalPhysicalMemory/1GB), $isAdmin2)
} catch {}
try {
  $pm = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory -ErrorAction Stop
  $commitPct = if ($pm.CommitLimit) { [math]::Round(100.0*$pm.CommittedBytes/$pm.CommitLimit, 0) } else { $pm.PercentCommittedBytesInUse }
  S ("Memory: AvailMB={0:N0} | Commit={1}% of limit" -f $pm.AvailableMBytes, $commitPct)
} catch {}
try {
  $sticks = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue)
  if ($sticks.Count -gt 0) {
    S ("RAM sticks: {0} | rated {1}MHz / running {2}MHz{3}" -f $sticks.Count, $sticks[0].Speed, $sticks[0].ConfiguredClockSpeed,
      $(if ($sticks[0].ConfiguredClockSpeed -lt $sticks[0].Speed) { ' (XMP off?)' } else { '' }))
  }
} catch {}
try {
  Get-CimInstance Win32_VideoController | ForEach-Object {
    S ("GPU: {0} | drv {1} | {2}x{3}@{4}Hz" -f $_.Name, $_.DriverVersion, $_.CurrentHorizontalResolution, $_.CurrentVerticalResolution, $_.CurrentRefreshRate)
  }
} catch {}
try { S ("PowerPlan: {0}" -f (powercfg /getactivescheme | Out-String).Trim()) } catch {}
try {
  $sv = Get-Volume -DriveLetter C -ErrorAction Stop
  S ("C: free {0:N0}GB / {1:N0}GB ({2:N0}% free)" -f ($sv.SizeRemaining/1GB), ($sv.Size/1GB), (100.0*$sv.SizeRemaining/$sv.Size))
} catch {}
try {
  $tdrN = @(Get-WinEvent -FilterHashtable @{ LogName='System'; Id=4101; StartTime=(Get-Date).AddDays(-7) } -ErrorAction SilentlyContinue).Count
  $wheaN = @(Get-WinEvent -FilterHashtable @{ LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'; StartTime=(Get-Date).AddDays(-7) } -ErrorAction SilentlyContinue).Count
  S ("Last7d: TDR4101={0} | WHEA={1}" -f $tdrN, $wheaN)
} catch {}
try {
  $pend = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
          (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
  $up = ((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalHours
  S ("PendingReboot: {0} | UptimeHours: {1:N0} | RestorePoints: {2}" -f $pend, $up, @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue).Count)
} catch {}
try {
  if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    S ("GPU idle snapshot: {0}" -f (nvidia-smi --query-gpu=name,temperature.gpu,utilization.gpu,memory.used --format=csv,noheader))
  }
} catch {}
S '(idle snapshot - values under game load may differ; query sections below for detail)'

$text = $sumSb.ToString() + "`n" + $sb.ToString()
try {
  $dir = Split-Path -Parent $OutFile
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  Set-Content -Path $OutFile -Value $text -Encoding UTF8
  Write-Host "Diagnostics written to: $OutFile"
} catch {
  Write-Warning "Could not write file: $($_.Exception.Message)"
}
Write-Output $text
