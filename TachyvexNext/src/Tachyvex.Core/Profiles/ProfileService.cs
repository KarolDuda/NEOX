using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.State;

namespace Tachyvex.Core.Profiles;

public sealed record ProfileApplyReport(int Applied, int Skipped, int Failed, RestartRequirement Restart, IReadOnlyList<string> Lines);

/// <summary>Profile jednym kliknięciem (wbudowane i własne) oraz porównanie przed/po.</summary>
public sealed class ProfileService
{
    private readonly TweakCatalog _catalog;
    private readonly TweakEngine _engine;
    private readonly IStateStore _store;
    private readonly IRestorePointService _restorePoints;
    private readonly IClock _clock;

    public ProfileService(TweakCatalog catalog, TweakEngine engine, IStateStore store, IRestorePointService restorePoints, IClock clock)
    {
        _catalog = catalog; _engine = engine; _store = store; _restorePoints = restorePoints; _clock = clock;
    }

    /// <summary>Wbudowane profile są wyliczane z katalogu według poziomu ryzyka, nie z ręcznej listy.</summary>
    public IReadOnlyList<Profile> BuiltIn()
    {
        static bool Simple(TweakDefinition t) =>
            t.Kind is TweakKind.Reg or TweakKind.RegPerAdapter or TweakKind.PowerIdx or TweakKind.PowerPlan;
        static bool GameArea(TweakDefinition t) =>
            t.Category is TweakCategory.Performance or TweakCategory.Input or TweakCategory.Network;
        var safe = _catalog.All.Where(t => Simple(t) && GameArea(t) && t.Risk == RiskLevel.Recommended)
            .Select(t => t.Id).ToList();
        var gaming = _catalog.All.Where(t => Simple(t) && GameArea(t) && t.Risk is RiskLevel.Recommended or RiskLevel.Caution)
            .Select(t => t.Id).ToList();
        var privacy = _catalog.All.Where(t => !t.IsAction && t.Category == TweakCategory.Privacy && t.Risk == RiskLevel.Recommended)
            .Select(t => t.Id).ToList();
        return new[]
        {
            new Profile { Id = "builtin.safe", BuiltIn = true, Name = "Bezpieczny start",
                Description = "Tylko pozycje oznaczone jako zalecane w wydajności, input delay i sieci. Bez usług i zadań.", TweakIds = safe },
            new Profile { Id = "builtin.gaming", BuiltIn = true, Name = "Gaming",
                Description = "Zalecane oraz ostrożne zmiany wydajności, inputu i sieci. Bez usług, eksperymentalnych i wysokiego ryzyka.", TweakIds = gaming },
            new Profile { Id = "builtin.privacy", BuiltIn = true, Name = "Prywatność",
                Description = "Zalecane ustawienia prywatności (telemetria, reklamy, sugestie).", TweakIds = privacy },
        };
    }

    public IReadOnlyList<Profile> All() => BuiltIn().Concat(_store.State.CustomProfiles).ToList();

    public async Task<Profile> SaveCustomAsync(string name, IEnumerable<string> ids, string description = "", CancellationToken ct = default)
    {
        var p = new Profile { Name = name.Trim(), Description = description, TweakIds = ids.Where(i => _catalog.Find(i) is { IsAction: false }).Distinct().ToList() };
        _store.State.CustomProfiles.Add(p);
        await _store.SaveAsync(ct);
        return p;
    }

    public async Task DeleteCustomAsync(string id, CancellationToken ct = default)
    {
        _store.State.CustomProfiles.RemoveAll(p => p.Id == id);
        await _store.SaveAsync(ct);
    }

    public async Task<ProfileApplyReport> ApplyAsync(Profile profile, TweakContext ctx, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var lines = new List<string>();
        int ok = 0, skip = 0, fail = 0;
        var restart = RestartRequirement.None;

        if (_store.State.Settings.CreateRestorePointBeforeProfiles && _engine.IsElevated)
        {
            progress?.Report("Tworzenie punktu przywracania…");
            var rp = await _restorePoints.CreateAsync("TACHYVEX: " + profile.Name, ct);
            lines.Add(rp.Success ? "Punkt przywracania utworzony." : "Punkt przywracania pominięty: " + rp.Message);
        }

        foreach (var id in profile.TweakIds)
        {
            ct.ThrowIfCancellationRequested();
            var t = _catalog.Find(id);
            if (t is null) { skip++; lines.Add($"• {id}: brak w katalogu"); continue; }
            if (t.RequiresAdmin && !_engine.IsElevated) { skip++; lines.Add($"• {t.Title}: wymaga administratora"); continue; }
            progress?.Report(t.Title);
            var r = await _engine.ApplyAsync(id, ctx, ct);
            if (r.Success) { ok++; if (r.Restart > restart) restart = r.Restart; lines.Add($"✔ {t.Title}"); }
            else { fail++; lines.Add($"✖ {t.Title}: {r.Message}"); }
        }
        _store.State.History.Add(new HistoryEntry
        {
            Time = _clock.Now, TweakId = "profile:" + profile.Id, Title = "Profil: " + profile.Name,
            Action = HistoryAction.ProfileApply, Success = fail == 0, Message = $"{ok} zastosowano, {skip} pominięto, {fail} błędów"
        });
        await _store.SaveAsync(ct);
        return new ProfileApplyReport(ok, skip, fail, restart, lines);
    }

    public async Task<int> RevertAllAsync(TweakContext ctx, CancellationToken ct = default)
    {
        int n = 0;
        foreach (var id in _store.State.Snapshots.Keys.ToList())
            if ((await _engine.RevertAsync(id, ctx, ct)).Success) n++;
        return n;
    }

    // ---- porównanie przed/po ----
    public async Task<Baseline> CaptureBaselineAsync(string name, TweakContext ctx, CancellationToken ct = default)
    {
        var b = new Baseline { Name = name, Time = _clock.Now };
        foreach (var (id, st) in await _engine.GetAllStatusesAsync(ctx, ct)) b.Statuses[id] = st;
        _store.State.Baselines.Add(b);
        if (_store.State.Baselines.Count > 20) _store.State.Baselines.RemoveAt(0);
        await _store.SaveAsync(ct);
        return b;
    }

    public async Task<IReadOnlyList<(TweakDefinition Tweak, TweakStatus Before, TweakStatus Now)>> CompareAsync(Baseline baseline, TweakContext ctx, CancellationToken ct = default)
    {
        var now = await _engine.GetAllStatusesAsync(ctx, ct);
        var diff = new List<(TweakDefinition, TweakStatus, TweakStatus)>();
        foreach (var (id, before) in baseline.Statuses)
        {
            var t = _catalog.Find(id);
            if (t is null || !now.TryGetValue(id, out var cur)) continue;
            if (cur != before) diff.Add((t, before, cur));
        }
        return diff;
    }
}
