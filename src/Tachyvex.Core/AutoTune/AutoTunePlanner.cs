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
    public AutoTunePlanner(TweakCatalog catalog) => _catalog = catalog;

    private static readonly HashSet<string> LaptopOnlyExcluded = new()
    {
        "cpu_min", "core_parking", "cpu_epp", "pcie_aspm", "usb_suspend", "usb_reg", "power_throttle", "powerPlan", "wlan_perf", "disk_idle",
    };

    /// <summary>Pozycje zmieniające komfort lub zachowanie systemu: Auto-Tune ich nie rusza (można je włączyć ręcznie).</summary>
    private static readonly Dictionary<string, string> AlwaysExcluded = new()
    {
        ["ultimate_plan"] = "koliduje z planem wysokiej wydajności; wybierz ręcznie, jeśli chcesz",
        ["fast_startup_off"] = "dotyczy czasu startu, nie gier",
        ["toasts_off"] = "wyłącza powiadomienia, więc to Twoja decyzja",
        ["onedrive_off"] = "wyłącza synchronizację, więc to Twoja decyzja",
        ["store_autodl"] = "zmienia zachowanie Sklepu, więc to Twoja decyzja",
        ["hover_time"] = "kosmetyka interfejsu",
        ["ink_workspace_off"] = "wyłącza funkcję pisma odręcznego",
        ["net_connectivity_probe_off"] = "może pogorszyć wykrywanie sieci",
        ["netbios_off"] = "może zepsuć udziały sieciowe w domu lub firmie",
        ["prio_sep"] = "mały i trudny do zmierzenia efekt",
        ["ui_priority_boost"] = "mały i trudny do zmierzenia efekt",
        ["bg_access_off"] = "ogranicza aplikacje UWP w tle",
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
        if (applied?.Invoke(t.Id) == true) return "już zastosowane";
        if (t.RequiresAdmin && !elevated) return "wymaga administratora";
        if (hw.IsLaptop && LaptopOnlyExcluded.Contains(t.Id)) return "laptop: zachowujemy oszczędzanie baterii i kultury pracy";
        if (t.Id == "prefetch_off" && !hw.HasSsd) return "bez SSD Prefetch pomaga";
        if (t.Id == "nopaging_exec" && hw.RamGb < 16) return "wymaga co najmniej 16 GB RAM";
        if (t.Id == "core_parking" && hw.Threads < 6) return "mało wątków, więc nic to nie da";
        return null;
    }

    private static string Why(TweakDefinition t, HardwareProfile hw) => t.Category switch
    {
        TweakCategory.Input => "mniejsze opóźnienie i spójniejsze sterowanie",
        TweakCategory.Network => "stabilniejsza sieć w grach",
        _ => hw.IsLaptop ? "niższe obciążenie systemu bez wpływu na baterię" : "mniej pracy systemu w tle podczas gry",
    };
}
