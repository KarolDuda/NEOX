using Tachyvex.Core.Engine;
using Tachyvex.Core.Health;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;

namespace Tachyvex.Windows;

/// <summary>Zbiera rzeczywiste dane systemu dla HealthScorer.</summary>
public sealed class HealthProbe
{
    private readonly TweakEngine _engine;
    private readonly ProfileService _profiles;
    private readonly IStateStore _store;
    private readonly StartupManager _startup;

    public HealthProbe(TweakEngine engine, ProfileService profiles, IStateStore store, StartupManager startup)
    {
        _engine = engine; _profiles = profiles; _store = store; _startup = startup;
    }

    public Task<HealthReport> ScanAsync() => Task.Run(() =>
    {
        var vols = SystemInfo.Volumes().Where(v => v.TotalBytes > 0).ToList();
        var worst = vols.Count == 0 ? 100 : vols.Min(v => v.FreeBytes * 100.0 / v.TotalBytes);
        var mem = SystemInfo.GlobalMemory();
        var os = SystemInfo.Os();
        var uptime = os is null ? 0 : Math.Max(0, (DateTime.Now - os.LastBoot).TotalDays);
        var startup = _startup.List().Count(e => e.Enabled);

        var safe = _profiles.BuiltIn().First(p => p.Id == "builtin.safe").TweakIds
            .Where(id => _engine.Catalog.Find(id) is { } t && (_engine.IsElevated || !t.RequiresAdmin)).ToList();
        var applied = safe.Count(_engine.IsApplied);

        return HealthScorer.Score(new HealthInput(worst, mem.LoadPercent, uptime, startup, safe.Count, applied, _store.State.PendingRestart.Count > 0));
    });
}
