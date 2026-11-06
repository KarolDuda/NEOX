using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Models;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.Windows.Handlers;

/// <summary>powercfg: pojedyncze ustawienia planu (powerIdx) i przełączanie planu (powerPlan). Bez zależności od języka systemu: porównujemy GUID-y.</summary>
public sealed partial class PowerHandler : ITweakHandler
{
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    // Ustawienia własnego planu tworzonego, gdy system (Modern Standby, edycje OEM) nie udostępnia planu wysokiej wydajności.
    private static readonly (string Sub, string Setting, int Value)[] CustomPlanSettings =
    {
        ("54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", 100), // min. stan CPU
        ("54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", 100), // maks. stan CPU
        ("54533251-82be-4824-96c1-47b60b740d00", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0),   // EPP
        ("54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 2),   // boost
        ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100), // parkowanie rdzeni
        ("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0),   // wyłączenie dysku
        ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0),   // USB selective suspend
        ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0),   // PCIe link state
        ("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0),   // Wi-Fi power saving
    };

    private readonly IProcessRunner _run;
    public PowerHandler(IProcessRunner run) => _run = run;

    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.PowerIdx, TweakKind.PowerPlan };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}")]
    private static partial Regex GuidRx();

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})")]
    private static partial Regex HexRx();

    public async Task<string?> ActiveSchemeAsync(CancellationToken ct)
    {
        var r = await _run.RunAsync(new[] { "powercfg", "/getactivescheme" }, null, ct);
        var m = GuidRx().Match(r.Output);
        return r.Success && m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private async Task<int?> GetAcValueAsync(string sub, string setting, CancellationToken ct)
    {
        var v = await TryQueryAsync(sub, setting, ct);
        if (v is null)
        {
            // Ustawienie bywa ukryte w planie; odkrywamy atrybut i próbujemy ponownie.
            await _run.RunAsync(new[] { "powercfg", "-attributes", sub, setting, "-ATTRIB_HIDE" }, null, ct);
            v = await TryQueryAsync(sub, setting, ct);
        }
        return v;
    }

    private async Task<int?> TryQueryAsync(string sub, string setting, CancellationToken ct)
    {
        var r = await _run.RunAsync(new[] { "powercfg", "/query", "SCHEME_CURRENT", sub, setting }, null, ct);
        if (!r.Success) return null;
        var vals = HexRx().Matches(r.Output);
        if (vals.Count < 2) return null;
        // przedostatnia wartość = AC, ostatnia = DC
        return int.Parse(vals[^2].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string TargetGuid(TweakDefinition t) => t.Plan == "ultimate" ? UltimateGuid : HighPerformanceGuid;

    public async Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        if (t.Kind == TweakKind.PowerPlan)
        {
            var active = await ActiveSchemeAsync(ct);
            return active is null ? TweakStatus.Unknown : active == TargetGuid(t) ? TweakStatus.Applied : TweakStatus.NotApplied;
        }
        var cur = await GetAcValueAsync(t.Power!.Subgroup, t.Power.Setting, ct);
        return cur is null ? TweakStatus.Unknown : cur == t.Power.Value ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var scheme = await ActiveSchemeAsync(ct) ?? throw new TweakException("Nie udało się odczytać aktywnego planu zasilania.");
        if (t.Kind == TweakKind.PowerIdx)
        {
            var p = t.Power!;
            var old = await GetAcValueAsync(p.Subgroup, p.Setting, ct)
                ?? throw new TweakException("Ten plan zasilania nie udostępnia tego ustawienia.");
            await SetAcAsync(scheme, p.Subgroup, p.Setting, p.Value, ct);
            await SetActiveAsync(scheme, ct);
            return new JsonObject { ["scheme"] = scheme, ["sub"] = p.Subgroup, ["setting"] = p.Setting, ["old"] = old };
        }

        var target = TargetGuid(t);
        string? created = null;
        if (scheme != target)
        {
            var r = await _run.RunAsync(new[] { "powercfg", "/setactive", target }, null, ct);
            if (!r.Success)
            {
                // Plan ukryty: utwórz kopię z szablonu GUID; jeśli się nie da, zbuduj własny z Zrównoważonego.
                var dup = await _run.RunAsync(new[] { "powercfg", "/duplicatescheme", target }, null, ct);
                var m = GuidRx().Match(dup.Output);
                if (dup.Success && m.Success) created = m.Value.ToLowerInvariant();
                else created = await CreateCustomPlanAsync(ct) ?? throw new TweakException("Windows nie udostępnia tego planu zasilania i nie udało się utworzyć własnego.");
                var act = await _run.RunAsync(new[] { "powercfg", "/setactive", created }, null, ct);
                if (!act.Success)
                {
                    await _run.RunAsync(new[] { "powercfg", "/delete", created }, null, ct);
                    throw new TweakException("Nie udało się włączyć planu zasilania: " + act.FirstLine);
                }
            }
        }
        return new JsonObject { ["previous"] = scheme, ["created"] = created };
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        var s = snapshot!.AsObject();
        if (t.Kind == TweakKind.PowerIdx)
        {
            var scheme = s["scheme"]!.GetValue<string>();
            await SetAcAsync(scheme, s["sub"]!.GetValue<string>(), s["setting"]!.GetValue<string>(), s["old"]!.GetValue<int>(), ct);
            var active = await ActiveSchemeAsync(ct);
            if (active == scheme) await SetActiveAsync(scheme, ct);
            return;
        }
        var prev = s["previous"]!.GetValue<string>();
        var r = await _run.RunAsync(new[] { "powercfg", "/setactive", prev }, null, ct);
        if (!r.Success) throw new TweakException("Nie udało się przywrócić poprzedniego planu zasilania: " + r.FirstLine);
        var created = s["created"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(created)) await _run.RunAsync(new[] { "powercfg", "/delete", created }, null, ct);
    }

    private async Task SetAcAsync(string scheme, string sub, string setting, int value, CancellationToken ct)
    {
        var r = await _run.RunAsync(new[] { "powercfg", "/setacvalueindex", scheme, sub, setting, value.ToString(CultureInfo.InvariantCulture) }, null, ct);
        if (!r.Success) throw new TweakException(string.IsNullOrEmpty(r.FirstLine) ? "powercfg: błąd ustawienia" : r.FirstLine);
    }

    private Task SetActiveAsync(string scheme, CancellationToken ct) =>
        _run.RunAsync(new[] { "powercfg", "/setactive", scheme }, null, ct);

    private async Task<string?> CreateCustomPlanAsync(CancellationToken ct)
    {
        var dup = await _run.RunAsync(new[] { "powercfg", "/duplicatescheme", BalancedGuid }, null, ct);
        var m = GuidRx().Match(dup.Output);
        if (!dup.Success || !m.Success) return null;
        var g = m.Value.ToLowerInvariant();
        await _run.RunAsync(new[] { "powercfg", "/changename", g, "TACHYVEX Performance", "Plan wydajności utworzony przez TACHYVEX" }, null, ct);
        foreach (var (sub, setting, value) in CustomPlanSettings)
            await _run.RunAsync(new[] { "powercfg", "/setacvalueindex", g, sub, setting, value.ToString(CultureInfo.InvariantCulture) }, null, ct);
        return g;
    }
}
