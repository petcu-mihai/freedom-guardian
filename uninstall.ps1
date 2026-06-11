<#
.SYNOPSIS
  Removes the Freedom Guardian services.

.DESCRIPTION
  The intended removal path is:
     1. Run:  "C:\Program Files\FreedomGuardian\FreedomGuardian.exe" unlock
     2. Wait out the cooldown. When it elapses the services restore normal
        permissions, disable themselves and stop.
     3. Run this script (elevated) to delete the services and files.

  While protection is still active the service DACL blocks stop/delete even for
  admins, so this script will refuse to force it and will print the Safe Mode
  fallback instead. Use -Force only after the cooldown has elapsed.

  MUST be run from an elevated PowerShell.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Auto-elevate: relaunch this script as administrator, forwarding parameters.
if (-not (Test-Admin)) {
    Write-Host "Requesting administrator rights..." -ForegroundColor Yellow
    # Quote the script path (the install folders contain spaces) so the
    # elevated relaunch does not split the path into multiple arguments.
    $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File', "`"$PSCommandPath`"")
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Value -is [switch]) { if ($kv.Value.IsPresent) { $argList += "-$($kv.Key)" } }
        else { $argList += "-$($kv.Key)"; $argList += "`"$($kv.Value)`"" }
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    return
}

$guardianName = 'FreedomGuardian'
$watchName    = 'FreedomGuardianWatch'
$installDir   = Join-Path $env:ProgramFiles 'FreedomGuardian'
$dataDir      = Join-Path $env:ProgramData 'FreedomGuardian'

function Get-StartType($name) {
    $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
    if (Test-Path $k) { return (Get-ItemProperty $k -Name Start -ErrorAction SilentlyContinue).Start }
    return $null
}

function Show-SafeModeFallback {
    Write-Host ""
    Write-Host "EMERGENCY REMOVAL (if you cannot wait for the cooldown):" -ForegroundColor Yellow
    Write-Host "  1. Boot Windows into Safe Mode (services with 'auto' start do not run there)."
    Write-Host "  2. Open an elevated command prompt and run:"
    Write-Host "       sc delete $guardianName"
    Write-Host "       sc delete $watchName"
    Write-Host "  3. Delete '$installDir' and '$dataDir'."
    Write-Host "  4. Reboot normally."
}

$gStart = Get-StartType $guardianName
$wStart = Get-StartType $watchName
$exists = ($gStart -ne $null) -or ($wStart -ne $null)

if (-not $exists) {
    Write-Host "Services are not installed. Cleaning up any leftover files." -ForegroundColor Cyan
}
else {
    # 4 = disabled. After a completed stand-down both should be disabled and
    # their DACLs restored, so deletion will succeed.
    $stoodDown = ($gStart -eq 4) -and ($wStart -eq 4)
    if (-not $stoodDown -and -not $Force) {
        Write-Warning "Protection is still active (services are not in the stood-down state)."
        Write-Warning "Run the unlock cooldown first:"
        Write-Host    "    `"$installDir\FreedomGuardian.exe`" unlock"
        Write-Host    "  then wait the cooldown and re-run this script."
        Show-SafeModeFallback
        return
    }
}

Write-Host "Removing Freedom Guardian..." -ForegroundColor Cyan

foreach ($name in @($guardianName, $watchName)) {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Write-Host "  stopping $name"
        Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Write-Host "  deleting $name"
        $out = & sc.exe delete $name 2>&1
        if ($LASTEXITCODE -ne 0) { Write-Warning "sc delete $name failed (exit $LASTEXITCODE): $out" }
    }
}

Start-Sleep -Seconds 1

# Re-grant Administrators access to the SYSTEM-only `secure` subfolder so the
# recursive delete below can succeed. (The lockdown ACL is the whole reason a
# silent delete previously left this folder behind.)
$secureDir = Join-Path $dataDir 'secure'
if (Test-Path $secureDir) {
    Write-Host "  restoring ACLs on $secureDir"
    & icacls $secureDir /grant '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null
    & icacls $secureDir /inheritance:e /C | Out-Null
}

function Remove-Tree($path) {
    if (-not (Test-Path $path)) { return }
    Write-Host "  removing $path"
    try {
        Remove-Item -Recurse -Force -LiteralPath $path -ErrorAction Stop
    } catch {
        Write-Warning "Remove failed: $($_.Exception.Message)"
    }
    if (Test-Path $path) {
        Write-Warning "Still present after delete: $path"
        Write-Warning "Items remaining:"
        Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Warning "    $($_.FullName)" }
    }
}

Remove-Tree $installDir
Remove-Tree $dataDir

try {
    Remove-MpPreference -ExclusionProcess 'FreedomGuardian.exe' -ErrorAction SilentlyContinue
    Remove-MpPreference -ExclusionPath (Join-Path $installDir 'FreedomGuardian.exe') -ErrorAction SilentlyContinue
} catch { }

$installLeft = Test-Path $installDir
$dataLeft    = Test-Path $dataDir
if ($installLeft -or $dataLeft) {
    Write-Warning "Uninstall finished with leftovers:"
    if ($installLeft) { Write-Warning "  $installDir" }
    if ($dataLeft)    { Write-Warning "  $dataDir" }
} else {
    Write-Host "Uninstalled cleanly." -ForegroundColor Green
}

# Keep the elevated window open so you can read any warnings above.
Read-Host "Press Enter to close"
