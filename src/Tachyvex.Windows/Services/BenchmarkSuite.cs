using System.Diagnostics;
using Tachyvex.Core.AutoTune;

namespace Tachyvex.Windows;

/// <summary>
/// Krótkie, powtarzalne mikro-pomiary (ok. 10 s): CPU, przepustowość RAM i opóźnienie timera.
/// Każdy pomiar to mediana kilku przebiegów po rozgrzewce. Wyniki podlegają szumowi, dlatego porównanie ma próg szumu.
/// </summary>
public sealed class BenchmarkSuite : IBenchmarkSuite
{
    private static long _sink;

    public Task<BenchmarkResult> RunAsync(IProgress<string>? progress, CancellationToken ct = default) => Task.Run(() =>
    {
        progress?.Report("Measuring: processor…");
        var cpu = Median(5, () => CpuOnce(), ct);
        progress?.Report("Measuring: memory…");
        var mem = Median(5, () => MemOnce(), ct);
        progress?.Report("Measuring: timer…");
        var (avg, p99) = TimerOnce(ct);
        return new BenchmarkResult(cpu, mem, avg, p99);
    }, ct);

    private static double Median(int runs, Func<double> f, CancellationToken ct)
    {
        f(); // rozgrzewka (JIT, cache)
        var v = new List<double>();
        for (var i = 0; i < runs; i++) { ct.ThrowIfCancellationRequested(); v.Add(f()); }
        v.Sort();
        return v[v.Count / 2];
    }

    private static double CpuOnce()
    {
        const int n = 30_000_000;
        ulong x = 88172645463325252UL; ulong acc = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < n; i++)
        {
            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
            acc += x * 0x9E3779B97F4A7C15UL;
        }
        sw.Stop();
        _sink ^= (long)acc;
        return n / sw.Elapsed.TotalSeconds;
    }

    private static double MemOnce()
    {
        const int size = 64 * 1024 * 1024;
        var a = new byte[size]; var b = new byte[size];
        a[0] = 1;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 6; i++) Buffer.BlockCopy(a, 0, b, 0, size);
        sw.Stop();
        _sink ^= b[size - 1];
        return 6.0 * size / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
    }

    private static (double Avg, double P99) TimerOnce(CancellationToken ct)
    {
        var samples = new List<double>(300);
        var sw = new Stopwatch();
        for (var i = 0; i < 300; i++)
        {
            ct.ThrowIfCancellationRequested();
            sw.Restart();
            Thread.Sleep(1);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return (samples.Average(), samples[(int)(samples.Count * 0.99) - 1]);
    }
}
