using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Neox.App.Services;
using Neox.Core.Abstractions;
using Neox.Core.Catalog;
using Neox.Core.Health;
using Microsoft.UI.Xaml;
using Neox.Core.Engine;
using Neox.Core.Models;
using Neox.Core.Profiles;
using Neox.Core.State;
using System.Net.Http;
using Neox.Windows;
using Neox.Windows.Infrastructure;

namespace Neox.App.ViewModels;

public static class Fmt
{
    public static string Bytes(long b) => b switch
    {
        >= 1L << 40 => $"{b / (double)(1L << 40):F1} TB",
        >= 1L << 30 => $"{b / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):F0} MB",
        _ => $"{b / 1024.0:F0} KB"
    };
}

public sealed record HealthRow(string Name, string Detail, string Mark, FixKind Fix, string FixText, Visibility FixVisibility);

// ---------------- Panel ----------------
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly SystemMonitor _monitor;
    private readonly ProfileService _profiles;
    private readonly TweakEngine _engine;
    private readonly IRestorePointService _restore;
    private readonly DialogService _dialogs;
    private readonly AppContextService _ctx;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty] private double _cpu;
    [ObservableProperty] private double _ram;
    [ObservableProperty] private double _gpu;
    [ObservableProperty] private string _cpuText = "–";
    [ObservableProperty] private string _ramText = "–";
    [ObservableProperty] private string _gpuText = "n/a";
    [ObservableProperty] private string _hardware = "Reading hardware…";
    [ObservableProperty] private string _health = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _ramPct = "–";
    [ObservableProperty] private string _gpuPct = "–";
    [ObservableProperty] private int _appliedCount;

    private readonly OptimizationsViewModel _opt;
    private readonly IStateStore _store;
    private readonly HealthProbe _probe;

    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private string _healthLabel = "Checking…";
    public ObservableCollection<HealthRow> HealthChecks { get; } = new();
    /// <summary>Wynik zdrowia gotowy: strona animuje licznik od 0.</summary>
    public event Action<int>? HealthComputed;

    public bool IsIdle => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    /// <summary>Po udanym zastosowaniu profilu (do animacji sukcesu).</summary>
    public event Action<ProfileApplyReport>? ProfileApplied;

    public ObservableCollection<Profile> Profiles { get; } = new();

    public DashboardViewModel(SystemMonitor monitor, ProfileService profiles, TweakEngine engine, IRestorePointService restore, DialogService dialogs, AppContextService ctx, OptimizationsViewModel opt, IStateStore store, HealthProbe health)
    {
        _probe = health;
        _monitor = monitor; _profiles = profiles; _engine = engine; _restore = restore; _dialogs = dialogs; _ctx = ctx; _opt = opt; _store = store;
        AppliedCount = store.State.Snapshots.Count;
        _monitor.SampleReady += OnSample;
        foreach (var p in profiles.All()) Profiles.Add(p);
    }

    private void OnSample(MonitorSample s) => _ui.TryEnqueue(() =>
    {
        Cpu = s.CpuPercent; CpuText = $"{s.CpuPercent:F0}%";
        RamPct = $"{s.RamPercent}%";
        Ram = s.RamPercent; RamText = $"{s.RamPercent}%  ({Fmt.Bytes(s.RamUsedBytes)} / {Fmt.Bytes(s.RamTotalBytes)})";
        if (s.GpuPercent is { } g) { Gpu = g; GpuText = $"{g:F0}%"; GpuPct = $"{g:F0}%"; } else { Gpu = 0; GpuText = "not available on this hardware"; GpuPct = "–"; }
    });

    /// <summary>Monitoring działa tylko, gdy strona jest widoczna.</summary>
    public async Task OnShownAsync()
    {
        _monitor.Start();
        var info = await Task.Run(() =>
        {
            var cpu = SystemInfo.Cpu();
            var gpus = SystemInfo.Gpus();
            var os = SystemInfo.Os();
            var hw = string.Join("\n", new[]
            {
                cpu is null ? null : $"CPU: {cpu.Name}",
                gpus.Count == 0 ? null : "GPU: " + string.Join("; ", gpus.Select(g => g.Name)),
                $"RAM: {Fmt.Bytes(SystemInfo.TotalRamBytes())}",
                os is null ? null : $"OS: {os.Caption} (build {os.Build})",
            }.Where(x => x is not null));
            var vols = SystemInfo.Volumes();
            var low = vols.Where(v => v.TotalBytes > 0 && v.FreeBytes < v.TotalBytes * 0.10).Select(v => v.Name).ToList();
            var health = low.Count == 0 ? "Disks: free space is fine." : "Low free space (<10%): " + string.Join(", ", low);
            return (hw, health);
        });
        Hardware = info.hw; Health = info.health;
        await RefreshHealthAsync();
    }

    public async Task RefreshHealthAsync()
    {
        try
        {
            var r = await _probe.ScanAsync();
            HealthChecks.Clear();
            foreach (var c in r.Checks)
                HealthChecks.Add(new HealthRow(c.Name, c.Detail, c.Ok ? "✔" : c.Score >= 0.5 ? "!" : "✖", c.Fix, FixLabel(c.Fix),
                    c.Fix == FixKind.None ? Visibility.Collapsed : Visibility.Visible));
            HealthScore = r.Score; HealthLabel = r.Label;
            HealthComputed?.Invoke(r.Score);
        }
        catch (Exception ex) { HealthLabel = "Could not assess the system: " + ex.Message; }
    }

    private static string FixLabel(FixKind k) => k switch
    {
        FixKind.ApplySafeProfile => "Napraw",
        FixKind.OpenStartup => "Go to Startup",
        FixKind.OpenDisk => "Go to Disk",
        FixKind.Restart => "How to do it",
        _ => "",
    };

    [RelayCommand]
    private async Task FixAsync(HealthRow row)
    {
        switch (row.Fix)
        {
            case FixKind.ApplySafeProfile:
                if (Profiles.FirstOrDefault(x => x.Id == "builtin.safe") is { } p) { await ApplyProfileAsync(p); await RefreshHealthAsync(); }
                break;
            case FixKind.OpenStartup: App.MainWindow?.Navigate("managers"); break;
            case FixKind.OpenDisk: App.MainWindow?.Navigate("disk"); break;
            case FixKind.Restart:
                await _dialogs.InfoAsync("Restart your PC", "Save your work and restart the PC (Start → Power → Restart). We never do this for you without asking.");
                break;
        }
    }

    public void OnHidden() => _monitor.Stop();

    [RelayCommand]
    private async Task ApplyProfileAsync(Profile p)
    {
        if (Busy) return;
        var list = string.Join("\n", p.TweakIds.Take(12).Select(id => "• " + _engine.Catalog.Get(id).Title)) + (p.TweakIds.Count > 12 ? $"\n… and {p.TweakIds.Count - 12} more" : "");
        if (!await _dialogs.ConfirmAsync($"Profile “{p.Name}”", $"{p.Description}\n\nChanges: {p.TweakIds.Count}\n{list}\n\nEvery change is backed up and can be undone in History.", "Apply profile")) return;
        Busy = true;
        try
        {
            var progress = new Progress<string>(s => Message = s);
            var rep = await _profiles.ApplyAsync(p, _ctx.Current, progress);
            Message = $"Applied {rep.Applied}, skipped {rep.Skipped}, failed {rep.Failed}.";
            _opt.ReloadAppliedFlags();
            AppliedCount = _store.State.Snapshots.Count;
            if (rep.Applied > 0) ProfileApplied?.Invoke(rep);
            else await _dialogs.InfoAsync("Profile result", string.Join("\n", rep.Lines));
        }
        finally { Busy = false; }
    }

    /// <summary>Duży przycisk na panelu: profil Gaming.</summary>
    [RelayCommand]
    private Task BoostAsync()
    {
        var p = Profiles.FirstOrDefault(x => x.Id == "builtin.gaming") ?? Profiles.First();
        return ApplyProfileAsync(p);
    }

    [RelayCommand]
    private async Task RestorePointAsync()
    {
        Busy = true;
        try
        {
            var r = await _restore.CreateAsync("NEOX", CancellationToken.None);
            Message = r.Message;
            if (!r.Success) await _dialogs.InfoAsync("Restore point", r.Message + "\n\nRequires administrator rights and System Protection turned on (limit: 1 point per 24 h).");
        }
        finally { Busy = false; }
    }

    public void Dispose() { _monitor.SampleReady -= OnSample; _monitor.Dispose(); }
}

