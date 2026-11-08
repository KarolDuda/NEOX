using Tachyvex.Core.Catalog;
using Tachyvex.Core.Models;

namespace Tachyvex.Core.AutoTune;

/// <summary>
/// Dobiera optymalizacje do sprzętu. Wyłącznie zmiany Reg/PowerIdx/PowerPlan o ryzyku zalecanym lub ostrożnym,
/// bez usług, zadań, bcdedit i bez rzeczy, które zmieniają zachowanie systemu w sposób zauważalny dla użytkownika.
/// </summary>
public sealed class AutoTunePlanner
{
    private readonly TweakCatalog _catalog;
    public const string AlreadyAppliedReason = "already applied";

    public AutoTunePlanner(TweakCatalog catalog) => _catalog = catalog;

    private static readonly HashSet<string> LaptopOnlyExcluded = new()
    {
        "cpu_min", "core_parking", "cpu_epp", "pcie_aspm", "usb_suspend", "usb_reg", "power_throttle", "powerPlan", "wlan_perf", "disk_idle",
    };

    /// <summary>Pozycje zmieniające komfort lub zachowanie systemu: Auto-Tune ich nie rusza (można je włączyć ręcznie).</summary>
    private static readonly Dictionary<string, string> AlwaysExcluded = new()
    {
        ["ultimate_plan"] = "conflicts with the high-performance plan; pick it manually if you want it",
        ["fast_startup_off"] = "affects boot time, not gaming",
        ["toasts_off"] = "turns off notifications, so it is your call",
        ["onedrive_off"] = "turns off syncing, so it is your call",
        ["store_autodl"] = "changes Store behavior, so it is your call",
        ["hover_time"] = "cosmetic UI change",
        ["ink_workspace_off"] = "turns off the handwriting feature",
        ["net_connectivity_probe_off"] = "may worsen network detection",
        ["netbios_off"] = "may break network shares at home or at work",
        ["prio_sep"] = "small effect that is hard to measure",
        ["ui_priority_boost"] = "small effect that is hard to measure",
        ["bg_access_off"] = "limits background UWP apps",
    };

    public AutoTunePlan Plan(HardwareProfile hw, bool elevated, Func<string, bool>? alreadyApplied = null)
    {
        var planned = new List<PlannedTweak>();
        var skipped = new List<PlannedTweak>();
        foreach (var t in _catalog.All)
        {
            if (t.IsAction) continue;
            if (t.Kind is not (TweakKind.Reg or TweakKind.RegPerAdapter or TweakKind.PowerIdx or TweakKind.PowerPlan)) continue;
            if (t.Category is not (TweakCategory.Performance or TweakCategory.Input or TweakCategory.Network)) continue;
            if (t.Risk is not (RiskLevel.Recommended or RiskLevel.Caution)) continue;

            var reason = Exclusion(t, hw, elevated, alreadyApplied);
            if (reason is null) planned.Add(new PlannedTweak(t.Id, t.Title, Why(t, hw)));
            else skipped.Add(new PlannedTweak(t.Id, t.Title, reason));
        }
        return new AutoTunePlan(hw, planned, skipped);
    }

    private static string? Exclusion(TweakDefinition t, HardwareProfile hw, bool elevated, Func<string, bool>? applied)
    {
        if (AlwaysExcluded.TryGetValue(t.Id, out var why)) return why;
        if (applied?.Invoke(t.Id) == true) return AlreadyAppliedReason;
        if (t.RequiresAdmin && !elevated) return "requires administrator";
        if (hw.IsLaptop && LaptopOnlyExcluded.Contains(t.Id)) return "laptop: we keep battery saving and quiet operation";
        if (t.Id == "prefetch_off" && !hw.HasSsd) return "Prefetch helps on drives without an SSD";
        if (t.Id == "nopaging_exec" && hw.RamGb < 16) return "needs at least 16 GB of RAM";
        if (t.Id == "core_parking" && hw.Threads < 6) return "too few threads for this to matter";
        return null;
    }

    private static string Why(TweakDefinition t, HardwareProfile hw) => t.Category switch
    {
        TweakCategory.Input => "lower latency and more consistent input",
        TweakCategory.Network => "steadier network in games",
        _ => hw.IsLaptop ? "lower system load with no battery impact" : "less background work while gaming",
    };
}
