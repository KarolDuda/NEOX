using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Tachyvex.App.Services;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;

namespace Tachyvex.App.ViewModels;

public static class ThemeBrushes
{
    public static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>Jedna karta optymalizacji w liście.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly TweakEngine _engine;
    private readonly DialogService _dialogs;
    private readonly AppContextService _ctx;
    private readonly Action<string> _notify;
    private Action? _changed;

    /// <summary>Wywoływane tylko po udanym zastosowaniu przez użytkownika (do animacji).</summary>
    public event EventHandler? Applied;

    public TweakDefinition Tweak { get; }

    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public TweakItemViewModel(TweakDefinition t, TweakEngine engine, DialogService dialogs, AppContextService ctx, Action<string> notify, Action? changed = null)
    {
        Tweak = t; _engine = engine; _dialogs = dialogs; _ctx = ctx; _notify = notify;
        IsApplied = engine.IsApplied(t.Id);
        UpdateStatusText();
        _changed = changed;
    }

    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string Consequences => Tweak.IsAction ? "" : "Wady / skutki: " + Tweak.Consequences;
    public Visibility ConsequencesVisibility => Tweak.IsAction ? Visibility.Collapsed : Visibility.Visible;
    public string AdminText => Tweak.RequiresAdmin ? "Wymaga administratora" : "";
    public string RestartText => Tweak.Restart switch { RestartRequirement.Restart => "Wymaga restartu", RestartRequirement.Logout => "Wymaga wylogowania", _ => "" };
    public string RiskText => Tweak.IsAction ? "Akcja" : Tweak.Risk switch
    {
        RiskLevel.Recommended => "Zalecane", RiskLevel.Caution => "Ostrożnie", RiskLevel.Experimental => "Eksperymentalne", _ => "Wysokie ryzyko"
    };
    public Brush RiskBrush => ThemeBrushes.Resource(Tweak.IsAction ? "MutedBrush" : Tweak.Risk switch
    {
        RiskLevel.Recommended => "GoodBrush", RiskLevel.Caution => "AccentBrush", RiskLevel.Experimental => "WarnBrush", _ => "BadBrush"
    });
    public string ActionText => Tweak.IsAction ? (Tweak.Button ?? "Uruchom") : IsApplied ? "Cofnij" : "Zastosuj";

    public bool IsIdle => !IsBusy;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(BusyVisibility)); }

    partial void OnIsAppliedChanged(bool value) { OnPropertyChanged(nameof(ActionText)); UpdateStatusText(); _changed?.Invoke(); }

    private void UpdateStatusText() => StatusText = Tweak.IsAction ? "" : IsApplied ? "● Zastosowane" : "";

    public void SetFromStatus(TweakStatus s)
    {
        // Stan systemu ma pierwszeństwo, ale nie nadpisujemy zmiany, którą NEOX ma w kopii zapasowej, gdy odczyt jest nieznany.
        if (s == TweakStatus.Applied) IsApplied = true;
        else if (s == TweakStatus.NotApplied) IsApplied = _engine.IsApplied(Tweak.Id) && Tweak.Kind == TweakKind.Cmd;
    }

    [RelayCommand]
    private async Task PrimaryAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (Tweak.IsAction) await RunActionAsync();
            else if (IsApplied) await RevertAsync();
            else await ApplyAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task ApplyAsync()
    {
        if (Tweak.Risk == RiskLevel.HighRisk || Tweak.Risk == RiskLevel.Experimental)
        {
            var head = Tweak.Risk == RiskLevel.HighRisk ? "To zmiana o WYSOKIM ryzyku." : "To ustawienie eksperymentalne; efekt zależy od sprzętu.";
            var ok = await _dialogs.ConfirmAsync(Tweak.Title, $"{head}\n\n{Tweak.Description}\n\nSkutki: {Tweak.Consequences}\n\nPrzed zmianą zapisujemy kopię zapasową i możesz ją cofnąć w tej samej karcie.", "Rozumiem, zastosuj");
            if (!ok) return;
        }
        var r = await _engine.ApplyAsync(Tweak.Id, _ctx.Current);
        if (r.Success) { IsApplied = true; Applied?.Invoke(this, EventArgs.Empty); }
        await Report(r);
    }

    private async Task RevertAsync()
    {
        var r = await _engine.RevertAsync(Tweak.Id, _ctx.Current);
        if (r.Success) IsApplied = false;
        await Report(r);
    }

    private async Task RunActionAsync()
    {
        if (!string.IsNullOrEmpty(Tweak.Confirm) && !await _dialogs.ConfirmAsync(Tweak.Title, Tweak.Confirm!)) return;
        var r = await _engine.RunActionAsync(Tweak.Id, _ctx.Current);
        if (r.Success && Tweak.ShowsResult) await _dialogs.InfoAsync(Tweak.Title, r.Message);
        else await Report(r);
        if (r.Success) Applied?.Invoke(this, EventArgs.Empty);
    }

    private async Task Report(TweakResult r)
    {
        var suffix = r.Success && r.Restart != RestartRequirement.None ? (r.Restart == RestartRequirement.Restart ? " Wymagany restart." : " Wymagane wylogowanie.") : "";
        _notify($"{Tweak.Title}: {r.Message}{suffix}");
        if (!r.Success) await _dialogs.InfoAsync("Nie udało się", $"{Tweak.Title}\n\n{r.Message}");
    }
}

