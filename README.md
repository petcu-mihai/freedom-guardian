# Freedom Guardian

A small, tamper-resistant watchdog for the [Freedom](https://freedom.to) website/app
blocker on Windows. It's a **self-control commitment device**: you install it on
your *own* machine to make it deliberately hard for an impulsive you to kill the
blocker, while always leaving a safe way out.

Freedom normally runs as two ordinary user processes (`FreedomBlocker.exe`, which
spawns `FreedomProxy.exe`). Anyone can end them from the command
line, which defeats the blocker. Freedom Guardian closes that gap.

## What it does

- Runs as **two cooperating Windows services under LocalSystem** so they can't be
  killed from your normal (non-elevated) account.
- **Respawns `FreedomBlocker.exe`** into your desktop session within seconds if it
  dies, and **restores Freedom's autostart** registry value if it's deleted.
- The two services **watch and restart each other** — killing one brings it back
  via its peer and the Service Control Manager.
- A **locked-down service DACL** means even an *elevated* `sc stop` / `taskkill` /
  Services console is denied, until you take ownership.
- A **delayed-unlock escape hatch** guarantees you can never permanently lock
  yourself out.

## Honest limitations

This is a **friction device, not a prison.** On an account where you are an
administrator, no user-mode software can be truly unbypassable — a determined
admin can always win via Safe Mode, taking ownership of the services, or kernel
tools. The goal is to turn *"one command kills it"* into *"elevate, take
ownership, re-permission, and reboot into Safe Mode"* — enough friction to beat
an impulse. If you need to stop, use the **unlock cooldown** below.

## Quick start (one click)

1. Double-click **`Install.bat`** and click **Yes** on the UAC prompt. It builds
   and installs everything.
2. Double-click **`Status.bat`** to confirm it's running.

To turn it off later: **`Unlock.bat`** → wait the cooldown → **`Uninstall.bat`**.

New here? Follow the **[step-by-step tutorial](TUTORIAL.md)**.

## Requirements

- Windows 10 (1903+) or Windows 11. Uses **.NET Framework 4.x**, which ships with
  Windows — no runtime download needed.
- Freedom installed (default path `C:\Program Files (x86)\Freedom\FreedomBlocker.exe`).

## Build

No .NET SDK or Visual Studio required — it compiles with the C# compiler built
into Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# -> build\FreedomGuardian.exe
```

Contributors with Visual Studio / the .NET Framework 4.8 Developer Pack can also
open `src\FreedomGuardian.csproj`.

## Install

Run from an **elevated** PowerShell ("Run as administrator"):

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
# optional overrides:
#   -FreedomExePath "C:\Program Files (x86)\Freedom\FreedomBlocker.exe"
#   -CooldownHours 8
```

This copies the binary to `C:\Program Files\FreedomGuardian`, registers both
services (auto-start + crash recovery), writes config to
`C:\ProgramData\FreedomGuardian\config.ini`, applies the locked-down DACL, and
adds a Windows Defender exclusion (the behaviour resembles malware persistence,
so AV may otherwise flag it).

## Usage

```
FreedomGuardian status    Show lock state, service health, and whether Freedom is running
FreedomGuardian unlock    Request release of protection (takes effect AFTER the cooldown)
FreedomGuardian relock    Cancel a pending unlock and re-arm protection
FreedomGuardian console   Run the guardian loop in the foreground (debugging)
```

`status`, `unlock` and `relock` do not require elevation.

## The escape hatch (how to stop / uninstall)

1. Run `FreedomGuardian unlock`. This only *requests* release.
2. Wait out the cooldown (default 2 hours). The **clock is started and held by the
   SYSTEM service** in a SYSTEM-only folder, so you can't back-date it to skip the
   wait.
3. When the cooldown elapses, the services restore normal permissions, disable
   themselves, and stop. Freedom is no longer force-respawned.
4. Run `uninstall.ps1` (elevated) to delete the services and files.

`relock` cancels a pending unlock at any time.

### Emergency removal

If you must remove it before the cooldown:

1. Boot Windows into **Safe Mode** (auto-start services don't run there).
2. In an elevated prompt: `sc delete FreedomGuardian` and `sc delete FreedomGuardianWatch`.
3. Delete `C:\Program Files\FreedomGuardian` and `C:\ProgramData\FreedomGuardian`, then reboot.

## How it works

| Component | Role |
|---|---|
| `FreedomGuardian` service | Respawns Freedom, heals the Run key, guards the Watch peer, reverts start-type tampering, enforces the unlock cooldown. |
| `FreedomGuardianWatch` service | Keeps the Guardian service running + auto-start (the paired-respawn layer). |
| Service DACL lockdown | Strips stop/change/delete/ownership from Administrators; SYSTEM keeps full control so it can self-heal and later restore. |
| SCM failure recovery | Auto-restarts on crash as a backstop. |
| `C:\ProgramData\FreedomGuardian\secure\` | SYSTEM-only folder holding the authoritative unlock cooldown clock. |

Source layout is under `src/`. Key files: `SessionLauncher.cs` (launches Freedom
into the interactive session from session 0), `ServiceControl.cs` (DACL/recovery/
self-heal), `UnlockStore.cs` (the cooldown), `Workers.cs` (the two loops).

## Disclaimer

Provided as-is under the MIT License. Installs system services and modifies
service permissions on your own machine; use at your own risk. Always keep the
Safe Mode removal steps above in mind before installing.
