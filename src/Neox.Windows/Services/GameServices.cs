using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Neox.Windows;

public sealed record DetectedGame(string Name, string ExePath);

/// <summary>Wykrywanie zainstalowanych gier (manifesty Epic Games) — bez zgadywania liter dysków.</summary>
public static class GameDiscovery
{
    public static IReadOnlyList<DetectedGame> Epic()
    {
        var list = new List<DetectedGame>();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                var install = r.TryGetProperty("InstallLocation", out var i) ? i.GetString() ?? "" : "";
                var launch = r.TryGetProperty("LaunchExecutable", out var l) ? l.GetString() ?? "" : "";
                var name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? "" : "";
                var exe = Path.GetFullPath(Path.Combine(install, launch));
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                    list.Add(new DetectedGame(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name, exe));
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or NotSupportedException) { }
        }
        return list.OrderBy(g => g.Name).ToList();
    }

    public static bool IsRunning(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        var procs = Process.GetProcessesByName(name);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }
}

/// <summary>Edycja GameUserSettings.ini Fortnite (V-Sync i HDR wyłączane) z kopią zapasową i przywracaniem.</summary>
public sealed class FortniteConfigService
{
    private static string IniPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
    private static string BackupPath => IniPath + ".neox.bak";

    private static readonly (string Key, string Bad, string Good)[] Rules =
    {
        ("bUseVSync", "True", "False"),
        ("bUseHDRDisplayOutput", "True", "False"),
    };

    public bool Exists => File.Exists(IniPath);
    public bool HasBackup => File.Exists(BackupPath);

    public IReadOnlyList<string> PendingChanges()
    {
        if (!Exists) return Array.Empty<string>();
        var txt = File.ReadAllText(IniPath);
        return Rules.Where(r => Regex.IsMatch(txt, $@"^{r.Key}={r.Bad}\s*$", RegexOptions.Multiline)).Select(r => $"{r.Key}: {r.Bad} → {r.Good}").ToList();
    }

    public string Apply()
    {
        if (!Exists) throw new InvalidOperationException("Fortnite settings file not found.");
        if (GameDiscovery.IsRunning("FortniteClient-Win64-Shipping"))
            throw new InvalidOperationException("Close Fortnite before changing settings (the game overwrites the file on exit).");
        var txt = File.ReadAllText(IniPath);
        var changed = 0;
        foreach (var (key, bad, good) in Rules)
        {
            var next = Regex.Replace(txt, $@"^({key})={bad}\s*$", $"$1={good}", RegexOptions.Multiline);
            if (next != txt) { changed++; txt = next; }
        }
        if (changed == 0) return "Nothing to change: V-Sync and HDR are already off.";
        File.Copy(IniPath, BackupPath, overwrite: true);
        File.WriteAllText(IniPath, txt);
        return $"Changed {changed} settings. Backup: {BackupPath}";
    }

    public string Restore()
    {
        if (!HasBackup) throw new InvalidOperationException("No backup available.");
        if (GameDiscovery.IsRunning("FortniteClient-Win64-Shipping"))
            throw new InvalidOperationException("Close Fortnite before restoring settings.");
        File.Copy(BackupPath, IniPath, overwrite: true);
        File.Delete(BackupPath);
        return "Settings restored to their state before the change.";
    }
}
