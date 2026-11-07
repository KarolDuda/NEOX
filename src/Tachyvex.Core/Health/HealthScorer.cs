namespace Tachyvex.Core.Health;

public sealed record HealthInput(
    double WorstDiskFreePercent,
    int RamLoadPercent,
    double UptimeDays,
    int StartupEnabledCount,
    int AttainableTweaks,
    int AppliedTweaks,
    bool PendingRestart);

/// <summary>Co użytkownik może zrobić z danym problemem.</summary>
public enum FixKind { None, ApplySafeProfile, OpenStartup, OpenDisk, Restart }

public sealed record HealthCheck(string Name, string Detail, double Score, double Weight, FixKind Fix)
{
    public bool Ok => Score >= 0.99;
}

public sealed record HealthReport(int Score, string Label, IReadOnlyList<HealthCheck> Checks);

/// <summary>Czysta funkcja: wynik 0–100 z jawnych, objaśnialnych reguł. Bez magii i bez zmyślonych liczb.</summary>
public static class HealthScorer
{
    public static HealthReport Score(HealthInput i)
    {
        var checks = new List<HealthCheck>
        {
            i.WorstDiskFreePercent switch
            {
                < 10 => new("Wolne miejsce na dysku", $"Zostało tylko {i.WorstDiskFreePercent:F0}% wolnego miejsca", 0.0, 25, FixKind.OpenDisk),
                < 20 => new("Wolne miejsce na dysku", $"Wolne miejsce: {i.WorstDiskFreePercent:F0}% (zalecane powyżej 20%)", 0.5, 25, FixKind.OpenDisk),
                _ => new("Wolne miejsce na dysku", $"Wolne miejsce: {i.WorstDiskFreePercent:F0}%", 1.0, 25, FixKind.None),
            },
            i.UptimeDays switch
            {
                > 14 => new("Czas od ostatniego restartu", $"System działa {i.UptimeDays:F0} dni bez restartu", 0.0, 15, FixKind.Restart),
                > 7 => new("Czas od ostatniego restartu", $"System działa {i.UptimeDays:F0} dni bez restartu", 0.5, 15, FixKind.Restart),
                _ => new("Czas od ostatniego restartu", "Niedawny restart", 1.0, 15, FixKind.None),
            },
            i.StartupEnabledCount switch
            {
                > 12 => new("Programy w autostarcie", $"{i.StartupEnabledCount} programów startuje z systemem", 0.0, 15, FixKind.OpenStartup),
                > 8 => new("Programy w autostarcie", $"{i.StartupEnabledCount} programów startuje z systemem", 0.5, 15, FixKind.OpenStartup),
                _ => new("Programy w autostarcie", $"{i.StartupEnabledCount} programów w autostarcie", 1.0, 15, FixKind.None),
            },
            i.AttainableTweaks == 0
                ? new("Zastosowane optymalizacje", "Brak dostępnych optymalizacji", 1.0, 30, FixKind.None)
                : CoverageCheck(i),
            i.PendingRestart
                ? new("Zmiany czekające na restart", "Część optymalizacji zadziała dopiero po restarcie", 0.0, 15, FixKind.Restart)
                : new("Zmiany czekające na restart", "Brak oczekujących restartów", 1.0, 15, FixKind.None),
            i.RamLoadPercent switch
            {
                > 85 => new("Zajętość pamięci RAM", $"RAM zajęty w {i.RamLoadPercent}%", 0.0, 10, FixKind.OpenStartup),
                > 70 => new("Zajętość pamięci RAM", $"RAM zajęty w {i.RamLoadPercent}%", 0.5, 10, FixKind.OpenStartup),
                _ => new("Zajętość pamięci RAM", $"RAM zajęty w {i.RamLoadPercent}%", 1.0, 10, FixKind.None),
            },
        };

        var total = checks.Sum(c => c.Weight);
        var score = (int)Math.Round(checks.Sum(c => c.Score * c.Weight) / total * 100.0);
        var label = score switch { >= 90 => "Znakomicie", >= 75 => "Dobrze", >= 55 => "Można poprawić", _ => "Wymaga uwagi" };
        return new HealthReport(score, label, checks);
    }

    private static HealthCheck CoverageCheck(HealthInput i)
    {
        var ratio = Math.Clamp(i.AppliedTweaks / (double)i.AttainableTweaks, 0, 1);
        var detail = $"Zastosowano {i.AppliedTweaks} z {i.AttainableTweaks} bezpiecznych optymalizacji";
        return new("Zastosowane optymalizacje", detail, ratio, 30, ratio >= 0.99 ? FixKind.None : FixKind.ApplySafeProfile);
    }
}