// ---------------- Gaming ----------------
public sealed partial class GamingViewModel : ObservableObject
{
    private readonly ProfileService _profiles;
    private readonly IStateStore _store;
    private readonly DialogService _dialogs;
    private readonly AppContextService _ctx;
    private readonly TweakEngine _engine;
    private readonly FortniteConfigService _fn;
    private readonly ProfileTransfer _transfer;
    private readonly GameBoostService _boost;
    private readonly DispatcherQueue _uiq = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty] private bool _gameBoost;
    [ObservableProperty] private string _boostStatus = "";

    public OptimizationsViewModel Optimizations { get; }
    public ObservableCollection<Profile> Profiles { get; } = new();
    public ObservableCollection<DetectedGame> Games { get; } = new();
    public ObservableCollection<TweakItemViewModel> GameTweaks { get; } = new();

    [ObservableProperty] private string _gameExePath = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _fortniteInfo = "";
    [ObservableProperty] private bool _busy;

    public GamingViewModel(ProfileService profiles, IStateStore store, DialogService dialogs, AppContextService ctx, TweakEngine engine,
        OptimizationsViewModel optimizations, FortniteConfigService fn, ProfileTransfer transfer, GameBoostService boost)
    {
        _transfer = transfer; _boost = boost;
        _gameBoost = store.State.Settings.GameBoost;
        _boostStatus = boost.Status;
        _profiles = profiles; _store = store; _dialogs = dialogs; _ctx = ctx; _engine = engine; _fn = fn; Optimizations = optimizations;
        GameExePath = store.State.Settings.GameExePath ?? "";
        Reload();
        foreach (var t in optimizations.All.Where(i => i.Tweak.Kind == TweakKind.RegPerGame)) GameTweaks.Add(t);
    }

    private void Reload()
    {
        Profiles.Clear();
        foreach (var p in _profiles.All()) Profiles.Add(p);
        FortniteInfo = !_fn.Exists ? "Fortnite settings file not found."
            : _fn.PendingChanges().Count > 0 ? "To change: " + string.Join(", ", _fn.PendingChanges())
            : "V-Sync and HDR are already off.";
    }

    partial void OnGameBoostChanged(bool value)
    {
        _store.State.Settings.GameBoost = value;
        _ = _store.SaveAsync();
        if (value) _boost.Start(); else _ = _boost.StopAsync();
    }

    private void OnBoostStatus(string s) => _uiq.TryEnqueue(() => BoostStatus = s);
    public void Attach() { _boost.StatusChanged += OnBoostStatus; BoostStatus = _boost.Status; }
    public void Detach() => _boost.StatusChanged -= OnBoostStatus;

    public string ExportJson(Profile p) => _transfer.Export(p);

    public async Task ImportJsonAsync(string json)
    {
        var r = _transfer.Import(json);
        if (!r.Success) { Message = "Import failed: " + r.Error; return; }
        _store.State.CustomProfiles.Add(r.Profile!);
        await _store.SaveAsync();
        Reload();
        Message = $"Imported profile “{r.Profile!.Name}” ({r.Profile.TweakIds.Count} changes)."
                  + (r.Dropped > 0 ? $" Skipped {r.Dropped} disallowed or unknown entries." : "");
    }

    partial void OnGameExePathChanged(string value)
    {
        _store.State.Settings.GameExePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
        _ = _store.SaveAsync();
    }

    [RelayCommand]
    private async Task DetectGamesAsync()
    {
        var found = await Task.Run(GameDiscovery.Epic);
        Games.Clear();
        foreach (var g in found) Games.Add(g);
        Message = found.Count == 0 ? "No Epic Games titles detected. Pick the .exe manually." : $"Games detected: {found.Count}.";
    }

    [RelayCommand]
    private void UseGame(DetectedGame g) => GameExePath = g.ExePath;

    [RelayCommand]
    private async Task ApplyProfileAsync(Profile p)
    {
        if (Busy) return;
        if (!await _dialogs.ConfirmAsync($"Profile “{p.Name}”", $"{p.Description}\n\nChanges: {p.TweakIds.Count}. Backups are saved; undo them in History.", "Zastosuj")) return;
        Busy = true;
        try
        {
            var rep = await _profiles.ApplyAsync(p, _ctx.Current, new Progress<string>(s => Message = s));
            Message = $"Applied {rep.Applied}, skipped {rep.Skipped}, failed {rep.Failed}.";
            Optimizations.ReloadAppliedFlags();
            await _dialogs.InfoAsync("Profile result", string.Join("\n", rep.Lines));
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SaveCurrentAsProfileAsync()
    {
        var name = await _dialogs.PromptAsync("New profile from current changes", "Profile name");
        if (name is null) return;
        var ids = _store.State.Snapshots.Keys.ToList();
        if (ids.Count == 0) { Message = "No applied changes to save."; return; }
        await _profiles.SaveCustomAsync(name, ids, "Custom profile from applied changes.");
        Reload();
        Message = $"Saved profile “{name}” ({ids.Count} changes).";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile p)
    {
        if (p.BuiltIn) return;
        await _profiles.DeleteCustomAsync(p.Id);
        Reload();
    }

    [RelayCommand]
    private async Task FortniteApplyAsync()
    {
        try { Message = await Task.Run(_fn.Apply); } catch (InvalidOperationException ex) { Message = ex.Message; }
        Reload();
    }

    [RelayCommand]
    private async Task FortniteRestoreAsync()
    {
        try { Message = await Task.Run(_fn.Restore); } catch (InvalidOperationException ex) { Message = ex.Message; }
        Reload();
    }
}

// ---------------- Sieć ----------------
public sealed record PingRow(string Name, string Summary);
public sealed record DnsRow(string Name, string Servers, string Result);

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly NetworkDiagnostics _net;
    private readonly DialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<PingRow> PingRows { get; } = new();
    public ObservableCollection<DnsRow> DnsRows { get; } = new();
    public ObservableCollection<string> Adapters { get; } = new();

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _customHost = "";
    [ObservableProperty] private string _mtuText = "";
    [ObservableProperty] private string _bestDns = "";
    private DnsProvider? _best;

    public NetworkViewModel(NetworkDiagnostics net, DialogService dialogs) { _net = net; _dialogs = dialogs; LoadAdapters(); }

    public void LoadAdapters()
    {
        Adapters.Clear();
        foreach (var a in NetworkAdapters.GetActive())
        {
            var (dns, manual) = NetworkDiagnostics.AdapterDns(a.Guid);
            Adapters.Add($"{a.Name} ({a.Type}) · {(a.SpeedMbps > 0 ? a.SpeedMbps + " Mb/s" : "?")} · IP {string.Join(", ", a.Addresses.Take(2))} · DNS {(dns.Length == 0 ? "—" : string.Join(", ", dns))} ({(manual ? "manual" : "automatic")})");
        }
        if (Adapters.Count == 0) Adapters.Add("No active network adapter (Ethernet / Wi-Fi).");
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (Busy) return;
        Busy = true; _cts = new CancellationTokenSource();
        try { await work(_cts.Token); }
        catch (OperationCanceledException) { Message = "Przerwano."; }
        finally { Busy = false; Progress = 0; }
    }

    [RelayCommand]
    private Task PingAllAsync() => RunAsync(async ct =>
    {
        PingRows.Clear();
        var targets = NetworkDiagnostics.PingTargets.ToList();
        if (!string.IsNullOrWhiteSpace(CustomHost)) targets.Insert(0, ("Custom", CustomHost.Trim()));
        for (var i = 0; i < targets.Count; i++)
        {
            Message = $"Ping: {targets[i].Name}…";
            var s = await _net.PingAsync(targets[i].Host, 8, 2000, null, ct);
            PingRows.Add(new PingRow(targets[i].Name, s.Received == 0 ? "no response" :
                $"{s.AvgMs:F0} ms (min {s.MinMs:F0}, max {s.MaxMs:F0}) · jitter {s.JitterMs:F1} ms · loss {s.LossPercent:F0}%"));
            Progress = (i + 1) / (double)targets.Count;
        }
        Message = "Ping test finished.";
    });

    [RelayCommand]
    private Task BenchDnsAsync() => RunAsync(async ct =>
    {
        DnsRows.Clear(); _best = null; BestDns = "";
        var results = new List<DnsBenchResult>();
        var providers = NetworkDiagnostics.DnsProviders;
        for (var i = 0; i < providers.Count; i++)
        {
            Message = $"DNS: {providers[i].Name}…";
            results.Add(await _net.BenchDnsAsync(providers[i], 2, ct));
            Progress = (i + 1) / (double)providers.Count;
        }
        foreach (var r in results.OrderBy(r => r.MedianMs ?? double.MaxValue))
            DnsRows.Add(new DnsRow(r.Provider.Name, $"{r.Provider.Primary} / {r.Provider.Secondary}",
                r.MedianMs is null ? "no response" : $"{r.MedianMs:F0} ms (failures: {r.Failures})"));
        var best = results.Where(r => r.MedianMs is not null && r.Failures <= 2).OrderBy(r => r.MedianMs).FirstOrDefault();
        _best = best?.Provider;
        BestDns = best is null ? "" : $"Fastest: {best.Provider.Name} ({best.MedianMs:F0} ms)";
        Message = "DNS test finished. The result applies to your connection right now.";
    });

    [RelayCommand]
    private async Task UseBestDnsAsync()
    {
        if (_best is null) { Message = "Run the DNS test first."; return; }
        if (!await _dialogs.ConfirmAsync("Change DNS", $"Set DNS to {_best.Name} ({_best.Primary} / {_best.Secondary}) on active adapters? Previous settings are saved and can be restored.", "Ustaw")) return;
        var r = await _net.SetDnsAsync(new[] { _best.Primary, _best.Secondary });
        Message = r.Message; LoadAdapters();
    }

    [RelayCommand]
    private async Task RestoreDnsAsync() { var r = await _net.RestoreDnsAsync(); Message = r.Message; LoadAdapters(); }

    [RelayCommand]
    private Task FindMtuAsync() => RunAsync(async ct =>
    {
        Message = "Probing MTU…";
        var mtu = await _net.FindMtuAsync("1.1.1.1", ct);
        MtuText = mtu is null ? "No ping response (or ICMP is blocked)." : $"MTU: {mtu} bytes" + (mtu < 1500 ? " (below 1500; check your link/VPN)" : "");
        Message = "Gotowe.";
    });
}

