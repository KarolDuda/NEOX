# Testing

- `tests/Neox.Tests` (xUnit, cross-platform): catalog integrity (no critical services, no Defender/update killers, unique ids), engine (snapshot, verify, rollback, history), Auto-Tune planner, benchmark comparer, health scorer, profile import validation.
- `tests/Neox.Tests.Windows` (Windows only): registry, file cleaner and hardware scanner against the real system.
- CI (`.github/workflows/build.yml`) builds, runs both suites, publishes self-contained x64, builds the Inno Setup installer and publishes `Neox_Setup.exe` to Releases.
