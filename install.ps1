<#
.SYNOPSIS
  Installs the Freedom Guardian as two hardened LocalSystem services.

.DESCRIPTION
  Creates FreedomGuardian + FreedomGuardianWatch, configures auto-start, SCM
  failure recovery, and a locked-down service DACL so even an elevated admin
  cannot simply stop or delete them. The sanctioned way out is the delayed
  `unlock` cooldown; the emergency way out is Safe Mode (see README / uninstall).

  MUST be run from an elevated PowerShell ("Run as administrator").

.PARAMETER FreedomExePath
  Path to FreedomBlocker.exe (default: C:\Program Files (x86)\Freedom\FreedomBlocker.exe).

.PARAMETER CooldownHours
  Hours between `unlock` and protection actually releasing (default: 2).
#>
[CmdletBinding()]
param(
    [string]$InstallDir   = (Join-Path $env:ProgramFiles 'FreedomGuardian'),
    [string]$SourceExe,
    [string]$FreedomExePath = 'C:\Program Files (x86)\Freedom\FreedomBlocker.exe',
    [double]$CooldownHours  = 2,
    [int]$PollIntervalSeconds = 3
)

$ErrorActionPreference = 'Stop'

# ---- preconditions -----------------------------------------------------------

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Auto-elevate: relaunch this script as administrator, forwarding parameters.
if (-not (Test-Admin)) {
    Write-Host "Requesting administrator rights..." -ForegroundColor Yellow
    $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File', $PSCommandPath)
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Value -is [switch]) { if ($kv.Value.IsPresent) { $argList += "-$($kv.Key)" } }
        else { $argList += "-$($kv.Key)"; $argList += "$($kv.Value)" }
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    return
}

$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrEmpty($SourceExe)) { $SourceExe = Join-Path $root 'build\FreedomGuardian.exe' }

if (-not (Test-Path $SourceExe)) {
    throw "FreedomGuardian.exe not found at '$SourceExe'. Build it first: powershell -File build.ps1"
}

$guardianName = 'FreedomGuardian'
$watchName    = 'FreedomGuardianWatch'
$dataDir      = Join-Path $env:ProgramData 'FreedomGuardian'
$secureDir    = Join-Path $dataDir 'secure'

# Locked-down service DACL: SYSTEM full; Administrators may query+start only
# (no stop/change-config/delete/take-ownership). Mirrors ServiceControl.LockdownSddl.
$lockSddl = 'D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWRPLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'

Write-Host "Installing Freedom Guardian..." -ForegroundColor Cyan

# ---- 1. copy binary ----------------------------------------------------------

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$exe = Join-Path $InstallDir 'FreedomGuardian.exe'
Copy-Item -Force $SourceExe $exe

# ---- 2. data dir + config ----------------------------------------------------

New-Item -ItemType Directory -Force -Path $dataDir   | Out-Null
New-Item -ItemType Directory -Force -Path $secureDir | Out-Null

$config = @(
    '# FreedomGuardian configuration',
    "GuardianServiceName=$guardianName",
    "WatchServiceName=$watchName",
    "FreedomExePath=$FreedomExePath",
    'FreedomProcessName=FreedomBlocker',
    'RunKeyName=Freedom',
    "PollIntervalSeconds=$PollIntervalSeconds",
    "CooldownHours=$CooldownHours"
) -join "`r`n"
Set-Content -Path (Join-Path $dataDir 'config.ini') -Value $config -Encoding ASCII

# ---- 3. ACLs -----------------------------------------------------------------
# DataDir: SYSTEM+Admins full, Users modify (so `unlock`/`relock`/`status` work
# without elevation). secure\ : SYSTEM only, so the cooldown clock cannot be
# read, deleted or back-dated by the user.

& icacls $dataDir /inheritance:r /grant '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)M' | Out-Null
& icacls $secureDir /inheritance:r /grant '*S-1-5-18:(OI)(CI)F' | Out-Null

# ---- 4. create services ------------------------------------------------------

function Invoke-Sc {
    # Run sc.exe with errors visible. Throws if exit code is non-zero.
    $out = & sc.exe @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($args -join ' ') failed (exit $LASTEXITCODE): $out"
    }
}

function New-GuardianService($name, $role) {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
        Invoke-Sc delete $name
        Start-Sleep -Milliseconds 500
    }
    # New-Service avoids the well-known PowerShell 5.1 native-arg quoting bug
    # that mangles embedded quotes in sc.exe binPath= arguments.
    $binPath = '"' + $exe + '" ' + $role
    Write-Host "  creating service $name -> $binPath"
    New-Service -Name $name -BinaryPathName $binPath `
        -DisplayName "Freedom Guardian ($role)" `
        -Description "Keeps the Freedom blocker running; tamper-resistant self-control aid." `
        -StartupType Automatic -ErrorAction Stop | Out-Null

    # Restart on crash; never reset the counter; also act on non-crash stops.
    Invoke-Sc failure $name reset= 0 actions= restart/2000/restart/2000/restart/2000
    Invoke-Sc failureflag $name 1
}

New-GuardianService $guardianName 'guardian'
New-GuardianService $watchName 'watch'

# ---- 5. start, then lock down ------------------------------------------------

Start-Service -Name $guardianName -ErrorAction Stop
Start-Service -Name $watchName    -ErrorAction Stop
Start-Sleep -Seconds 2

# Apply the hardened DACL LAST so the steps above are unhindered.
Invoke-Sc sdset $guardianName $lockSddl
Invoke-Sc sdset $watchName    $lockSddl

# ---- 6. Defender exclusion (best effort) -------------------------------------

try {
    Add-MpPreference -ExclusionPath $exe -ErrorAction Stop
    Add-MpPreference -ExclusionProcess 'FreedomGuardian.exe' -ErrorAction Stop
    Write-Host "Added Windows Defender exclusion." -ForegroundColor DarkGray
} catch {
    Write-Warning "Could not add a Defender exclusion automatically: $($_.Exception.Message)"
    Write-Warning "If Defender quarantines the service, add an exclusion for: $exe"
}

Write-Host ""
Write-Host "Installed and running." -ForegroundColor Green
Write-Host "  Binary : $exe"
Write-Host "  Data   : $dataDir"
Write-Host "  Cooldown for unlock: $CooldownHours hour(s)"
Write-Host ""
Write-Host "To remove: run '$exe unlock', wait the cooldown, then run uninstall.ps1 (admin)."
Write-Host "Emergency removal is via Safe Mode - see README.md."
