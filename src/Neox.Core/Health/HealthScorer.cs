namespace Neox.Core.Health;

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
                < 10 => new("Free disk space", $"Only {i.WorstDiskFreePercent:F0}% free space left", 0.0, 25, FixKind.OpenDisk),
                < 20 => new("Free disk space", $"Free space: {i.WorstDiskFreePercent:F0}% (20%+ recommended)", 0.5, 25, FixKind.OpenDisk),
                _ => new("Free disk space", $"Free space: {i.WorstDiskFreePercent:F0}%", 1.0, 25, FixKind.None),
            },
            i.UptimeDays switch
            {
                > 14 => new("Time since last restart", $"Up for {i.UptimeDays:F0} days without a restart", 0.0, 15, FixKind.Restart),
                > 7 => new("Time since last restart", $"Up for {i.UptimeDays:F0} days without a restart", 0.5, 15, FixKind.Restart),
                _ => new("Time since last restart", "Restarted recently", 1.0, 15, FixKind.None),
            },
            i.StartupEnabledCount switch
            {
                > 12 => new("Startup programs", $"{i.StartupEnabledCount} programs start with Windows", 0.0, 15, FixKind.OpenStartup),
                > 8 => new("Startup programs", $"{i.StartupEnabledCount} programs start with Windows", 0.5, 15, FixKind.OpenStartup),
                _ => new("Startup programs", $"{i.StartupEnabledCount} startup programs", 1.0, 15, FixKind.None),
            },
            i.AttainableTweaks == 0
                ? new("Applied optimizations", "No optimizations available", 1.0, 30, FixKind.None)
                : CoverageCheck(i),
            i.PendingRestart
                ? new("Changes waiting for a restart", "Some optimizations only take effect after a restart", 0.0, 15, FixKind.Restart)
                : new("Changes waiting for a restart", "No pending restarts", 1.0, 15, FixKind.None),
            i.RamLoadPercent switch
            {
                > 85 => new("Memory usage", $"RAM is {i.RamLoadPercent}% in use", 0.0, 10, FixKind.OpenStartup),
                > 70 => new("Memory usage", $"RAM is {i.RamLoadPercent}% in use", 0.5, 10, FixKind.OpenStartup),
                _ => new("Memory usage", $"RAM is {i.RamLoadPercent}% in use", 1.0, 10, FixKind.None),
            },
        };

        var total = checks.Sum(c => c.Weight);
        var score = (int)Math.Round(checks.Sum(c => c.Score * c.Weight) / total * 100.0);
        var label = score switch { >= 90 => "Excellent", >= 75 => "Good", >= 55 => "Could be better", _ => "Needs attention" };
        return new HealthReport(score, label, checks);
    }

    private static HealthCheck CoverageCheck(HealthInput i)
    {
        var ratio = Math.Clamp(i.AppliedTweaks / (double)i.AttainableTweaks, 0, 1);
        var detail = $"Applied {i.AppliedTweaks} of {i.AttainableTweaks} safe optimizations";
        return new("Applied optimizations", detail, ratio, 30, ratio >= 0.99 ? FixKind.None : FixKind.ApplySafeProfile);
    }
}