public sealed partial class OptimizationsViewModel : ObservableObject
{
    private readonly TweakEngine _engine;
    private readonly AppContextService _ctx;
    private readonly List<TweakItemViewModel> _all;
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
    public IReadOnlyList<string> Categories { get; } = new[] { "Wszystkie", "Wydajność", "Input delay", "Sieć", "Prywatność", "Usługi i zadania", "Wygląd", "Czyszczenie", "Narzędzia" };
    public IReadOnlyList<string> Risks { get; } = new[] { "Każde ryzyko", "Zalecane", "Ostrożnie", "Eksperymentalne", "Wysokie ryzyko" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _categoryIndex;
    [ObservableProperty] private int _riskIndex;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _counter = "";
    [ObservableProperty] private int _appliedCount;

    public OptimizationsViewModel(TweakCatalog catalog, TweakEngine engine, DialogService dialogs, AppContextService ctx)
    {
        _engine = engine; _ctx = ctx;
        _all = catalog.All.Select(t => new TweakItemViewModel(t, engine, dialogs, ctx, m => StatusMessage = m, Recount)).ToList();
        Recount();
        ApplyFilter();
    }

    public IReadOnlyList<TweakItemViewModel> All => _all;

    private void Recount() => AppliedCount = _all?.Count(i => !i.Tweak.IsAction && i.IsApplied) ?? 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCategoryIndexChanged(int value) => ApplyFilter();
    partial void OnRiskIndexChanged(int value) => ApplyFilter();

    private static TweakCategory? CategoryOf(int i) => i switch
    {
        1 => TweakCategory.Performance, 2 => TweakCategory.Input, 3 => TweakCategory.Network, 4 => TweakCategory.Privacy,
        5 => TweakCategory.Services, 6 => TweakCategory.Appearance, 7 => TweakCategory.Cleanup, 8 => TweakCategory.Tools, _ => null
    };

    private static RiskLevel? RiskOf(int i) => i switch
    {
        1 => RiskLevel.Recommended, 2 => RiskLevel.Caution, 3 => RiskLevel.Experimental, 4 => RiskLevel.HighRisk, _ => null
    };

    public void ApplyFilter()
    {
        var q = SearchText.Trim();
        var cat = CategoryOf(CategoryIndex);
        var risk = RiskOf(RiskIndex);
        var rows = _all.Where(i =>
            (cat is null || i.Tweak.Category == cat) &&
            (risk is null || (!i.Tweak.IsAction && i.Tweak.Risk == risk)) &&
            (q.Length == 0 || i.Tweak.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                           || i.Tweak.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                           || i.Tweak.Id.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
        Counter = $"{rows.Count} z {_all.Count}";
    }

    /// <summary>Odczytuje rzeczywisty stan wszystkich ustawień w tle, po jednym, bez blokowania UI.</summary>
    [RelayCommand]
    public async Task RefreshStatesAsync()
    {
        _refreshCts?.Cancel();
        var cts = _refreshCts = new CancellationTokenSource();
        IsRefreshing = true;
        try
        {
            var ctx = _ctx.Current;
            var done = 0;
            foreach (var item in _all.Where(i => !i.Tweak.IsAction))
            {
                cts.Token.ThrowIfCancellationRequested();
                var s = await _engine.GetStatusAsync(item.Tweak.Id, ctx, cts.Token);
                item.SetFromStatus(s);
                if (++done % 10 == 0) StatusMessage = $"Odczyt stanu… {done}";
            }
            StatusMessage = "Stany odświeżone.";
        }
        catch (OperationCanceledException) { }
        finally { if (_refreshCts == cts) IsRefreshing = false; }
    }

    public void ReloadAppliedFlags()
    {
        foreach (var i in _all.Where(i => !i.Tweak.IsAction)) i.IsApplied = _engine.IsApplied(i.Tweak.Id);
    }
}
