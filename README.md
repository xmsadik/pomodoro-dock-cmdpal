# Pomodoro Dock

A PowerToys Command Palette extension that puts a Pomodoro timer in the **Dock**.

- **In the Dock:** a live clock with a phase icon, e.g. `🍅 18:42` (focus), `☕ 03:10` (short break), `🌴 12:00` (long break), `⏸️ 18:42` (paused). The subtitle shows the phase and your place in the cycle, e.g. `Focus · 2/4`.
- **Click it:** a flyout with **Start / Pause / Resume**, **Skip to the next phase**, **Reset cycle**, today's totals (pomodoros, focus time, breaks), a **Last 7 days** summary, and **Settings**.
- **Notifications:** a Windows toast when a focus session or a break ends. The toast is scheduled with Windows when the phase starts, so it still appears if Command Palette is busy or the extension process was stopped.

## How the cycle works

Focus, short break, focus, short break, ... and after every *N* completed focus sessions (default 4) a long break; a finished long break starts a new cycle.

- **Skip** leaves the current phase without counting it. A skipped focus session is **not** counted: the `2/4` counter stays where it was and nothing is added to today's totals. Skipping a long break starts a new cycle.
- **Auto-start** (breaks on, focus off by default) starts the next phase straight away. This only chains while the extension was running. If the PC was asleep or the extension was stopped when a phase ended (more than 90 seconds late), that phase is credited to the day it ended and the next phase waits, ready, until you start it.
- **Changing settings mid-phase** affects only future phases; the running phase keeps its end time and is credited with the length it started with.
- Totals are kept for 90 days in `%LOCALAPPDATA%\PomodoroDock\history.json`, the timer state in `state.json` (so a restart or Reload does not lose a running timer).

## Settings

Command Palette -> *Pomodoro Dock* -> *Settings* (also in the flyout):

| Setting | Choices | Default |
|---|---|---|
| Focus | 15, 20, 25, 30, 35, 40, 45, 50, 55, 60 min | 25 |
| Short break | 3, 5, 10, 15 min | 5 |
| Long break | 10, 15, 20, 25, 30 min | 15 |
| Long break every | 2, 3, 4, 5, 6 pomodoros | 4 |
| Auto-start breaks | on / off | on |
| Auto-start focus | on / off | off |
| Notifications | on / off | on |

Windows **Focus Assist / Do Not Disturb** suppresses toasts like any other notification. Clicking a toast just launches the extension process (nothing visible happens).

## Requirements

- Windows 10 19041+ / Windows 11, PowerToys with Command Palette **0.9 or later** (Dock support)

## Installation

Download `PomodoroDockDev.cer` and the `.msix` for your PC from the [Releases page](https://github.com/xmsadik/pomodoro-dock-cmdpal/releases).

### 1. Turn on the Dock

1. Install or update [PowerToys](https://github.com/microsoft/PowerToys/releases) and make sure **Command Palette** is enabled in PowerToys Settings.
2. Open Command Palette (default <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>) -> **Settings** -> **Dock (Preview)** -> turn on **Enable Dock**.

### 2. Trust the certificate (once per machine)

The package is signed with a self-signed certificate, so Windows has to be told to trust it. In **PowerShell as Administrator**, in the folder with the files:

```powershell
Import-Certificate .\PomodoroDockDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

### 3. Install

```powershell
Add-AppxPackage .\PomodoroDock_<version>_x64.msix
```

Use the `arm64` package on Arm PCs (`$env:PROCESSOR_ARCHITECTURE` says `ARM64`).

### 4. Show it in the Dock

1. Open Command Palette and run **Reload** so it picks up the new extension.
2. The *Pomodoro Dock* band usually appears in the Dock by itself. If it doesn't, search for **Pomodoro Dock** in Command Palette, open its context menu and run **Pin to Dock**.

### Update / uninstall

Install a newer `.msix` the same way, then **Reload**. To remove:

```powershell
Get-AppxPackage PomodoroDock | Remove-AppxPackage
# optional, as Administrator: remove the trusted certificate
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=PomodoroDockDev' | Remove-Item
```

### Troubleshooting

| Symptom | Fix |
|---|---|
| `0x800B0109` / "the root certificate ... is not trusted" on install | Step 2 was skipped or not run as Administrator. |
| `0x80073CFB` / "a package with the same identity is already installed" | A development build is registered: `Get-AppxPackage PomodoroDock \| Remove-AppxPackage`, then install again. |
| Band doesn't appear | Check **Enable Dock** is on, run **Reload**, then use **Pin to Dock**. |
| No toast when a phase ends | Check Windows notification settings and Focus Assist; the **Notifications** setting must be on. Errors are logged to `%LOCALAPPDATA%\PomodoroDock\diag.log` (inside the package's `LocalCache` when installed as MSIX). |
| Clicking the band opens the palette instead of the flyout, or the clock stops updating | Command Palette host issues, see [Known host issues](#known-host-issues). **Reload** fixes both. |

## Known host issues

These are Command Palette bugs, not bugs in this extension:
- [#50367](https://github.com/microsoft/PowerToys/issues/50367): after the host releases an idle extension, clicking a band opens the palette instead of the flyout.
- [#49688](https://github.com/microsoft/PowerToys/issues/49688): bands stop repainting after roughly 41 hours of uptime.

Running **Reload** in Command Palette works around both.

## Build from source

Needs the .NET 10 SDK. A full Windows SDK / Visual Studio is **not** required.

```powershell
dotnet test tests\PomodoroDock.Tests -p:Platform=x64     # unit tests
.\scripts\dev-deploy.ps1                                 # build + register (Developer Mode on)
.\scripts\dev-deploy.ps1 -Remove                         # unregister
```

After deploying, run **Reload** in Command Palette.

### MSIX package

```powershell
.\scripts\pack.ps1 -Sign        # dist\...\PomodoroDock_<ver>_x64.msix + dist\PomodoroDockDev.cer
```

`-Platform ARM64` builds the Arm package. The first `-Sign` run creates a self-signed `CN=PomodoroDockDev` code-signing certificate in `Cert:\CurrentUser\My` and reuses it afterwards. Back it up with `.\scripts\backup-signing-cert.ps1 -Destination D:\backup`.

The icons are generated by `.\scripts\make-assets.ps1` (System.Drawing, no extra tools).

## Layout

```
src/PomodoroDock/        Command Palette extension (Dock band, flyout page, settings, toasts)
src/PomodoroDock.Core/   Plain .NET library: timer engine, state/history stores, formatting
tests/PomodoroDock.Tests xUnit tests for Core
scripts/                 dev-deploy.ps1, pack.ps1, make-assets.ps1, backup-signing-cert.ps1
```

## License

[MIT](LICENSE). Parts derived from the PowerToys extension template are (c) Microsoft, MIT; see [NOTICE](NOTICE).
