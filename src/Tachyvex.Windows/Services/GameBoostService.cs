using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tachyvex.Core.State;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.Windows;

/// <summary>
/// Game Boost: gdy uruchomi się gra z Twojej listy, przełącza plan zasilania na wysoką wydajność i podnosi priorytet procesu gry.
/// Po zamknięciu gry przywraca poprzedni plan. Poprzedni plan jest zapisany na dysku, więc odzyskamy go nawet po awarii programu.
/// </summary>
public sealed class GameBoostService : IDisposable
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private readonly IStateStore _store;
    private readonly IProcessRunner _runner;
    private readonly ILogger<GameBoostService> _log;
    private Timer? _timer;
    private int _busy;
    private int _boostedPid;

    public event Action<string>? StatusChanged;
    public string Status { get; private set; } = "Wyłączony";
    public bool IsRunning => _timer is not null;

    public GameBoostService(IStateStore store, IProcessRunner runner, ILogger<GameBoostService> log)
    {
        _store = store; _runner = runner; _log = log;
    }

    public void Start()
    {
        if (_timer is not null) return;
        SetStatus("Czekam na uruchomienie gry");
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(4));
    }

    public async Task StopAsync()
    {
        _timer?.Dispose(); _timer = null;
        await RestoreAsync();
        SetStatus("Wyłączony");
    }

    private IEnumerable<string> WatchedNames()
    {
        var paths = _store.State.Games.Select(g => g.ExePath).Append(_store.State.Settings.GameExePath);
        return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFileNameWithoutExtension(p!.Trim().Trim('"')))
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            Process? game = null;
            foreach (var n in WatchedNames())
            {
                var found = Process.GetProcessesByName(n);
                if (found.Length > 0) { game = found[0]; foreach (var extra in found.Skip(1)) extra.Dispose(); break; }
            }

            if (game is not null)
            {
                if (_boostedPid != game.Id) await BoostAsync(game);
                game.Dispose();
            }
            else if (_boostedPid != 0 || _store.State.Settings.GameBoostRestoreScheme is not null)
            {
                await RestoreAsync();
                SetStatus("Czekam na uruchomienie gry");
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Game Boost: błąd cyklu"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private async Task BoostAsync(Process game)
    {
        var active = await ActiveSchemeAsync();
        if (active is not null && !active.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase)
            && _store.State.Settings.GameBoostRestoreScheme is null)
        {
            _store.State.Settings.GameBoostRestoreScheme = active;
            await _store.SaveAsync();
            var r = await _runner.RunAsync(new[] { "powercfg", "/setactive", HighPerformance });
            if (!r.Success) { _log.LogInformation("Game Boost: plan wysokiej wydajności niedostępny: {M}", r.FirstLine); _store.State.Settings.GameBoostRestoreScheme = null; await _store.SaveAsync(); }
        }
        try { game.PriorityClass = ProcessPriorityClass.High; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { _log.LogInformation("Game Boost: nie można zmienić priorytetu: {M}", ex.Message); }
        _boostedPid = game.Id;
        SetStatus($"Aktywny: {game.ProcessName}");
    }

    /// <summary>Przywraca poprzedni plan zasilania (też po awarii: wartość jest w state.json).</summary>
    public async Task RestoreAsync()
    {
        _boostedPid = 0;
        var prev = _store.State.Settings.GameBoostRestoreScheme;
        if (prev is null) return;
        await _runner.RunAsync(new[] { "powercfg", "/setactive", prev });
        _store.State.Settings.GameBoostRestoreScheme = null;
        await _store.SaveAsync();
    }

    private async Task<string?> ActiveSchemeAsync()
    {
        var r = await _runner.RunAsync(new[] { "powercfg", "/getactivescheme" });
        var m = GuidRx.Match(r.Output);
        return r.Success && m.Success ? m.Value : null;
    }

    private void SetStatus(string s) { Status = s; StatusChanged?.Invoke(s); }

    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
