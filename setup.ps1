<#
.SYNOPSIS
  One-step setup: builds FreedomGuardian.exe and installs the services.

.DESCRIPTION
  Self-elevates, compiles with the in-box C# compiler (no SDK needed), then runs
  install.ps1. This is the easiest way to get started - just run it (or
  double-click Install.bat).

.PARAMETER CooldownHours
  Hours between `unlock` and protection releasing (default: 2).

.PARAMETER FreedomExePath
  Path to FreedomBlocker.exe.
#>
[CmdletBinding()]
param(
    [double]$CooldownHours = 2,
    [string]$FreedomExePath = 'C:\Program Files (x86)\Freedom\FreedomBlocker.exe',
    [int]$PollIntervalSeconds = 3
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Auto-elevate, forwarding parameters.
if (-not (Test-Admin)) {
    Write-Host "Requesting administrator rights..." -ForegroundColor Yellow
    $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File', $PSCommandPath)
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        $argList += "-$($kv.Key)"; $argList += "$($kv.Value)"
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    return
}

$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

Write-Host "=== Freedom Guardian setup ===" -ForegroundColor Cyan

# 1. Build
& (Join-Path $root 'build.ps1')

# 2. Install
& (Join-Path $root 'install.ps1') -CooldownHours $CooldownHours `
    -FreedomExePath $FreedomExePath -PollIntervalSeconds $PollIntervalSeconds

Write-Host ""
Write-Host "Setup complete. Try:  FreedomGuardian status" -ForegroundColor Green
Read-Host "Press Enter to close"
