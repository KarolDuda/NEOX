using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;

namespace Tachyvex.Core.AutoTune;

public sealed record AutoTuneReport(
    AutoTunePlan Plan,
    ProfileApplyReport Apply,
    BenchmarkResult? Before,
    BenchmarkResult? After,
    IReadOnlyList<MetricDelta> Deltas,
    IReadOnlyList<string> AppliedIds);

/// <summary>Skanuje sprzęt, planuje, mierzy przed i po, stosuje przez ProfileService (z punktem przywracania) i pozwala wszystko cofnąć.</summary>
public sealed class AutoTuneService
{
    private readonly IHardwareScanner _scanner;
    private readonly IBenchmarkSuite _bench;
    private readonly AutoTunePlanner _planner;
    private readonly ProfileService _profiles;
    private readonly TweakEngine _engine;
    private readonly IStateStore _store;
    private readonly IClock _clock;

    public AutoTuneService(IHardwareScanner scanner, IBenchmarkSuite bench, AutoTunePlanner planner, ProfileService profiles,
        TweakEngine engine, IStateStore store, IClock clock)
    {
        _scanner = scanner; _bench = bench; _planner = planner; _profiles = profiles; _engine = engine; _store = store; _clock = clock;
    }

    public async Task<AutoTunePlan> PrepareAsync(CancellationToken ct = default)
    {
        var hw = await _scanner.ScanAsync(ct);
        return _planner.Plan(hw, _engine.IsElevated, _engine.IsApplied);
    }

    public async Task<AutoTuneReport> RunAsync(AutoTunePlan plan, TweakContext ctx, bool measure, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        BenchmarkResult? before = null, after = null;
        if (measure)
        {
            progress?.Report("Pomiar przed zmianami…");
            before = await _bench.RunAsync(progress, ct);
        }

        var profile = new Profile { Id = "autotune", Name = "Auto-Tune", TweakIds = plan.Planned.Select(p => p.Id).ToList() };
        var apply = await _profiles.ApplyAsync(profile, ctx, progress, ct);
        var appliedIds = plan.Planned.Select(p => p.Id).Where(_engine.IsApplied).ToList();

        IReadOnlyList<MetricDelta> deltas = Array.Empty<MetricDelta>();
        if (measure && appliedIds.Count > 0)
        {
            progress?.Report("Pomiar po zmianach…");
            await Task.Delay(1500, ct); // chwila na ustabilizowanie systemu
            after = await _bench.RunAsync(progress, ct);
            deltas = BenchmarkComparer.Compare(before!, after);
        }

        _store.State.Settings.LastAutoTune = _clock.Now;
        await _store.SaveAsync(ct);
        return new AutoTuneReport(plan, apply, before, after, deltas, appliedIds);
    }

    public async Task<int> UndoAsync(IEnumerable<string> ids, TweakContext ctx, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var id in ids)
            if (_engine.IsApplied(id) && (await _engine.RevertAsync(id, ctx, ct)).Success) n++;
        return n;
    }
}