// ---------------- Informacje o systemie ----------------
public sealed partial class SystemViewModel : ObservableObject
{
    [ObservableProperty] private string _cpu = "…";
    [ObservableProperty] private string _gpu = "…";
    [ObservableProperty] private string _ram = "…";
    [ObservableProperty] private string _os = "…";
    public ObservableCollection<string> Disks { get; } = new();
    public ObservableCollection<string> Volumes { get; } = new();

    public async Task LoadAsync()
    {
        var d = await Task.Run(() => (SystemInfo.Cpu(), SystemInfo.Gpus(), SystemInfo.TotalRamBytes(), SystemInfo.Os(), SystemInfo.Disks(), SystemInfo.Volumes()));
        Cpu = d.Item1 is null ? "no data" : $"{d.Item1.Name}\n{d.Item1.Cores} cores / {d.Item1.Threads} threads · max {d.Item1.MaxMHz} MHz";
        Gpu = d.Item2.Count == 0 ? "no data" : string.Join("\n", d.Item2.Select(g => $"{g.Name} · driver {g.DriverVersion} · {g.RefreshHz} Hz"));
        Ram = Fmt.Bytes(d.Item3);
        Os = d.Item4 is null ? "no data" : $"{d.Item4.Caption} · version {d.Item4.Version} · build {d.Item4.Build}\nLast boot: {d.Item4.LastBoot:g}";
        Disks.Clear(); foreach (var x in d.Item5) Disks.Add($"{x.Model} · {Fmt.Bytes(x.SizeBytes)} · health: {x.Health}");
        Volumes.Clear(); foreach (var v in d.Item6) Volumes.Add($"{v.Name} {v.Format} · {Fmt.Bytes(v.FreeBytes)} free of {Fmt.Bytes(v.TotalBytes)} ({(v.TotalBytes == 0 ? 0 : 100 - 100 * v.FreeBytes / v.TotalBytes)}% used)");
    }
}

