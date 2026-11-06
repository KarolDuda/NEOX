using System.ComponentModel;
using System.Diagnostics;

namespace Tachyvex.Windows;

public sealed record MonitorSample(double CpuPercent, int RamPercent, long RamUsedBytes, long RamTotalBytes, double? GpuPercent);

/// <summary>
/// Monitoring na żądanie: licznik działa tylko po Start() (widok widoczny) i kończy po Stop().
/// Brak aktywnego odpytywania, gdy nikt nie patrzy. GPU: licznik "GPU Engine" (3D), null gdy sterownik go nie udostępnia.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private PerformanceCounter? _cpu;
    private List<PerformanceCounter> _gpu = new();
    private CancellationTokenSource? _cts;
    private int _tick;

    public event Action<MonitorSample>? SampleReady;

    public void Start(TimeSpan? interval = null)
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var period = interval ?? TimeSpan.FromSeconds(1);
        _ = Task.Run(async () =>
        {
            try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true); _cpu.NextValue(); }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception) { _cpu = null; }
            using var timer = new PeriodicTimer(period);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    SampleReady?.Invoke(Take());
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        DisposeCounters();
    }

    private MonitorSample Take()
    {
        var (total, avail, load) = SystemInfo.GlobalMemory();
        double cpu = 0;
        try { cpu = _cpu?.NextValue() ?? 0; } catch (InvalidOperationException) { }
        return new MonitorSample(Math.Clamp(cpu, 0, 100), load, total - avail, total, ReadGpu());
    }

    private double? ReadGpu()
    {
        try
        {
            if (_tick++ % 10 == 0) RefreshGpuCounters(); // lista instancji zmienia się z procesami
            if (_gpu.Count == 0) return null;
            double sum = 0;
            foreach (var c in _gpu) { try { sum += c.NextValue(); } catch (InvalidOperationException) { } }
            return Math.Clamp(sum, 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    private void RefreshGpuCounters()
    {
        foreach (var c in _gpu) c.Dispose();
        _gpu = new List<PerformanceCounter>();
        if (!PerformanceCounterCategory.Exists("GPU Engine")) return;
        var cat = new PerformanceCounterCategory("GPU Engine");
        foreach (var inst in cat.GetInstanceNames().Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)).Take(64))
        {
            try { var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true); pc.NextValue(); _gpu.Add(pc); }
            catch (InvalidOperationException) { }
        }
    }

    private void DisposeCounters()
    {
        _cpu?.Dispose(); _cpu = null;
        foreach (var c in _gpu) c.Dispose();
        _gpu = new List<PerformanceCounter>();
    }

    public void Dispose() => Stop();
}
