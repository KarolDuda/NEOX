using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;
using Xunit;

namespace Tachyvex.Tests;

internal sealed class FakeHandler : ITweakHandler
{
    public Dictionary<string, bool> Live = new();
    public bool IgnoreApply;
    public bool ThrowOnApply;
    public IReadOnlyCollection<TweakKind> Kinds { get; } = Enum.GetValues<TweakKind>().Where(k => k != TweakKind.Action).ToArray();

    public Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct) =>
        Task.FromResult(Live.GetValueOrDefault(t.Id) ? TweakStatus.Applied : TweakStatus.NotApplied);

    public Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        if (ThrowOnApply) throw new TweakException("boom");
        var before = Live.GetValueOrDefault(t.Id);
        if (!IgnoreApply) Live[t.Id] = true;
        return Task.FromResult<JsonNode?>(new JsonObject { ["was"] = before });
    }

    public Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        Live[t.Id] = snapshot!["was"]!.GetValue<bool>();
        return Task.CompletedTask;
    }
}

internal sealed class FakeAction : IActionHandler
{
    public bool CanHandle(TweakDefinition t) => t.IsAction;
    public Task<string> RunAsync(TweakDefinition t, TweakContext c, CancellationToken ct) => Task.FromResult("done " + t.Id);
}

internal sealed class Elevation(bool on) : IElevationService { public bool IsElevated { get; } = on; }

internal sealed class FakeRestore : IRestorePointService
{
    public int Calls;
    public Task<TweakResult> CreateAsync(string d, CancellationToken ct) { Calls++; return Task.FromResult(TweakResult.Ok("ok")); }
}

public class EngineTests
{
    private static (TweakEngine e, FakeHandler h, InMemoryStateStore s, TweakCatalog c) Make(bool admin = true)
    {
        var c = TweakCatalog.LoadEmbedded();
        var h = new FakeHandler();
        var s = new InMemoryStateStore();
        var e = new TweakEngine(c, new[] { h }, new[] { new FakeAction() }, s, new Elevation(admin), new SystemClock(), NullLogger<TweakEngine>.Instance);
        return (e, h, s, c);
    }

    [Fact]
    public async Task Apply_then_revert_restores_previous_state_and_logs_history()
    {
        var (e, h, s, _) = Make();
        var r = await e.ApplyAsync("game_mode", new TweakContext());
        Assert.True(r.Success);
        Assert.True(h.Live["game_mode"]);
        Assert.True(e.IsApplied("game_mode"));

        var back = await e.RevertAsync("game_mode", new TweakContext());
        Assert.True(back.Success);
        Assert.False(h.Live["game_mode"]);
        Assert.False(e.IsApplied("game_mode"));
        Assert.Equal(2, s.State.History.Count);
        Assert.All(s.State.History, x => Assert.True(x.Success));
    }

    [Fact]
    public async Task Apply_is_refused_when_admin_needed_and_not_elevated()
    {
        var (e, h, _, c) = Make(admin: false);
        var admin = c.All.First(t => t.RequiresAdmin && !t.IsAction);
        var r = await e.ApplyAsync(admin.Id, new TweakContext());
        Assert.False(r.Success);
        Assert.False(h.Live.ContainsKey(admin.Id));
    }

    [Fact]
    public async Task Failed_verification_rolls_back_and_keeps_no_snapshot()
    {
        var (e, h, s, _) = Make();
        h.IgnoreApply = true; // zmiana "nie weszła"
        var r = await e.ApplyAsync("game_mode", new TweakContext());
        Assert.False(r.Success);
        Assert.False(e.IsApplied("game_mode"));
        Assert.Contains(s.State.History, x => !x.Success);
    }

    [Fact]
    public async Task Handler_exception_is_reported_not_thrown()
    {
        var (e, h, _, _) = Make();
        h.ThrowOnApply = true;
        var r = await e.ApplyAsync("game_mode", new TweakContext());
        Assert.False(r.Success);
        Assert.Equal("boom", r.Message);
        Assert.False(e.IsApplied("game_mode"));
    }

