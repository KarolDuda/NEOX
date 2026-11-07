using Microsoft.Extensions.Logging.Abstractions;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.AutoTune;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Health;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;
using Xunit;

namespace Tachyvex.Tests;

internal sealed class FakeScanner(HardwareProfile hw) : IHardwareScanner
{
    public Task<HardwareProfile> ScanAsync(CancellationToken ct = default) => Task.FromResult(hw);
}

internal sealed class FakeBench : IBenchmarkSuite
{
    public int Runs;
    public BenchmarkResult[] Results = { new(100, 100, 1.0, 2.0), new(120, 120, 0.8, 1.5) };
    public Task<BenchmarkResult> RunAsync(IProgress<string>? p, CancellationToken ct = default) => Task.FromResult(Results[Math.Min(Runs++, Results.Length - 1)]);
}

public class AutoTuneTests
{
    private static readonly HardwareProfile Desktop = new(8, 16, 32L << 30, false, true, true, 26100);
    private static readonly HardwareProfile Laptop = new(4, 8, 8L << 30, true, false, false, 26100);

    private static AutoTunePlanner Planner() => new(TweakCatalog.LoadEmbedded());

    [Fact]
    public void Plan_only_contains_safe_simple_tweaks_that_exist()
    {
        var c = TweakCatalog.LoadEmbedded();
        var plan = Planner().Plan(Desktop, elevated: true);
        Assert.NotEmpty(plan.Planned);
        foreach (var p in plan.Planned)
        {
            var t = c.Get(p.Id);
            Assert.False(t.IsAction);
            Assert.True(t.Risk is RiskLevel.Recommended or RiskLevel.Caution);
            Assert.True(t.Kind is TweakKind.Reg or TweakKind.RegPerAdapter or TweakKind.PowerIdx or TweakKind.PowerPlan);
        }
    }

    [Fact]
    public void Laptop_keeps_battery_features_untouched()
    {
        var ids = Planner().Plan(Laptop, true).Planned.Select(p => p.Id).ToHashSet();
        Assert.DoesNotContain("cpu_min", ids);
        Assert.DoesNotContain("core_parking", ids);
        Assert.DoesNotContain("powerPlan", ids);
    }

    [Fact]
    public void Hardware_dependent_rules_are_applied()
    {
        var low = Planner().Plan(Laptop, true);
        Assert.DoesNotContain(low.Planned, p => p.Id is "prefetch_off" or "nopaging_exec");
        var big = Planner().Plan(Desktop, true);
        Assert.Contains(big.Planned, p => p.Id == "nopaging_exec");
    }

    [Fact]
    public void Admin_tweaks_are_skipped_when_not_elevated_and_ultimate_is_never_planned()
    {
        var c = TweakCatalog.LoadEmbedded();
        var plan = Planner().Plan(Desktop, elevated: false);
        Assert.All(plan.Planned, p => Assert.False(c.Get(p.Id).RequiresAdmin));
        Assert.DoesNotContain(plan.Planned, p => p.Id == "ultimate_plan");
    }

    [Fact]
    public void Already_applied_tweaks_are_not_planned_again()
    {
        var plan = Planner().Plan(Desktop, true, id => id == "game_mode");
        Assert.DoesNotContain(plan.Planned, p => p.Id == "game_mode");
        Assert.Contains(plan.Skipped, p => p.Id == "game_mode");
    }

    [Fact]
    public void Benchmark_changes_below_threshold_are_noise()
    {
        var d = BenchmarkComparer.Compare(new(100, 100, 1.0, 2.0), new(102, 98, 1.05, 2.1));
        Assert.All(d, x => Assert.Equal(Verdict.Noise, x.Verdict));
    }

    [Fact]
    public void Benchmark_direction_depends_on_metric_type()
    {
        var d = BenchmarkComparer.Compare(new(100, 100, 1.0, 2.0), new(130, 70, 0.5, 4.0));
        Assert.Equal(Verdict.Better, d[0].Verdict);  // CPU w górę
        Assert.Equal(Verdict.Worse, d[1].Verdict);   // RAM w dół
        Assert.Equal(Verdict.Better, d[2].Verdict);  // opóźnienie w dół
        Assert.Equal(Verdict.Worse, d[3].Verdict);   // opóźnienie w górę
    }

