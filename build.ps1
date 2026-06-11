<#
.SYNOPSIS
  Builds FreedomGuardian.exe using the .NET Framework C# compiler that ships
  with Windows (no .NET SDK or Visual Studio required).

.NOTES
  Output: build\FreedomGuardian.exe (targets .NET Framework 4.x, present on all
  Windows 10 1903+ / Windows 11 machines).
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $root 'build' }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "Could not find the .NET Framework C# compiler (csc.exe). Install the .NET Framework 4.x runtime."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir 'FreedomGuardian.exe'

$sources = Get-ChildItem -Path (Join-Path $root 'src') -Filter *.cs -Recurse |
    ForEach-Object { $_.FullName }

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.ServiceProcess.dll'
)

$icon = Join-Path $root 'assets\FreedomGuardian.ico'

$args = @(
    '/nologo',
    '/target:exe',
    '/platform:x64',
    '/optimize+',
    "/out:$out"
)
if (Test-Path $icon) { $args += "/win32icon:$icon" }
$args += ($refs | ForEach-Object { "/reference:$_" }) + $sources

Write-Host "Compiling $($sources.Count) source file(s) with csc..." -ForegroundColor Cyan
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit $LASTEXITCODE)." }

Write-Host "Build succeeded: $out" -ForegroundColor Green
