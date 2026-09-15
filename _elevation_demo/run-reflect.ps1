$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$coreDir = 'D:\优化软件\YHRJ - 副本\GameOptimizer.Core\bin\Release\net8.0-windows10.0.19041.0'
Get-ChildItem $coreDir -Filter *.dll | ForEach-Object {
  try { [System.Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch {}
}
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $coreDir 'GameOptimizer.Core.dll'))
Write-Host "Loaded: $($asm.FullName)"

$we = $asm.GetType('GameOptimizer.Core.Services.WindowsElevation')
$eg = $asm.GetType('GameOptimizer.Core.Services.ElevationGate')
$snapType = $asm.GetType('GameOptimizer.Core.Services.ElevationSnapshot')
$levelType = $asm.GetType('GameOptimizer.Core.Services.ElevationLevel')

Write-Host '=== CURRENT PROCESS (WindowsElevation) ==='
Write-Host ("Level                 : {0}" -f $we.GetProperty('Level').GetValue($null))
Write-Host ("IsElevated            : {0}" -f $we.GetProperty('IsElevated').GetValue($null))
Write-Host ("IsAdministratorAccount: {0}" -f $we.GetProperty('IsAdministratorAccount').GetValue($null))
Write-Host ("Detail                : {0}" -f $we.GetProperty('Current').GetValue($null).Detail)
Write-Host ''

function New-Snap([string]$levelName, [bool]$elevated, [bool]$account, [string]$reason, [string]$detail) {
  $level = [System.Enum]::Parse($levelType, $levelName)
  return [Activator]::CreateInstance($snapType, [object[]]@($level, $elevated, $account, $true, $reason, $detail))
}

$eval = $eg.GetMethods() | Where-Object {
  $_.Name -eq 'Evaluate' -and $_.GetParameters().Count -eq 4 -and $_.GetParameters()[2].ParameterType -eq $snapType
} | Select-Object -First 1

function Show-Eval([string]$title, $snap) {
  $d = $eval.Invoke($null, [object[]]@('SystemCleanup', $true, $snap, $null))
  Write-Host "=== $title ==="
  Write-Host ("NeedsElevation : {0}" -f $d.NeedsElevation)
  Write-Host ("Attribution    : {0}" -f $d.Attribution)
  $msg = $d.UserMessage
  $askAdmin = $msg.Contains('run as administrator') -or $msg.Contains([string]([char]0x4EE5) + [string]([char]0x7BA1) + [string]([char]0x7406) + [string]([char]0x5458) + [string]([char]0x8EAB) + [string]([char]0x4EFD))
  # Chinese phrase: 以管理员身份
  $askAdmin = $msg.Contains([char]0x4EE5 + [string]([char]0x7BA1) + [char]0x7406 + [char]0x5458 + [char]0x8EAB + [char]0x4EFD)
  $systemDeny = $msg.Contains([char]0x7CFB + [char]0x7EDF + [char]0x62D2 + [char]0x7EDD)
  Write-Host ("Contains restart-as-admin phrase : {0}" -f $askAdmin)
  Write-Host ("Contains system-denied phrase    : {0}" -f $systemDeny)
  Write-Host '--- message preview ---'
  $preview = $msg
  if ($preview.Length -gt 320) { $preview = $preview.Substring(0,320) + '...' }
  Write-Host $preview
  Write-Host ''
}

$elevatedSnap = New-Snap 'ElevatedAdministrator' $true $true 'elevation.elevated-administrator' 'elevated demo'
$uacSnap = New-Snap 'AdministratorNotElevated' $false $true 'elevation.administrator-token-filtered' 'uac filtered demo'
Show-Eval 'SCENARIO A: already elevated (must NOT ask to run as admin)' $elevatedSnap
Show-Eval 'SCENARIO B: UAC filtered (should correctly ask to elevate)' $uacSnap

$liveM = $eg.GetMethod('DescribeWriteDenial', [type[]]@([string], [string]))
$live = $liveM.Invoke($null, [object[]]@('SystemCleanup', 'access denied'))
Write-Host '=== LIVE PROCESS DescribeWriteDenial(access denied) ==='
$askAdmin = $live.Contains([char]0x4EE5 + [string]([char]0x7BA1) + [char]0x7406 + [char]0x5458 + [char]0x8EAB + [char]0x4EFD)
$systemDeny = $live.Contains([char]0x7CFB + [char]0x7EDF + [char]0x62D2 + [char]0x7EDD)
Write-Host ("Contains restart-as-admin phrase : {0}" -f $askAdmin)
Write-Host ("Contains system-denied phrase    : {0}" -f $systemDeny)
if ($live.Length -gt 280) { $live = $live.Substring(0,280) + '...' }
Write-Host $live