// ---------------- Start, procesy, usługi ----------------
public sealed partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupManager _mgr;
    private readonly StartupEntry _entry;
    private bool _loading = true;
    [ObservableProperty] private bool _enabled;
    public string Name => _entry.Name;
    public string Detail => $"{_entry.Location} · {_entry.Command}";
    public StartupItemViewModel(StartupManager mgr, StartupEntry e) { _mgr = mgr; _entry = e; Enabled = e.Enabled; _loading = false; }
    partial void OnEnabledChanged(bool value) { if (!_loading) _mgr.SetEnabled(_entry, value); }
}

public sealed record ProcessRow(string Name, string Memory, string Cpu, int Pid);
public sealed record ServiceRow(string DisplayName, string Name, string Status, string StartType, string Note);

public sealed partial class ManagersViewModel : ObservableObject
{
    private readonly StartupManager _startup; private readonly ProcessOverview _proc; private readonly WindowsServicesManager _svc;
    public ObservableCollection<StartupItemViewModel> Startup { get; } = new();
    public ObservableCollection<ProcessRow> Processes { get; } = new();
    public ObservableCollection<ServiceRow> Services { get; } = new();
    [ObservableProperty] private string _message = "";

    public ManagersViewModel(StartupManager s, ProcessOverview p, WindowsServicesManager v) { _startup = s; _proc = p; _svc = v; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var d = await Task.Run(() => (_startup.List(), _proc.Top(25), _svc.List()));
        Startup.Clear(); foreach (var e in d.Item1) Startup.Add(new StartupItemViewModel(_startup, e));
        Processes.Clear(); foreach (var p in d.Item2) Processes.Add(new ProcessRow(p.Name, Fmt.Bytes(p.WorkingSetBytes), $"{p.CpuSeconds:F0} s", p.Pid));
        Services.Clear(); foreach (var s in d.Item3) Services.Add(new ServiceRow(s.DisplayName, s.Name, s.Status, s.StartType, s.IsCritical ? "critical: we never modify it" : ""));
        Message = $"Startup programs: {Startup.Count} · services: {Services.Count}. Change services in the Optimizations tab (with backup).";
    }
}

