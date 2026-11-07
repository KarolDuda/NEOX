using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Tachyvex.App.Services;
using Tachyvex.Core.AutoTune;
using Tachyvex.Core.Models;

namespace Tachyvex.App.ViewModels;

public sealed record PlanRow(string Title, string Reason);
public sealed record DeltaRow(string Name, string Before, string After, string Change, string Verdict, string Mark);

public sealed partial class AutoTuneViewModel : ObservableObject
{
    private readonly AutoTuneService _svc;
    private readonly DialogService _dialogs;
    private readonly AppContextService _ctx;
    private readonly OptimizationsViewModel _opt;
    private AutoTunePlan? _plan;
    private AutoTuneReport? _report;
    private CancellationTokenSource? _cts;

    public ObservableCollection<PlanRow> Planned { get; } = new();
    public ObservableCollection<DeltaRow> Deltas { get; } = new();

    [ObservableProperty] private string _hardwareText = "Skanowanie sprzętu…";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _skippedText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private bool _measure = true;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _canRun;
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private Visibility _reportVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _deltasVisibility = Visibility.Collapsed;

    public bool IsIdle => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    /// <summary>Po zakończeniu (do animacji sukcesu).</summary>
    public event Action<AutoTuneReport>? Completed;

    public AutoTuneViewModel(AutoTuneService svc, DialogService dialogs, AppContextService ctx, OptimizationsViewModel opt)
    {
        _svc = svc; _dialogs = dialogs; _ctx = ctx; _opt = opt;
    }

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Status = "Skanowanie sprzętu…";
        try
        {
            _plan = await _svc.PrepareAsync();
            HardwareText = _plan.Hardware.Describe();
            Planned.Clear();
            foreach (var p in _plan.Planned) Planned.Add(new PlanRow(p.Title, p.Reason));
            var already = _plan.Skipped.Count(s => s.Reason == "już zastosowane");
            SkippedText = $"Pominięto {_plan.Skipped.Count} pozycji (w tym {already} już zastosowanych): wymagają administratora, nie pasują do Twojego sprzętu albo zmieniają zachowanie systemu, więc zostawiamy je Tobie.";
            CanRun = _plan.Planned.Count > 0;
            Status = _plan.Planned.Count > 0 ? $"Plan gotowy: {_plan.Planned.Count} zmian dobranych do Twojego komputera." : "Nic do zrobienia: wszystko, co pasuje do Twojego komputera, jest już ustawione.";
        }
        catch (Exception ex) { Status = "Nie udało się przeskanować sprzętu: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Busy || _plan is null || _plan.Planned.Count == 0) return;
        var text = $"Zmian: {_plan.Planned.Count}. Każda ma kopię zapasową, a całość cofniesz jednym przyciskiem.\n\n" +
                   (Measure ? "Zmierzymy komputer przed i po (ok. 30 s łącznie). Na czas pomiaru nie używaj innych programów.\n\n" : "") +
                   "Wymagający restartu zadziałają dopiero po nim.";
        if (!await _dialogs.ConfirmAsync("Uruchomić Auto-Tune?", text, "Uruchom")) return;

        Busy = true; CanRun = false; _cts = new CancellationTokenSource();
        ReportVisibility = Visibility.Collapsed;
        try
        {
            var progress = new Progress<string>(s => Status = s);
            var rep = await _svc.RunAsync(_plan, _ctx.Current, Measure, progress, _cts.Token);
            _report = rep;
            _opt.ReloadAppliedFlags();
            Show(rep);
            Completed?.Invoke(rep);
        }
        catch (OperationCanceledException) { Status = "Przerwano. Zmiany zastosowane do tej pory można cofnąć w Historii."; _opt.ReloadAppliedFlags(); }
        catch (Exception ex) { Status = "Błąd: " + ex.Message; }
        finally { Busy = false; _cts = null; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void Show(AutoTuneReport r)
    {
        Deltas.Clear();
        foreach (var d in r.Deltas)
        {
            var (verdict, mark) = d.Verdict switch
            {
                Verdict.Better => ("lepiej", "▲"),
                Verdict.Worse => ("gorzej", "▼"),
                _ => ("w granicach szumu pomiarowego", "≈"),
            };
            var dec = d.Unit == "ms" ? "F2" : "F0";
            Deltas.Add(new DeltaRow(d.Name, $"{d.Before.ToString(dec)} {d.Unit}", $"{d.After.ToString(dec)} {d.Unit}", $"{d.Percent:+0.0;-0.0;0.0}%", verdict, mark));
        }
        DeltasVisibility = Deltas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary = r.Apply.Failed == 0
            ? $"Zastosowano {r.AppliedIds.Count} zmian."
            : $"Zastosowano {r.AppliedIds.Count}, błędów: {r.Apply.Failed}. Szczegóły w Historii.";
        Note = (r.Deltas.Count > 0
                   ? "To pomiar syntetyczny (CPU, RAM, timer), a nie FPS w grze. Zmiany poniżej progu szumu nie są poprawą. "
                   : "") + (r.Apply.Restart != RestartRequirement.None ? "Aby wszystkie zmiany zadziałały, uruchom ponownie komputer." : "");
        CanUndo = r.AppliedIds.Count > 0;
        ReportVisibility = Visibility.Visible;
        Status = "Gotowe.";
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_report is null || Busy) return;
        if (!await _dialogs.ConfirmAsync("Cofnąć Auto-Tune?", $"Przywrócimy poprzednie ustawienia {_report.AppliedIds.Count} zmian.", "Cofnij")) return;
        Busy = true;
        try
        {
            var n = await _svc.UndoAsync(_report.AppliedIds, _ctx.Current);
            _opt.ReloadAppliedFlags();
            CanUndo = false;
            Status = $"Cofnięto {n} zmian.";
        }
        finally { Busy = false; }
        await LoadAsync();
    }
}
