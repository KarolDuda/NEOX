using Microsoft.Extensions.Logging;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Models;
using Tachyvex.Core.State;

namespace Tachyvex.Core.Engine;

/// <summary>
/// Jedyne miejsce, w którym zmiany systemowe są wykonywane: migawka -> zmiana -> weryfikacja odczytem -> historia.
/// Wszystkie operacje są serializowane (jedna zmiana naraz), żeby nie mieszać migawek.
/// </summary>
public sealed class TweakEngine
{
    private readonly TweakCatalog _catalog;
    private readonly IReadOnlyDictionary<TweakKind, ITweakHandler> _handlers;
    private readonly IReadOnlyList<IActionHandler> _actions;
    private readonly IStateStore _store;
    private readonly IElevationService _elevation;
    private readonly IClock _clock;
    private readonly ILogger<TweakEngine> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TweakEngine(TweakCatalog catalog, IEnumerable<ITweakHandler> handlers, IEnumerable<IActionHandler> actions,
        IStateStore store, IElevationService elevation, IClock clock, ILogger<TweakEngine> log)
    {
        _catalog = catalog;
        var map = new Dictionary<TweakKind, ITweakHandler>();
        foreach (var h in handlers)
            foreach (var k in h.Kinds) map[k] = h;
        _handlers = map;
        _actions = actions.ToList();
        _store = store;
        _elevation = elevation;
        _clock = clock;
        _log = log;
    }

    public TweakCatalog Catalog => _catalog;
    public bool IsElevated => _elevation.IsElevated;

    public event EventHandler? StateChanged;

    public bool IsApplied(string id) => _store.State.Snapshots.ContainsKey(id);

