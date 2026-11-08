using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Neox.Core.AutoTune;

namespace Neox.Windows;

/// <summary>Odczyt sprzętu potrzebny Auto-Tune. Gdy czegoś nie da się ustalić, przyjmuje wartość ostrożną.</summary>
public sealed class HardwareScanner : IHardwareScanner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static bool HasBattery()
    {
        // BatteryFlag: 128 = brak baterii, 255 = nieznany. Przy niepewności uznajemy, że to laptop (ostrożniej dla baterii).
        if (!GetSystemPowerStatus(out var s)) return true;
        return s.BatteryFlag != 128;
    }

    public Task<HardwareProfile> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var cpu = SystemInfo.Cpu();
        var gpus = SystemInfo.Gpus();
        var disks = SystemInfo.Disks();
        var os = SystemInfo.Os();
        var discrete = gpus.Any(g => Regex.IsMatch(g.Name, @"NVIDIA|GeForce|Radeon RX|Radeon Pro|Arc A|Arc B", RegexOptions.IgnoreCase));
        var ssd = disks.Any(d => Regex.IsMatch(d.Model + " " + d.Type, @"SSD|NVMe|Solid", RegexOptions.IgnoreCase));
        _ = int.TryParse(os?.Build, out var build);
        return new HardwareProfile(cpu?.Cores ?? Environment.ProcessorCount, cpu?.Threads ?? Environment.ProcessorCount,
            SystemInfo.TotalRamBytes(), HasBattery(), ssd, discrete, build);
    }, ct);
}