// ---------------- Dysk ----------------
public sealed record FolderRow(string Path, string Size);

public sealed partial class DiskViewModel : ObservableObject
{
    private readonly StorageAnalyzer _analyzer;
    public ObservableCollection<FolderRow> Folders { get; } = new();
    public ObservableCollection<string> Drives { get; } = new();
    [ObservableProperty] private int _driveIndex;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    private CancellationTokenSource? _cts;

    public DiskViewModel(StorageAnalyzer a)
    {
        _analyzer = a;
        foreach (var v in SystemInfo.Volumes()) Drives.Add(v.Name);
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (Busy || Drives.Count == 0) return;
        Busy = true; _cts = new CancellationTokenSource(); Folders.Clear();
        try
        {
            var rows = await _analyzer.AnalyzeAsync(Drives[Math.Max(0, DriveIndex)], 25, new Progress<string>(s => Message = "Scanning: " + s), _cts.Token);
            foreach (var r in rows) Folders.Add(new FolderRow(r.Path, Fmt.Bytes(r.Bytes)));
            Message = "Analysis finished (top-level folders; inaccessible ones skipped).";
        }
        catch (OperationCanceledException) { Message = "Przerwano."; }
        finally { Busy = false; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();
}

// ---------------- Historia ----------------
public sealed record HistoryRow(string Time, string Action, string Title, string Result, bool Success);
public sealed record DiffRow(string Title, string Before, string Now);

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IStateStore _store; private readonly ProfileService _profiles; private readonly TweakEngine _engine;
    private readonly DialogService _dialogs; private readonly AppContextService _ctx; private readonly OptimizationsViewModel _opt;

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public ObservableCollection<string> Baselines { get; } = new();
    public ObservableCollection<DiffRow> Diff { get; } = new();
    [ObservableProperty] private int _baselineIndex = -1;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _applied = "";
    [ObservableProperty] private bool _busy;

    public HistoryViewModel(IStateStore store, ProfileService profiles, TweakEngine engine, DialogService dialogs, AppContextService ctx, OptimizationsViewModel opt)
    { _store = store; _profiles = profiles; _engine = engine; _dialogs = dialogs; _ctx = ctx; _opt = opt; Reload(); }

    public void Reload()
    {
        Rows.Clear();
        foreach (var h in _store.State.History.AsEnumerable().Reverse().Take(300))
            Rows.Add(new HistoryRow(h.Time.ToString("g"), h.Action switch { HistoryAction.Apply => "Applied", HistoryAction.Revert => "Reverted", HistoryAction.Run => "Ran", _ => "Profile" }, h.Title, h.Message, h.Success));
        Baselines.Clear();
        foreach (var b in _store.State.Baselines) Baselines.Add($"{b.Name} ({b.Time:g})");
        Applied = $"Changes that can be undone: {_store.State.Snapshots.Count}";
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (_store.State.Snapshots.Count == 0) { Message = "Nothing to undo."; return; }
        if (!await _dialogs.ConfirmAsync("Undo all changes", $"Restore previous settings for {_store.State.Snapshots.Count} changes made by NEOX?", "Undo everything")) return;
        Busy = true;
        try { var n = await _profiles.RevertAllAsync(_ctx.Current); Message = $"Undone: {n}. Remaining: {_store.State.Snapshots.Count} (need administrator rights or could not be undone)."; }
        finally { Busy = false; }
        _opt.ReloadAppliedFlags(); Reload();
    }

    [RelayCommand]
    private async Task CaptureBaselineAsync()
    {
        var name = await _dialogs.PromptAsync("Save configuration", "Name (e.g. Before optimizing)");
        if (name is null) return;
        Busy = true; Message = "Reading setting states…";
        try { await _profiles.CaptureBaselineAsync(name, _ctx.Current); Message = "Baseline configuration saved."; }
        finally { Busy = false; }
        Reload();
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (BaselineIndex < 0 || BaselineIndex >= _store.State.Baselines.Count) { Message = "Pick a saved configuration."; return; }
        Busy = true; Message = "Comparing…"; Diff.Clear();
        try
        {
            var diff = await _profiles.CompareAsync(_store.State.Baselines[BaselineIndex], _ctx.Current);
            foreach (var d in diff) Diff.Add(new DiffRow(d.Tweak.Title, Name(d.Before), Name(d.Now)));
            Message = diff.Count == 0 ? "No differences." : $"Differences: {diff.Count}.";
        }
        finally { Busy = false; }
    }

    private static string Name(TweakStatus s) => s switch { TweakStatus.Applied => "zastosowane", TweakStatus.NotApplied => "niezastosowane", _ => "nieznane" };

    [RelayCommand]
    private async Task RevertOneAsync(string id) { await _engine.RevertAsync(id, _ctx.Current); _opt.ReloadAppliedFlags(); Reload(); }
}

// ---------------- Ustawienia ----------------
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IStateStore _store; private readonly IElevationService _elev; private readonly UpdateService _updates; private readonly DialogService _dialogs;
    [ObservableProperty] private bool _restorePointBeforeProfiles;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _updateBusy;
    public string AdminStatus => _elev.IsElevated ? "Running as administrator." : "Standard mode: changes that need administrator rights are blocked.";
    public string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neox");
    public string Version => $"NEOX {UpdateService.Current} (WinUI 3, .NET 10)";

