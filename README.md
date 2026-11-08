# NEOX 3.0 RECODE: Windows optimizer (C# · .NET 10 · WinUI 3)

NEOX tunes Windows 10/11 for gaming. 253 documented tweaks with benefits and side effects, one-click profiles, undo for every change, network tools, monitoring, startup/service/process managers and disk analysis.

## Install
Download `Neox_Setup.exe` from the **Releases** page of this repository. It is built by GitHub Actions on every push to `main`. The installer is not code-signed, so SmartScreen may warn you (More info → Run anyway). NEOX updates itself in the background when auto-update is enabled.

## Safety
- No critical services (Windows Update, Defender, Firewall, EventLog, RPC) and no keys that disable Defender or updates. Tests enforce this.
- Every change: full snapshot before, read-back after, automatic rollback if Windows did not accept it.
- High-risk items need a separate confirmation and are never part of any profile.
- Starts without administrator rights; elevation only on request.

## Features
- **Auto-Tune**: scans hardware (laptop/desktop, RAM, SSD, GPU), picks only safe changes, shows the plan with reasons, optionally benchmarks before and after (changes under the noise threshold are not reported as gains), and undoes everything with one button. It does not promise FPS.
- **System health**: a 0–100 score from explicit rules (disk, uptime, startup, applied tweaks, RAM) with Fix buttons.
- **Game Boost**: switches power plan and process priority while a listed game runs; the previous plan is stored in `state.json`, so it is restored even after a crash.
- **Profile export/import** (JSON), validated: unknown, service, experimental and high-risk entries are rejected.

## Layout
```
src/Neox.Core       catalog, engine, profiles, state store (no Windows APIs)
src/Neox.Windows    registry, powercfg, services, bcdedit, network, monitoring, cleanup
src/Neox.App        WinUI 3 UI (MVVM, DI, Serilog)
tests/              xUnit (Core and Windows layer)
installer/          Inno Setup
docs/               architecture, test plan
```
Logs: `%LOCALAPPDATA%\Neox\logs`. State and backups: `%LOCALAPPDATA%\Neox\state.json`.

Author and publisher: spinprint (spinprint.pl).