    [Fact]
    public async Task Revert_without_snapshot_fails_safely()
    {
        var (e, _, _, _) = Make();
        var r = await e.RevertAsync("game_mode", new TweakContext());
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Restart_requirement_is_tracked_until_cleared()
    {
        var (e, _, s, c) = Make();
        var t = c.All.First(x => x.Restart != RestartRequirement.None && !x.IsAction && x.Kind == TweakKind.Reg);
        await e.ApplyAsync(t.Id, new TweakContext());
        Assert.Contains(t.Id, s.State.PendingRestart);
        await e.ClearPendingRestartAsync();
        Assert.Empty(s.State.PendingRestart);
    }

    [Fact]
    public async Task Actions_run_and_are_logged()
    {
        var (e, _, s, _) = Make();
        var r = await e.RunActionAsync("dns_flush", new TweakContext());
        Assert.True(r.Success);
        Assert.Single(s.State.History);
    }

    [Fact]
    public async Task Profile_apply_counts_results_and_creates_restore_point_once()
    {
        var (e, _, s, c) = Make();
        var rp = new FakeRestore();
        var ps = new ProfileService(c, e, s, rp, new SystemClock());
        var profile = ps.BuiltIn().First(p => p.Id == "builtin.safe");
        Assert.NotEmpty(profile.TweakIds);
        var rep = await ps.ApplyAsync(profile, new TweakContext());
        Assert.Equal(profile.TweakIds.Count, rep.Applied + rep.Skipped + rep.Failed);
        Assert.Equal(1, rp.Calls);
        Assert.Equal(0, rep.Failed);
    }

    [Fact]
    public async Task Builtin_profiles_never_include_services_tasks_or_high_risk()
    {
        var (e, _, s, c) = Make();
        var ps = new ProfileService(c, e, s, new FakeRestore(), new SystemClock());
        foreach (var p in ps.BuiltIn())
            foreach (var id in p.TweakIds)
            {
                var t = c.Get(id);
                Assert.NotEqual(TweakKind.Service, t.Kind);
                Assert.NotEqual(TweakKind.Task, t.Kind);
                Assert.True(t.Risk is RiskLevel.Recommended or RiskLevel.Caution, id);
            }
    }

    [Fact]
    public async Task Baseline_comparison_reports_only_changed_items()
    {
        var (e, _, s, c) = Make();
        var ps = new ProfileService(c, e, s, new FakeRestore(), new SystemClock());
        var b = await ps.CaptureBaselineAsync("przed", new TweakContext());
        await e.ApplyAsync("game_mode", new TweakContext());
        var diff = await ps.CompareAsync(b, new TweakContext());
        var d = Assert.Single(diff);
        Assert.Equal("game_mode", d.Tweak.Id);
        Assert.Equal(TweakStatus.NotApplied, d.Before);
        Assert.Equal(TweakStatus.Applied, d.Now);
    }
}

public class StateStoreTests
{
    [Fact]
    public async Task Roundtrip_preserves_snapshots_and_history_and_recovers_from_corruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tvx-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "state.json");
        var store = new JsonStateStore(path, NullLogger<JsonStateStore>.Instance);
        await store.LoadAsync();
        store.State.Snapshots["x"] = new JsonObject { ["a"] = 1 };
        store.State.History.Add(new HistoryEntry { TweakId = "x", Title = "t", Success = true });
        await store.SaveAsync();
        await store.SaveAsync(); // druga zapis tworzy .bak

        var again = new JsonStateStore(path, NullLogger<JsonStateStore>.Instance);
        await again.LoadAsync();
        Assert.Equal(1, again.State.Snapshots["x"]!["a"]!.GetValue<int>());
        Assert.Single(again.State.History);

        await File.WriteAllTextAsync(path, "{ to nie jest json");
        var broken = new JsonStateStore(path, NullLogger<JsonStateStore>.Instance);
        await broken.LoadAsync();
        Assert.Single(broken.State.History); // odzyskane z .bak
        Directory.Delete(dir, true);
    }
}