    public SettingsViewModel(IStateStore store, IElevationService elev, UpdateService updates, DialogService dialogs)
    {
        _store = store; _elev = elev; _updates = updates; _dialogs = dialogs;
        _restorePointBeforeProfiles = store.State.Settings.CreateRestorePointBeforeProfiles;
        _autoUpdate = store.State.Settings.AutoUpdate;
    }

    partial void OnRestorePointBeforeProfilesChanged(bool value)
    { _store.State.Settings.CreateRestorePointBeforeProfiles = value; _ = _store.SaveAsync(); }

    partial void OnAutoUpdateChanged(bool value)
    { _store.State.Settings.AutoUpdate = value; _ = _store.SaveAsync(); }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (UpdateBusy) return;
        UpdateBusy = true; UpdateStatus = "Checking…";
        try
        {
            var (info, msg) = await _updates.CheckAsync();
            UpdateStatus = msg;
            if (info is null) return;
            if (!await _dialogs.ConfirmAsync("Aktualizacja", $"Version {info.Version} is available. Download and install now? The app will close and restart (a UAC prompt will appear).", "Zainstaluj")) return;
            var progress = new Progress<double>(p => UpdateStatus = $"Downloading… {p:P0}");
            var path = await _updates.DownloadAsync(info, progress);
            UpdateStatus = "Installing…";
            if (_updates.LaunchInstaller(path)) Microsoft.UI.Xaml.Application.Current.Exit();
            else UpdateStatus = "Installation cancelled (UAC).";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        { UpdateStatus = "Could not download the update: " + ex.Message; }
        finally { UpdateBusy = false; }
    }

    [RelayCommand] private void OpenDataFolder() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
}