    public async Task<TweakStatus> GetStatusAsync(string id, TweakContext ctx, CancellationToken ct = default)
    {
        var t = _catalog.Get(id);
        if (t.IsAction) return TweakStatus.Unknown;
        if (!_handlers.TryGetValue(t.Kind, out var h)) return TweakStatus.Unknown;
        try { return await h.GetStatusAsync(t, ctx, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Could not read state of {Id}", id);
            return TweakStatus.Unknown;
        }
    }

    public async Task<IReadOnlyDictionary<string, TweakStatus>> GetAllStatusesAsync(TweakContext ctx, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TweakStatus>();
        foreach (var t in _catalog.All.Where(t => !t.IsAction))
        {
            ct.ThrowIfCancellationRequested();
            result[t.Id] = await GetStatusAsync(t.Id, ctx, ct).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<TweakResult> ApplyAsync(string id, TweakContext ctx, CancellationToken ct = default)
    {
        var t = _catalog.Get(id);
        if (t.IsAction) return TweakResult.Fail("This is a one-time action, not a setting.");
        if (t.RequiresAdmin && !_elevation.IsElevated)
            return TweakResult.Fail("This change requires running the app as administrator.");
        if (!_handlers.TryGetValue(t.Kind, out var handler))
            return TweakResult.Fail("No handler for kind " + t.Kind);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = _store.State;
            if (state.Snapshots.ContainsKey(id))
                return TweakResult.Ok("Already applied by NEOX.");

            TweakStatus before;
            try { before = await handler.GetStatusAsync(t, ctx, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { before = TweakStatus.Unknown; _log.LogDebug(ex, "status przed {Id}", id); }

            System.Text.Json.Nodes.JsonNode? snapshot;
            try
            {
                snapshot = await handler.ApplyAsync(t, ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Applying {Id} failed", id);
                await RecordAsync(t, HistoryAction.Apply, false, ex.Message, ct).ConfigureAwait(false);
                return TweakResult.Fail(ex.Message);
            }

            // Zapisz migawkę ZANIM cokolwiek innego się stanie, żeby cofnięcie zawsze było możliwe.
            state.Snapshots[id] = snapshot;
            await _store.SaveAsync(ct).ConfigureAwait(false);

            TweakStatus after;
            try { after = await handler.GetStatusAsync(t, ctx, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { after = TweakStatus.Unknown; _log.LogDebug(ex, "status po {Id}", id); }

            if (after == TweakStatus.NotApplied && before != TweakStatus.Applied)
            {
                // Weryfikacja nie potwierdziła zmiany: cofamy, zamiast zostawiać stan nieznany.
                _log.LogWarning("Weryfikacja {Id} nieudana, cofam", id);
                try { await handler.RevertAsync(t, snapshot, ctx, ct).ConfigureAwait(false); } catch (Exception ex) { _log.LogError(ex, "Rollback after failed verification of {Id}", id); }
                state.Snapshots.Remove(id);
                await _store.SaveAsync(ct).ConfigureAwait(false);
                const string msg = "The change was not confirmed by read-back (Windows or a policy may have rejected it). The previous state was restored.";
                await RecordAsync(t, HistoryAction.Apply, false, msg, ct).ConfigureAwait(false);
                return TweakResult.Fail(msg);
            }

            if (t.Restart != RestartRequirement.None && !state.PendingRestart.Contains(id))
                state.PendingRestart.Add(id);
            await RecordAsync(t, HistoryAction.Apply, true, "Applied", ct).ConfigureAwait(false);
            return TweakResult.Ok("Applied.", t.Restart);
        }
        finally { _gate.Release(); }
    }

    public async Task<TweakResult> RevertAsync(string id, TweakContext ctx, CancellationToken ct = default)
    {
        var t = _catalog.Get(id);
        if (!_handlers.TryGetValue(t.Kind, out var handler))
            return TweakResult.Fail("No handler for kind " + t.Kind);
        if (t.RequiresAdmin && !_elevation.IsElevated)
            return TweakResult.Fail("Reverting requires running the app as administrator.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = _store.State;
            if (!state.Snapshots.TryGetValue(id, out var snapshot))
                return TweakResult.Fail("No backup of this change was saved, so it cannot be reverted safely.");
            try
            {
                await handler.RevertAsync(t, snapshot, ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Reverting {Id} failed", id);
                await RecordAsync(t, HistoryAction.Revert, false, ex.Message, ct).ConfigureAwait(false);
                return TweakResult.Fail(ex.Message); // the snapshot stays, so you can retry
            }
            state.Snapshots.Remove(id);
            state.PendingRestart.Remove(id);
            await RecordAsync(t, HistoryAction.Revert, true, "Reverted", ct).ConfigureAwait(false);
            return TweakResult.Ok("Reverted.", t.Restart);
        }
        finally { _gate.Release(); }
    }

    public async Task<TweakResult> RunActionAsync(string id, TweakContext ctx, CancellationToken ct = default)
    {
        var t = _catalog.Get(id);
        if (!t.IsAction) return TweakResult.Fail("This is not an action.");
        if (t.RequiresAdmin && !_elevation.IsElevated)
            return TweakResult.Fail("This action requires running the app as administrator.");
        var handler = _actions.FirstOrDefault(a => a.CanHandle(t));
        if (handler is null) return TweakResult.Fail("No handler for this action.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                var msg = await handler.RunAsync(t, ctx, ct).ConfigureAwait(false);
                await RecordAsync(t, HistoryAction.Run, true, Truncate(msg), ct).ConfigureAwait(false);
                return TweakResult.Ok(msg);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Action {Id} failed", id);
                await RecordAsync(t, HistoryAction.Run, false, ex.Message, ct).ConfigureAwait(false);
                return TweakResult.Fail(ex.Message);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ClearPendingRestartAsync(CancellationToken ct = default)
    {
        _store.State.PendingRestart.Clear();
        await _store.SaveAsync(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RecordAsync(TweakDefinition t, HistoryAction action, bool ok, string message, CancellationToken ct)
    {
        var h = _store.State.History;
        h.Add(new HistoryEntry { Time = _clock.Now, TweakId = t.Id, Title = t.Title, Action = action, Success = ok, Message = Truncate(message) });
        if (h.Count > 2000) h.RemoveRange(0, h.Count - 2000);
        await _store.SaveAsync(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
