using Neox.Windows;
using Xunit;

namespace Neox.Tests.Windows;

public class AutoTuneWindowsTests
{
    [Fact]
    public async Task Benchmark_returns_positive_values()
    {
        var r = await new BenchmarkSuite().RunAsync(null);
        Assert.True(r.CpuOpsPerSec > 0);
        Assert.True(r.MemoryMBps > 0);
        Assert.True(r.TimerAvgMs >= 0.5);
        Assert.True(r.TimerP99Ms >= r.TimerAvgMs * 0.5);
    }

    [Fact]
    public async Task Hardware_scan_returns_sane_values()
    {
        var hw = await new HardwareScanner().ScanAsync();
        Assert.True(hw.Threads >= 1);
        Assert.True(hw.RamBytes > 512L * 1024 * 1024);
    }
}
