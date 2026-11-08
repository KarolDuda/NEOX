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

    [ObservableProperty] private string _hardwareText = "Scanning hardware…";
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
        Busy = true; Status = "Scanning hardware…";
        try
        {
            _plan = await _svc.PrepareAsync();
            HardwareText = _plan.Hardware.Describe();
            Planned.Clear();
            foreach (var p in _plan.Planned) Planned.Add(new PlanRow(p.Title, p.Reason));
            var already = _plan.Skipped.Count(s => s.Reason == AutoTunePlanner.AlreadyAppliedReason);
            SkippedText = $"Skipped {_plan.Skipped.Count} items ({already} already applied): they need administrator rights, do not fit your hardware, or change system behavior, so we leave them to you.";
            CanRun = _plan.Planned.Count > 0;
            Status = _plan.Planned.Count > 0 ? $"Plan ready: {_plan.Planned.Count} changes chosen for your PC." : "Nothing to do: everything that fits your PC is already set.";
        }
        catch (Exception ex) { Status = "Could not scan hardware: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Busy || _plan is null || _plan.Planned.Count == 0) return;
        var text = $"Changes: {_plan.Planned.Count}. Each has a backup, and you can undo everything with one button.\n\n" +
                   (Measure ? "We will measure your PC before and after (about 30 s total). Do not use other programs during the measurement.\n\n" : "") +
                   "Changes that need a restart take effect only after it.";
        if (!await _dialogs.ConfirmAsync("Run Auto-Tune?", text, "Uruchom")) return;

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
        catch (OperationCanceledException) { Status = "Cancelled. Changes applied so far can be undone in History."; _opt.ReloadAppliedFlags(); }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
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
                Verdict.Worse => ("worse", "▼"),
                _ => ("within measurement noise", "≈"),
            };
            var dec = d.Unit == "ms" ? "F2" : "F0";
            Deltas.Add(new DeltaRow(d.Name, $"{d.Before.ToString(dec)} {d.Unit}", $"{d.After.ToString(dec)} {d.Unit}", $"{d.Percent:+0.0;-0.0;0.0}%", verdict, mark));
        }
        DeltasVisibility = Deltas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary = r.Apply.Failed == 0
            ? $"Applied {r.AppliedIds.Count} changes."
            : $"Applied {r.AppliedIds.Count}, failed: {r.Apply.Failed}. Details in History.";
        Note = (r.Deltas.Count > 0
                   ? "This is a synthetic benchmark (CPU, RAM, timer), not in-game FPS. Changes below the noise threshold are not improvements. "
                   : "") + (r.Apply.Restart != RestartRequirement.None ? "Restart your PC so all changes take effect." : "");
        CanUndo = r.AppliedIds.Count > 0;
        ReportVisibility = Visibility.Visible;
        Status = "Gotowe.";
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_report is null || Busy) return;
        if (!await _dialogs.ConfirmAsync("Undo Auto-Tune?", $"We will restore the previous settings of {_report.AppliedIds.Count} changes.", "Cofnij")) return;
        Busy = true;
        try
        {
            var n = await _svc.UndoAsync(_report.AppliedIds, _ctx.Current);
            _opt.ReloadAppliedFlags();
            CanUndo = false;
            Status = $"Undone {n} changes.";
        }
        finally { Busy = false; }
        await LoadAsync();
    }
}
