# Architecture

Stack: C#, .NET 10, WinUI 3 (Windows App SDK), MVVM (CommunityToolkit.Mvvm), Microsoft.Extensions DI/Logging, Serilog file sink.

- `Neox.Core`: pure logic, no Windows APIs. Tweak catalog (`tweaks.json`), engine, profiles, Auto-Tune planner, health scorer, state store.
- `Neox.Windows`: everything that touches the system: registry, powercfg, services, scheduled tasks, bcdedit, network, monitoring, cleanup, elevation, updates.
- `Neox.App`: views, view-models, styles, composition animations (`Fx`).

Rules:
- The UI never touches the registry or powercfg; it talks to services and the engine.
- Every tweak: snapshot, apply, verify by read-back, history entry. Undo restores the snapshot. Data lives in `%LOCALAPPDATA%\Neox\`.
- Risk levels: Recommended, Caution, Experimental, High risk (separate confirmation, never in profiles).
- State is written atomically (temp file + replace) with a `.bak` copy; a corrupted file is kept aside and the backup is loaded.
- Monitoring polls only while its view is visible.
