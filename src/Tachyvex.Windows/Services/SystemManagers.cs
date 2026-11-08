using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.Windows;

public sealed record StartupEntry(string Name, string Command, string Location, bool Enabled, string Hive, string KeyPath);
public sealed record ProcessEntry(int Pid, string Name, long WorkingSetBytes, double CpuSeconds);
public sealed record ServiceEntry(string Name, string DisplayName, string Status, string StartType, bool IsCritical);
public sealed record FolderUsage(string Path, long Bytes);

/// <summary>Autostart: odczyt i bezpieczne włączanie/wyłączanie przez mechanizm StartupApproved (jak Menedżer zadań), bez kasowania wpisów.</summary>
public sealed class StartupManager
{
    private static readonly (string Hive, string Run, string Approved, string Label)[] Sources =
    {
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Current user"),
        ("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "All users"),
    };

    private readonly RegistryService _reg = new();

    public IReadOnlyList<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        foreach (var (hive, run, approved, label) in Sources)
        {
            using var root = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = root.OpenSubKey(run);
            if (key is null) continue;
            foreach (var name in key.GetValueNames())
            {
                var enabled = true;
                if (_reg.Read(hive, approved, name) is { Value: byte[] b } && b.Length > 0) enabled = (b[0] & 1) == 0 || b[0] == 2;
                list.Add(new StartupEntry(name, key.GetValue(name)?.ToString() ?? "", label, enabled, hive, run));
            }
        }
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void SetEnabled(StartupEntry e, bool enabled)
    {
        var approved = Sources.First(s => s.Hive == e.Hive && s.Run == e.KeyPath).Approved;
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3; // 02 = włączony, 03 = wyłączony
        _reg.Write(e.Hive, approved, e.Name, RegistryValueKind.Binary, data);
    }
}

public sealed class ProcessOverview
{
    public IReadOnlyList<ProcessEntry> Top(int count = 25)
    {
        var res = new List<ProcessEntry>();
        foreach (var p in Process.GetProcesses())
        {
            try { res.Add(new ProcessEntry(p.Id, p.ProcessName, p.WorkingSet64, p.TotalProcessorTime.TotalSeconds)); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        return res.OrderByDescending(r => r.WorkingSetBytes).Take(count).ToList();
    }
}

/// <summary>Podgląd usług. Usługi krytyczne są oznaczone i nigdy nie są oferowane do wyłączenia.</summary>
public sealed class WindowsServicesManager
{
    public static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "wuauserv", "WinDefend", "Sense", "mpssvc", "SecurityHealthService", "wscsvc", "EventLog", "RpcSs", "RpcEptMapper",
        "DcomLaunch", "LSM", "SamSs", "BFE", "Dhcp", "Dnscache", "nsi", "CryptSvc", "TrustedInstaller", "gpsvc", "ProfSvc",
    };

    public IReadOnlyList<ServiceEntry> List()
    {
        var reg = new RegistryService();
        var res = new List<ServiceEntry>();
        foreach (var s in ServiceController.GetServices())
        {
            using (s)
            {
                string start;
                try { start = s.StartType.ToString(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { start = "?"; }
                res.Add(new ServiceEntry(s.ServiceName, s.DisplayName, s.Status.ToString(), start, Critical.Contains(s.ServiceName)));
            }
        }
        return res.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public sealed class StorageAnalyzer
{
    /// <summary>Rozmiary katalogów pierwszego poziomu wskazanego folderu (z pominięciem dowiązań), największe najpierw.</summary>
    public async Task<IReadOnlyList<FolderUsage>> AnalyzeAsync(string root, int top = 20, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var res = new List<FolderUsage>();
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(dir);
                long total = 0;
                try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opt)) { total += f.Length; ct.ThrowIfCancellationRequested(); } }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                res.Add(new FolderUsage(dir, total));
            }
            return (IReadOnlyList<FolderUsage>)res.OrderByDescending(r => r.Bytes).Take(top).ToList();
        }, ct);
    }
}
