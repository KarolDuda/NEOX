namespace Neox.Core.AutoTune;

/// <summary>Opis sprzętu istotny dla doboru optymalizacji.</summary>
public sealed record HardwareProfile(int Cores, int Threads, long RamBytes, bool IsLaptop, bool HasSsd, bool HasDiscreteGpu, int Build)
{
    public double RamGb => RamBytes / 1073741824.0;
    public string Describe() =>
        $"{Cores} cores / {Threads} threads · RAM {RamGb:F0} GB · {(IsLaptop ? "laptop" : "desktop")} · " +
        $"{(HasSsd ? "SSD" : "no SSD detected")} · {(HasDiscreteGpu ? "dedicated GPU" : "integrated or unknown GPU")}";
}

public interface IHardwareScanner
{
    Task<HardwareProfile> ScanAsync(CancellationToken ct = default);
}

/// <summary>Wynik mikro-pomiaru. To nie jest FPS: mierzy tylko to, co da się powtarzalnie zmierzyć bez gry.</summary>
public sealed record BenchmarkResult(double CpuOpsPerSec, double MemoryMBps, double TimerAvgMs, double TimerP99Ms);

public interface IBenchmarkSuite
{
    Task<BenchmarkResult> RunAsync(IProgress<string>? progress, CancellationToken ct = default);
}

public enum Verdict { Better, Worse, Noise }

public sealed record MetricDelta(string Name, string Unit, double Before, double After, double Percent, Verdict Verdict, bool HigherIsBetter);

public static class BenchmarkComparer
{
    /// <summary>Zmiany poniżej progu uznajemy za szum pomiarowy, nie za poprawę.</summary>
    public const double ThroughputNoisePercent = 5;
    public const double LatencyNoisePercent = 10;

    public static IReadOnlyList<MetricDelta> Compare(BenchmarkResult before, BenchmarkResult after) => new[]
    {
        Make("CPU performance (synthetic test)", "Mops/s", before.CpuOpsPerSec / 1e6, after.CpuOpsPerSec / 1e6, true, ThroughputNoisePercent),
        Make("Memory bandwidth", "MB/s", before.MemoryMBps, after.MemoryMBps, true, ThroughputNoisePercent),
        Make("Timer latency (average)", "ms", before.TimerAvgMs, after.TimerAvgMs, false, LatencyNoisePercent),
        Make("Timer latency (99th percentile)", "ms", before.TimerP99Ms, after.TimerP99Ms, false, LatencyNoisePercent),
    };

    public static MetricDelta Make(string name, string unit, double before, double after, bool higherIsBetter, double noisePercent)
    {
        var pct = before <= 0 ? 0 : (after - before) / before * 100.0;
        var gain = higherIsBetter ? pct : -pct;
        var verdict = Math.Abs(pct) < noisePercent ? Verdict.Noise : gain > 0 ? Verdict.Better : Verdict.Worse;
        return new MetricDelta(name, unit, before, after, pct, verdict, higherIsBetter);
    }
}

public sealed record PlannedTweak(string Id, string Title, string Reason);
public sealed record AutoTunePlan(HardwareProfile Hardware, IReadOnlyList<PlannedTweak> Planned, IReadOnlyList<PlannedTweak> Skipped);