    [Fact]
    public async Task Service_applies_plan_measures_and_can_undo()
    {
        var c = TweakCatalog.LoadEmbedded();
        var h = new FakeHandler();
        var s = new InMemoryStateStore();
        var e = new TweakEngine(c, new[] { h }, new[] { new FakeAction() }, s, new Elevation(true), new SystemClock(), NullLogger<TweakEngine>.Instance);
        var restore = new FakeRestore();
        var profiles = new ProfileService(c, e, s, restore, new SystemClock());
        var bench = new FakeBench();
        var svc = new AutoTuneService(new FakeScanner(Desktop), bench, new AutoTunePlanner(c), profiles, e, s, new SystemClock());

        var plan = await svc.PrepareAsync();
        var rep = await svc.RunAsync(plan, new TweakContext(), measure: true);

        Assert.Equal(plan.Planned.Count, rep.AppliedIds.Count);
        Assert.Equal(2, bench.Runs);
        Assert.Equal(4, rep.Deltas.Count);
        Assert.Equal(1, restore.Calls);
        Assert.NotNull(s.State.Settings.LastAutoTune);

        var undone = await svc.UndoAsync(rep.AppliedIds, new TweakContext());
        Assert.Equal(rep.AppliedIds.Count, undone);
        Assert.All(rep.AppliedIds, id => Assert.False(e.IsApplied(id)));
    }

    [Fact]
    public async Task Service_without_measure_does_not_benchmark()
    {
        var c = TweakCatalog.LoadEmbedded();
        var s = new InMemoryStateStore();
        var e = new TweakEngine(c, new[] { new FakeHandler() }, new[] { new FakeAction() }, s, new Elevation(true), new SystemClock(), NullLogger<TweakEngine>.Instance);
        var bench = new FakeBench();
        var svc = new AutoTuneService(new FakeScanner(Desktop), bench, new AutoTunePlanner(c), new ProfileService(c, e, s, new FakeRestore(), new SystemClock()), e, s, new SystemClock());
        var rep = await svc.RunAsync(await svc.PrepareAsync(), new TweakContext(), measure: false);
        Assert.Equal(0, bench.Runs);
        Assert.Empty(rep.Deltas);
    }
}

public class HealthScorerTests
{
    [Fact]
    public void Perfect_system_scores_100()
    {
        var r = HealthScorer.Score(new(60, 40, 1, 3, 10, 10, false));
        Assert.Equal(100, r.Score);
        Assert.All(r.Checks, c => Assert.Equal(FixKind.None, c.Fix));
    }

    [Fact]
    public void Problems_lower_score_and_offer_fixes()
    {
        var r = HealthScorer.Score(new(5, 90, 20, 15, 10, 0, true));
        Assert.True(r.Score < 25);
        Assert.Contains(r.Checks, c => c.Fix == FixKind.ApplySafeProfile);
        Assert.Contains(r.Checks, c => c.Fix == FixKind.OpenDisk);
        Assert.Contains(r.Checks, c => c.Fix == FixKind.OpenStartup);
    }

    [Fact]
    public void Score_is_always_within_bounds()
    {
        foreach (var disk in new[] { 0.0, 15, 99 })
            foreach (var applied in new[] { 0, 5, 10 })
            {
                var r = HealthScorer.Score(new(disk, 50, 3, 5, 10, applied, false));
                Assert.InRange(r.Score, 0, 100);
            }
    }

    [Fact]
    public void Zero_attainable_tweaks_does_not_divide_by_zero()
    {
        var r = HealthScorer.Score(new(50, 50, 1, 1, 0, 0, false));
        Assert.Equal(100, r.Score);
    }
}

public class ProfileTransferTests
{
    private static ProfileTransfer T() => new(TweakCatalog.LoadEmbedded());

    [Fact]
    public void Export_then_import_roundtrips()
    {
        var p = new Profile { Name = "Mój", Description = "opis", TweakIds = { "game_mode", "game_dvr" } };
        var r = T().Import(T().Export(p));
        Assert.True(r.Success);
        Assert.Equal("Mój", r.Profile!.Name);
        Assert.Equal(new[] { "game_mode", "game_dvr" }, r.Profile.TweakIds);
        Assert.Equal(0, r.Dropped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nie json")]
    [InlineData("{\"format\":\"inny\",\"tweaks\":[]}")]
    [InlineData("{\"format\":\"tachyvex-profile\"}")]
    public void Garbage_is_rejected(string json) => Assert.False(T().Import(json).Success);

    [Fact]
    public void Unknown_ids_and_risky_tweaks_are_dropped()
    {
        var c = TweakCatalog.LoadEmbedded();
        var risky = c.All.First(t => !t.IsAction && t.Risk is RiskLevel.HighRisk or RiskLevel.Experimental).Id;
        var json = $"{{\"format\":\"tachyvex-profile\",\"name\":\"x\",\"tweaks\":[\"game_mode\",\"nie_istnieje\",\"{risky}\",\"game_mode\",5]}}";
        var r = T().Import(json);
        Assert.True(r.Success);
        Assert.Equal(new[] { "game_mode" }, r.Profile!.TweakIds);
        Assert.Equal(4, r.Dropped);
    }

    [Fact]
    public void Oversized_file_is_rejected()
    {
        Assert.False(T().Import(new string(' ', ProfileTransfer.MaxBytes + 1)).Success);
    }
}
