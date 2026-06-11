# Freedom Guardian — Step-by-Step Tutorial

This guide walks you through installing, using, and removing Freedom Guardian.
No coding or command-line knowledge is required — you can do everything by
double-clicking the `.bat` files.

> **What this is:** a self-control aid that makes it hard for an impulsive you to
> kill the Freedom blocker, while always leaving a safe (if slow) way out.
>
> **What this is not:** unbreakable. As an administrator you can always remove it
> via Safe Mode. The point is *friction*, not a prison.

---

## Before you start

- You need **Windows 10 (version 1903+) or Windows 11**. Nothing else to install —
  it uses the .NET that already ships with Windows.
- **Freedom** should be installed (the normal way, from freedom.to).
- You should be able to click **"Yes"** on the Windows "Do you want to allow this
  app to make changes?" (UAC) prompt — i.e. use an administrator account.

---

## Step 1 — Install (one click)

1. Double-click **`Install.bat`**.
2. Click **Yes** on the blue "User Account Control" prompt that appears.
3. A window opens, builds the app, installs two services, and prints
   `Setup complete.` Press **Enter** to close it.

That's it. Freedom Guardian is now running and will start automatically every
time you boot.

> **Want a longer cooldown?** (see Step 4 for what this means.) Instead of
> double-clicking, run this in PowerShell to set, say, an 8-hour cooldown:
> ```powershell
> powershell -ExecutionPolicy Bypass -File setup.ps1 -CooldownHours 8
> ```

---

## Step 2 — Check it's working

1. Double-click **`Status.bat`**. You should see something like:

   ```
   Freedom Guardian status
     Lock state      : Locked
     Guardian service: running (auto)
     Watch service   : running (auto)
     Freedom running : yes
   ```

   `Locked` means protection is active. Both services `running (auto)` is healthy.

2. **Try to beat it** (optional, to see it work): end `FreedomBlocker.exe` process from the command line. 
	Within a few seconds it comes back on its own. Try to
   stop the `FreedomGuardian` service — Windows refuses, even as an administrator.

---

## Step 3 — Daily use

You don't have to do anything day to day. It runs quietly in the background,
keeps Freedom alive, and restarts itself if anything tries to shut it down.

- See the current state any time: **`Status.bat`**.

---

## Step 4 — When you genuinely need to turn it off (the cooldown)

Turning protection off is **intentionally slow** — that delay is the whole point.

1. Double-click **`Unlock.bat`** (or run `FreedomGuardian unlock`). This *requests*
   release and starts a countdown (default **2 hours**).
2. Go do something else. The countdown is held by the system, so you can't skip it
   by changing the clock.
3. Check progress with **`Status.bat`** — it shows `Pending` and the time
   remaining.
4. When the cooldown finishes, the services switch themselves off and stop
   force-restarting Freedom.

**Changed your mind during the wait?** Double-click **`Relock.bat`** to cancel the
unlock and re-arm protection instantly.

---

## Step 5 — Uninstall completely

1. First do **Step 4** (run `Unlock.bat` and wait out the cooldown). This is
   required — while protection is active the uninstaller is deliberately blocked.
2. Once the cooldown has elapsed, double-click **`Uninstall.bat`** and click
   **Yes** on the UAC prompt.
3. The services and files are removed. Done.

---

## Emergency removal (can't wait for the cooldown)

If you ever need it gone immediately:

1. **Boot into Safe Mode.** (Settings → System → Recovery → Restart now under
   "Advanced startup", then Troubleshoot → Advanced options → Startup Settings →
   Restart → press **4** for Safe Mode.) The guardian does not run in Safe Mode.
2. Open **Command Prompt as administrator** and run:
   ```
   sc delete FreedomGuardian
   sc delete FreedomGuardianWatch
   ```
3. Delete these two folders:
   - `C:\Program Files\FreedomGuardian`
   - `C:\ProgramData\FreedomGuardian`
4. Restart normally.

---

## Troubleshooting

- **Windows Defender flagged it / it disappeared after install.**
  The "respawn and protect itself" behaviour looks like malware to antivirus, even
  though it isn't. The installer tries to add a Defender exclusion automatically.
  If your AV still removes it, add an exclusion for
  `C:\Program Files\FreedomGuardian\FreedomGuardian.exe` and re-run `Install.bat`.

- **`Status.bat` says "not installed".**
  Installation didn't complete — re-run `Install.bat` and make sure you click
  **Yes** on the UAC prompt.

- **Freedom is installed somewhere unusual.**
  Install with the correct path:
  ```powershell
  powershell -ExecutionPolicy Bypass -File setup.ps1 -FreedomExePath "D:\Apps\Freedom\FreedomBlocker.exe"
  ```

- **Where are the logs?**
  `C:\ProgramData\FreedomGuardian\guardian.log`.
