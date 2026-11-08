using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace Neox.Windows;

public sealed record CpuInfo(string Name, int Cores, int Threads, int MaxMHz);
public sealed record GpuInfo(string Name, string DriverVersion, long VramBytes, int RefreshHz);
public sealed record DiskInfo(string Model, string Type, long SizeBytes, string Health);
public sealed record VolumeInfo(string Name, long TotalBytes, long FreeBytes, string Format);
public sealed record OsInfo(string Caption, string Version, string Build, DateTime LastBoot);

/// <summary>Informacje o sprzęcie przez WMI (System.Management). Każda sekcja jest odporna na brak danych.</summary>
public static class SystemInfo
{
    public static CpuInfo? Cpu() => Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor",
        r => new CpuInfo(S(r, "Name"), I(r, "NumberOfCores"), I(r, "NumberOfLogicalProcessors"), I(r, "MaxClockSpeed"))).FirstOrDefault();

    public static IReadOnlyList<GpuInfo> Gpus() => Query("SELECT Name,DriverVersion,AdapterRAM,CurrentRefreshRate FROM Win32_VideoController",
        r => new GpuInfo(S(r, "Name"), S(r, "DriverVersion"), L(r, "AdapterRAM"), I(r, "CurrentRefreshRate")));

    public static long TotalRamBytes() => GlobalMemory().Total;

    public static IReadOnlyList<DiskInfo> Disks() => Query("SELECT Model,MediaType,Size,Status FROM Win32_DiskDrive",
        r => new DiskInfo(S(r, "Model"), S(r, "MediaType"), L(r, "Size"), S(r, "Status")));

    public static IReadOnlyList<VolumeInfo> Volumes() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
        .Select(d => new VolumeInfo(d.Name, d.TotalSize, d.AvailableFreeSpace, d.DriveFormat)).ToList();

    public static OsInfo? Os() => Query("SELECT Caption,Version,BuildNumber,LastBootUpTime FROM Win32_OperatingSystem",
        r => new OsInfo(S(r, "Caption"), S(r, "Version"), S(r, "BuildNumber"),
            ManagementDateTimeConverter.ToDateTime(S(r, "LastBootUpTime")))).FirstOrDefault();

    public static string Describe()
    {
        var sb = new StringBuilder();
        var cpu = Cpu();
        if (cpu is not null) sb.AppendLine($"CPU: {cpu.Name} ({cpu.Cores} cores / {cpu.Threads} threads)");
        foreach (var g in Gpus()) sb.AppendLine($"GPU: {g.Name} | sterownik {g.DriverVersion} | {g.RefreshHz} Hz");
        sb.AppendLine($"RAM: {TotalRamBytes() / 1073741824.0:F0} GB");
        var os = Os();
        if (os is not null) sb.AppendLine($"System: {os.Caption} (build {os.Build})").AppendLine($"Ostatni start: {os.LastBoot:g}");
        return sb.ToString().Trim();
    }

    // --- RAM bez WMI ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    public static (long Total, long Available, int LoadPercent) GlobalMemory()
    {
        var m = new MemoryStatusEx();
        return GlobalMemoryStatusEx(m) ? ((long)m.ullTotalPhys, (long)m.ullAvailPhys, (int)m.dwMemoryLoad) : (0, 0, 0);
    }

    // --- helpers ---
    private static List<T> Query<T>(string wql, Func<ManagementBaseObject, T> map)
    {
        var list = new List<T>();
        try
        {
            using var s = new ManagementObjectSearcher(wql);
            foreach (var o in s.Get()) using (o) list.Add(map(o));
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        return list;
    }

    private static string S(ManagementBaseObject o, string p) => o[p]?.ToString() ?? "";
    private static int I(ManagementBaseObject o, string p) => int.TryParse(o[p]?.ToString(), out var v) ? v : 0;
    private static long L(ManagementBaseObject o, string p) => long.TryParse(o[p]?.ToString(), out var v) ? v : 0;
}
