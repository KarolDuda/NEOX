using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Tachyvex.App.Services;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;
using Tachyvex.Windows;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.App.ViewModels;

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
    [ObservableProperty] private string _gpuText = "niedostępne";
    [ObservableProperty] private string _hardware = "Odczyt sprzętu…";
    [ObservableProperty] private string _health = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;

    public ObservableCollection<Profile> Profiles { get; } = new();

    public DashboardViewModel(SystemMonitor monitor, ProfileService profiles, TweakEngine engine, IRestorePointService restore, DialogService dialogs, AppContextService ctx)
    {
        _monitor = monitor; _profiles = profiles; _engine = engine; _restore = restore; _dialogs = dialogs; _ctx = ctx;
        _monitor.SampleReady += OnSample;
        foreach (var p in profiles.All()) Profiles.Add(p);
    }

    private void OnSample(MonitorSample s) => _ui.TryEnqueue(() =>
    {
        Cpu = s.CpuPercent; CpuText = $"{s.CpuPercent:F0}%";
        Ram = s.RamPercent; RamText = $"{s.RamPercent}%  ({Fmt.Bytes(s.RamUsedBytes)} / {Fmt.Bytes(s.RamTotalBytes)})";
        if (s.GpuPercent is { } g) { Gpu = g; GpuText = $"{g:F0}%"; } else { Gpu = 0; GpuText = "niedostępne na tym sprzęcie"; }
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
                os is null ? null : $"System: {os.Caption} (build {os.Build})",
            }.Where(x => x is not null));
            var vols = SystemInfo.Volumes();
            var low = vols.Where(v => v.TotalBytes > 0 && v.FreeBytes < v.TotalBytes * 0.10).Select(v => v.Name).ToList();
            var health = low.Count == 0 ? "Dyski: wolne miejsce w normie." : "Mało wolnego miejsca (<10%): " + string.Join(", ", low);
            return (hw, health);
        });
        Hardware = info.hw; Health = info.health;
    }

    public void OnHidden() => _monitor.Stop();

    [RelayCommand]
    private async Task ApplyProfileAsync(Profile p)
    {
        if (Busy) return;
        var list = string.Join("\n", p.TweakIds.Take(12).Select(id => "• " + _engine.Catalog.Get(id).Title)) + (p.TweakIds.Count > 12 ? $"\n… i {p.TweakIds.Count - 12} więcej" : "");
        if (!await _dialogs.ConfirmAsync($"Profil „{p.Name}”", $"{p.Description}\n\nZmian: {p.TweakIds.Count}\n{list}\n\nKażda zmiana ma kopię zapasową i można ją cofnąć w Historii.", "Zastosuj profil")) return;
        Busy = true;
        try
        {
            var progress = new Progress<string>(s => Message = s);
            var rep = await _profiles.ApplyAsync(p, _ctx.Current, progress);
            Message = $"Zastosowano {rep.Applied}, pominięto {rep.Skipped}, błędów {rep.Failed}.";
            await _dialogs.InfoAsync("Wynik profilu", string.Join("\n", rep.Lines) + (rep.Restart != RestartRequirement.None ? "\n\nAby zmiany zadziałały, uruchom ponownie komputer." : ""));
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task RestorePointAsync()
    {
        Busy = true;
        try
        {
            var r = await _restore.CreateAsync("TACHYVEX", CancellationToken.None);
            Message = r.Message;
            if (!r.Success) await _dialogs.InfoAsync("Punkt przywracania", r.Message + "\n\nWymaga administratora i włączonej ochrony systemu (limit: 1 punkt na 24 h).");
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

    public OptimizationsViewModel Optimizations { get; }
    public ObservableCollection<Profile> Profiles { get; } = new();
    public ObservableCollection<DetectedGame> Games { get; } = new();
    public ObservableCollection<TweakItemViewModel> GameTweaks { get; } = new();

    [ObservableProperty] private string _gameExePath = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _fortniteInfo = "";
    [ObservableProperty] private bool _busy;

    public GamingViewModel(ProfileService profiles, IStateStore store, DialogService dialogs, AppContextService ctx, TweakEngine engine,
        OptimizationsViewModel optimizations, FortniteConfigService fn)
    {
        _profiles = profiles; _store = store; _dialogs = dialogs; _ctx = ctx; _engine = engine; _fn = fn; Optimizations = optimizations;
        GameExePath = store.State.Settings.GameExePath ?? "";
        Reload();
        foreach (var t in optimizations.All.Where(i => i.Tweak.Kind == TweakKind.RegPerGame)) GameTweaks.Add(t);
    }

    private void Reload()
    {
        Profiles.Clear();
        foreach (var p in _profiles.All()) Profiles.Add(p);
        FortniteInfo = !_fn.Exists ? "Nie znaleziono pliku ustawień Fortnite."
            : _fn.PendingChanges().Count > 0 ? "Do zmiany: " + string.Join(", ", _fn.PendingChanges())
            : "V-Sync i HDR już wyłączone.";
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
        Message = found.Count == 0 ? "Nie wykryto gier Epic Games. Wskaż plik .exe ręcznie." : $"Wykryto gier: {found.Count}.";
    }

    [RelayCommand]
    private void UseGame(DetectedGame g) => GameExePath = g.ExePath;

    [RelayCommand]
    private async Task ApplyProfileAsync(Profile p)
    {
        if (Busy) return;
        if (!await _dialogs.ConfirmAsync($"Profil „{p.Name}”", $"{p.Description}\n\nZmian: {p.TweakIds.Count}. Zapisujemy kopie zapasowe; cofniesz je w Historii.", "Zastosuj")) return;
        Busy = true;
        try
        {
            var rep = await _profiles.ApplyAsync(p, _ctx.Current, new Progress<string>(s => Message = s));
            Message = $"Zastosowano {rep.Applied}, pominięto {rep.Skipped}, błędów {rep.Failed}.";
            Optimizations.ReloadAppliedFlags();
            await _dialogs.InfoAsync("Wynik profilu", string.Join("\n", rep.Lines));
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SaveCurrentAsProfileAsync()
    {
        var name = await _dialogs.PromptAsync("Nowy profil z bieżących zmian", "Nazwa profilu");
        if (name is null) return;
        var ids = _store.State.Snapshots.Keys.ToList();
        if (ids.Count == 0) { Message = "Brak zastosowanych zmian do zapisania."; return; }
        await _profiles.SaveCustomAsync(name, ids, "Własny profil z zastosowanych zmian.");
        Reload();
        Message = $"Zapisano profil „{name}” ({ids.Count} zmian).";
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
            Adapters.Add($"{a.Name} ({a.Type}) · {(a.SpeedMbps > 0 ? a.SpeedMbps + " Mb/s" : "?")} · IP {string.Join(", ", a.Addresses.Take(2))} · DNS {(dns.Length == 0 ? "—" : string.Join(", ", dns))} ({(manual ? "ręczny" : "automatyczny")})");
        }
        if (Adapters.Count == 0) Adapters.Add("Brak aktywnej karty sieciowej (Ethernet / Wi-Fi).");
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
        if (!string.IsNullOrWhiteSpace(CustomHost)) targets.Insert(0, ("Własny", CustomHost.Trim()));
        for (var i = 0; i < targets.Count; i++)
        {
            Message = $"Ping: {targets[i].Name}…";
            var s = await _net.PingAsync(targets[i].Host, 8, 2000, null, ct);
            PingRows.Add(new PingRow(targets[i].Name, s.Received == 0 ? "brak odpowiedzi" :
                $"{s.AvgMs:F0} ms (min {s.MinMs:F0}, max {s.MaxMs:F0}) · jitter {s.JitterMs:F1} ms · utrata {s.LossPercent:F0}%"));
            Progress = (i + 1) / (double)targets.Count;
        }
        Message = "Test ping zakończony.";
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
                r.MedianMs is null ? "brak odpowiedzi" : $"{r.MedianMs:F0} ms (błędów: {r.Failures})"));
        var best = results.Where(r => r.MedianMs is not null && r.Failures <= 2).OrderBy(r => r.MedianMs).FirstOrDefault();
        _best = best?.Provider;
        BestDns = best is null ? "" : $"Najszybszy: {best.Provider.Name} ({best.MedianMs:F0} ms)";
        Message = "Test DNS zakończony. Wynik dotyczy Twojego łącza w tej chwili.";
    });

    [RelayCommand]
    private async Task UseBestDnsAsync()
    {
        if (_best is null) { Message = "Najpierw uruchom test DNS."; return; }
        if (!await _dialogs.ConfirmAsync("Zmiana DNS", $"Ustawić DNS na {_best.Name} ({_best.Primary} / {_best.Secondary}) na aktywnych kartach? Poprzednie ustawienia zostaną zapisane i można je przywrócić.", "Ustaw")) return;
        var r = await _net.SetDnsAsync(new[] { _best.Primary, _best.Secondary });
        Message = r.Message; LoadAdapters();
    }

    [RelayCommand]
    private async Task RestoreDnsAsync() { var r = await _net.RestoreDnsAsync(); Message = r.Message; LoadAdapters(); }

    [RelayCommand]
    private Task FindMtuAsync() => RunAsync(async ct =>
    {
        Message = "Szukam MTU…";
        var mtu = await _net.FindMtuAsync("1.1.1.1", ct);
        MtuText = mtu is null ? "Brak odpowiedzi na ping (lub zablokowany ICMP)." : $"MTU: {mtu} bajtów" + (mtu < 1500 ? " (poniżej 1500; sprawdź łącze/VPN)" : "");
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
        Cpu = d.Item1 is null ? "brak danych" : $"{d.Item1.Name}\n{d.Item1.Cores} rdzeni / {d.Item1.Threads} wątków · max {d.Item1.MaxMHz} MHz";
        Gpu = d.Item2.Count == 0 ? "brak danych" : string.Join("\n", d.Item2.Select(g => $"{g.Name} · sterownik {g.DriverVersion} · {g.RefreshHz} Hz"));
        Ram = Fmt.Bytes(d.Item3);
        Os = d.Item4 is null ? "brak danych" : $"{d.Item4.Caption} · wersja {d.Item4.Version} · build {d.Item4.Build}\nOstatni start: {d.Item4.LastBoot:g}";
        Disks.Clear(); foreach (var x in d.Item5) Disks.Add($"{x.Model} · {Fmt.Bytes(x.SizeBytes)} · stan: {x.Health}");
        Volumes.Clear(); foreach (var v in d.Item6) Volumes.Add($"{v.Name} {v.Format} · wolne {Fmt.Bytes(v.FreeBytes)} z {Fmt.Bytes(v.TotalBytes)} ({(v.TotalBytes == 0 ? 0 : 100 - 100 * v.FreeBytes / v.TotalBytes)}% zajęte)");
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
        Services.Clear(); foreach (var s in d.Item3) Services.Add(new ServiceRow(s.DisplayName, s.Name, s.Status, s.StartType, s.IsCritical ? "krytyczna: nie modyfikujemy" : ""));
        Message = $"Programy startowe: {Startup.Count} · usługi: {Services.Count}. Zmiany usług wprowadzasz w zakładce Optymalizacje (z kopią zapasową).";
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
            var rows = await _analyzer.AnalyzeAsync(Drives[Math.Max(0, DriveIndex)], 25, new Progress<string>(s => Message = "Skanuję: " + s), _cts.Token);
            foreach (var r in rows) Folders.Add(new FolderRow(r.Path, Fmt.Bytes(r.Bytes)));
            Message = "Analiza zakończona (foldery najwyższego poziomu; niedostępne pomijane).";
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
            Rows.Add(new HistoryRow(h.Time.ToString("g"), h.Action switch { HistoryAction.Apply => "Zastosowano", HistoryAction.Revert => "Cofnięto", HistoryAction.Run => "Uruchomiono", _ => "Profil" }, h.Title, h.Message, h.Success));
        Baselines.Clear();
        foreach (var b in _store.State.Baselines) Baselines.Add($"{b.Name} ({b.Time:g})");
        Applied = $"Zmian możliwych do cofnięcia: {_store.State.Snapshots.Count}";
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (_store.State.Snapshots.Count == 0) { Message = "Nie ma czego cofać."; return; }
        if (!await _dialogs.ConfirmAsync("Cofnij wszystkie zmiany", $"Przywrócić poprzednie ustawienia dla {_store.State.Snapshots.Count} zmian wprowadzonych przez TACHYVEX?", "Cofnij wszystko")) return;
        Busy = true;
        try { var n = await _profiles.RevertAllAsync(_ctx.Current); Message = $"Cofnięto: {n}. Pozostało: {_store.State.Snapshots.Count} (wymagają administratora lub nie udało się ich cofnąć)."; }
        finally { Busy = false; }
        _opt.ReloadAppliedFlags(); Reload();
    }

    [RelayCommand]
    private async Task CaptureBaselineAsync()
    {
        var name = await _dialogs.PromptAsync("Zapisz konfigurację", "Nazwa (np. Przed optymalizacją)");
        if (name is null) return;
        Busy = true; Message = "Odczytuję stany ustawień…";
        try { await _profiles.CaptureBaselineAsync(name, _ctx.Current); Message = "Zapisano konfigurację odniesienia."; }
        finally { Busy = false; }
        Reload();
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (BaselineIndex < 0 || BaselineIndex >= _store.State.Baselines.Count) { Message = "Wybierz zapisaną konfigurację."; return; }
        Busy = true; Message = "Porównuję…"; Diff.Clear();
        try
        {
            var diff = await _profiles.CompareAsync(_store.State.Baselines[BaselineIndex], _ctx.Current);
            foreach (var d in diff) Diff.Add(new DiffRow(d.Tweak.Title, Name(d.Before), Name(d.Now)));
            Message = diff.Count == 0 ? "Brak różnic." : $"Różnic: {diff.Count}.";
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
    private readonly IStateStore _store; private readonly IElevationService _elev;
    [ObservableProperty] private bool _restorePointBeforeProfiles;
    public string AdminStatus => _elev.IsElevated ? "Uruchomiono jako administrator." : "Tryb standardowy: zmiany wymagające administratora są zablokowane.";
    public string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyvex");
    public string Version => "TACHYVEX 2.0 (WinUI 3, .NET 10)";

    public SettingsViewModel(IStateStore store, IElevationService elev)
    { _store = store; _elev = elev; _restorePointBeforeProfiles = store.State.Settings.CreateRestorePointBeforeProfiles; }

    partial void OnRestorePointBeforeProfilesChanged(bool value)
    { _store.State.Settings.CreateRestorePointBeforeProfiles = value; _ = _store.SaveAsync(); }

    [RelayCommand] private void OpenDataFolder() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
}
