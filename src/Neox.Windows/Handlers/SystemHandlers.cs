using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Neox.Core.Abstractions;
using Neox.Core.Models;
using Neox.Windows.Infrastructure;

namespace Neox.Windows.Handlers;

/// <summary>Usługi: zmiana typu uruchamiania. Stan poprzedni czytany z rejestru (Start + DelayedAutostart), bez tłumaczeń z wyjścia sc.</summary>
public sealed class ServiceHandler : ITweakHandler
{
    private const int Auto = 2, Demand = 3, Disabled = 4;
    private readonly IProcessRunner _run;
    private readonly RegistryService _reg;
    public ServiceHandler(IProcessRunner run, RegistryService reg) { _run = run; _reg = reg; }
    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Service };

    private static string Key(string svc) => @"SYSTEM\CurrentControlSet\Services\" + svc;

    private (int Start, int Delayed)? Read(string svc)
    {
        var s = _reg.Read("HKLM", Key(svc), "Start");
        if (s is null) return null;
        var d = _reg.Read("HKLM", Key(svc), "DelayedAutostart");
        return ((int)RegistryService.DwordToLong(s.Value.Value!), d is null ? 0 : (int)RegistryService.DwordToLong(d.Value.Value!));
    }

    public Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var cur = Read(t.Service!);
        return Task.FromResult(cur is null ? TweakStatus.Unknown : cur.Value.Start == Disabled ? TweakStatus.Applied : TweakStatus.NotApplied);
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var name = t.Service!;
        var cur = Read(name) ?? throw new TweakException($"Service {name} does not exist on this computer.");
        var r = await _run.RunAsync(new[] { "sc", "config", name, "start=", "disabled" }, null, ct);
        if (!r.Success) throw new TweakException($"Could not disable service {name}: {r.FirstLine}");
        return new JsonObject { ["service"] = name, ["start"] = cur.Start, ["delayed"] = cur.Delayed };
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        var s = snapshot!.AsObject();
        var name = s["service"]!.GetValue<string>();
        var start = s["start"]!.GetValue<int>();
        var delayed = s["delayed"]!.GetValue<int>() == 1;
        var mode = start switch { Auto => delayed ? "delayed-auto" : "auto", Demand => "demand", Disabled => "disabled", _ => "demand" };
        var r = await _run.RunAsync(new[] { "sc", "config", name, "start=", mode }, null, ct);
        if (!r.Success) throw new TweakException($"Could not restore service {name}: {r.FirstLine}");
    }
}

public sealed class TaskHandler : ITweakHandler
{
    private readonly IProcessRunner _run;
    public TaskHandler(IProcessRunner run) => _run = run;
    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Task };

    private async Task<bool?> EnabledAsync(string task, CancellationToken ct)
    {
        var r = await _run.RunAsync(new[] { "schtasks", "/query", "/tn", task, "/xml" }, null, ct);
        if (!r.Success) return null;
        var block = Regex.Match(r.Output, "<Settings>(.*?)</Settings>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var m = block.Success ? Regex.Match(block.Groups[1].Value, @"<Enabled>\s*(true|false)\s*</Enabled>", RegexOptions.IgnoreCase) : Match.Empty;
        return !m.Success || m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var states = new List<bool>();
        foreach (var task in t.Tasks!) { var e = await EnabledAsync(task, ct); if (e is not null) states.Add(e.Value); }
        return states.Count == 0 ? TweakStatus.Unknown : states.Any(x => x) ? TweakStatus.NotApplied : TweakStatus.Applied;
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var changed = new JsonArray();
        foreach (var task in t.Tasks!)
        {
            var was = await EnabledAsync(task, ct);
            if (was is null) continue; // zadanie nie istnieje w tej wersji Windows
            var r = await _run.RunAsync(new[] { "schtasks", "/change", "/tn", task, "/disable" }, null, ct);
            if (!r.Success) throw new TweakException($"Could not disable task {task}: {r.FirstLine}");
            changed.Add(new JsonObject { ["task"] = task, ["wasEnabled"] = was.Value });
        }
        if (changed.Count == 0) throw new TweakException("None of these tasks exist in this version of Windows.");
        return changed;
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        foreach (var n in snapshot!.AsArray())
        {
            if (!n!["wasEnabled"]!.GetValue<bool>()) continue;
            var r = await _run.RunAsync(new[] { "schtasks", "/change", "/tn", n["task"]!.GetValue<string>(), "/enable" }, null, ct);
            if (!r.Success) throw new TweakException("Could not enable the task: " + r.FirstLine);
        }
    }
}

public sealed partial class BcdHandler : ITweakHandler
{
    private readonly IProcessRunner _run;
    public BcdHandler(IProcessRunner run) => _run = run;
    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Bcd };

    private async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var r = await _run.RunAsync(new[] { "bcdedit", "/enum", "{current}" }, null, ct);
        if (!r.Success) throw new TweakException("bcdedit is unavailable (requires administrator): " + r.FirstLine);
        var m = Regex.Match(r.Output, @"^\s*" + Regex.Escape(key) + @"\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    public async Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var cur = await GetAsync(t.Bcd!.Key, ct);
        return string.Equals(cur, t.Bcd.Value, StringComparison.OrdinalIgnoreCase) ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var old = await GetAsync(t.Bcd!.Key, ct);
        var r = await _run.RunAsync(new[] { "bcdedit", "/set", t.Bcd.Key, t.Bcd.Value }, null, ct);
        if (!r.Success) throw new TweakException("bcdedit rejected the change: " + r.FirstLine);
        return new JsonObject { ["key"] = t.Bcd.Key, ["old"] = old };
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        var key = snapshot!["key"]!.GetValue<string>();
        var old = snapshot["old"]?.GetValue<string>();
        var r = old is null
            ? await _run.RunAsync(new[] { "bcdedit", "/deletevalue", key }, null, ct)
            : await _run.RunAsync(new[] { "bcdedit", "/set", key, old }, null, ct);
        if (!r.Success) throw new TweakException("bcdedit rejected the revert: " + r.FirstLine);
    }
}

/// <summary>Zaawansowane właściwości karty sieciowej po słowach kluczowych NDIS (niezależne od języka).</summary>
public sealed class NicHandler : ITweakHandler
{
    private readonly IProcessRunner _run;
    public NicHandler(IProcessRunner run) => _run = run;
    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Nic };

    private static string Q(string s) => s.Replace("'", "''");

    private async Task<List<(string Adapter, string Value)>> ReadAsync(string keyword, CancellationToken ct)
    {
        var script = "Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { " +
            $"Get-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword '{Q(keyword)}' -ErrorAction SilentlyContinue }} | " +
            "ForEach-Object { [pscustomobject]@{N=$_.Name;V=([string]($_.RegistryValue -join ','))} } | ConvertTo-Json -Compress";
        var r = await _run.RunAsync(ProcessRunner.PowerShell(script), TimeSpan.FromSeconds(40), ct);
        var m = Regex.Match(r.Output, @"[\[{].*[\]}]", RegexOptions.Singleline);
        if (!r.Success || !m.Success) return new();
        using var doc = JsonDocument.Parse(m.Value);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        return items.Select(e => (e.GetProperty("N").GetString() ?? "", e.GetProperty("V").GetString() ?? "")).Where(x => x.Item1.Length > 0).ToList();
    }

    private async Task SetAsync(string adapter, string keyword, string value, CancellationToken ct)
    {
        var r = await _run.RunAsync(ProcessRunner.PowerShell(
            $"Set-NetAdapterAdvancedProperty -Name '{Q(adapter)}' -RegistryKeyword '{Q(keyword)}' -RegistryValue '{Q(value)}' -ErrorAction Stop"),
            TimeSpan.FromSeconds(60), ct);
        if (!r.Success) throw new TweakException($"Adapter {adapter}: {r.FirstLine}");
    }

    public async Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        foreach (var kw in t.Nic!.Keywords)
        {
            var vals = await ReadAsync(kw, ct);
            if (vals.Count == 0 || vals.Any(v => v.Value != t.Nic.Value)) return TweakStatus.NotApplied;
        }
        return TweakStatus.Applied;
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var snap = new JsonArray();
        foreach (var kw in t.Nic!.Keywords)
            foreach (var (adapter, value) in await ReadAsync(kw, ct))
                snap.Add(new JsonObject { ["adapter"] = adapter, ["keyword"] = kw, ["old"] = value });
        if (snap.Count == 0) throw new TweakException("No active network adapter supports this setting.");

        var done = 0;
        try
        {
            foreach (var n in snap) { await SetAsync(n!["adapter"]!.GetValue<string>(), n["keyword"]!.GetValue<string>(), t.Nic.Value, ct); done++; }
        }
        catch
        {
            for (var i = 0; i < done; i++)
                try { await SetAsync(snap[i]!["adapter"]!.GetValue<string>(), snap[i]!["keyword"]!.GetValue<string>(), snap[i]!["old"]!.GetValue<string>(), CancellationToken.None); } catch { }
            throw;
        }
        return snap;
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        foreach (var n in snapshot!.AsArray())
            await SetAsync(n!["adapter"]!.GetValue<string>(), n["keyword"]!.GetValue<string>(), n["old"]!.GetValue<string>(), ct);
    }
}

/// <summary>Ustawienia zmieniane poleceniem (powercfg /h, netsh, MMAgent...). Cofanie = polecenie "off" z katalogu.</summary>
public sealed class CmdHandler : ITweakHandler
{
    private readonly IProcessRunner _run;
    private readonly RegistryService _reg;
    public CmdHandler(IProcessRunner run, RegistryService reg) { _run = run; _reg = reg; }
    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Cmd };

    public async Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var chk = t.Cmd!.Check;
        if (chk?.RegDword is { } r)
        {
            var cur = _reg.Read(r.Hive, r.Path, r.Name);
            return cur is not null && RegistryService.DwordToLong(cur.Value.Value!) == r.Expected ? TweakStatus.Applied : TweakStatus.NotApplied;
        }
        if (chk?.Ps is { } ps)
        {
            var res = await _run.RunAsync(ProcessRunner.PowerShell(ps), TimeSpan.FromSeconds(30), ct);
            if (!res.Success) return TweakStatus.Unknown;
            var isFalse = res.Output.Trim().EndsWith("false", StringComparison.OrdinalIgnoreCase);
            return chk.EqualsFalse == isFalse ? TweakStatus.Applied : TweakStatus.NotApplied;
        }
        return TweakStatus.Unknown; // brak niezawodnego odczytu: stan = czy zmiana pochodzi z NEOX
    }

    public async Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var ran = 0;
        foreach (var argv in t.Cmd!.On)
        {
            var r = await _run.RunAsync(argv, TimeSpan.FromSeconds(t.TimeoutSeconds ?? 120), ct);
            if (!r.Success)
            {
                for (var i = 0; i < ran; i++) await _run.RunAsync(t.Cmd.Off[Math.Min(i, t.Cmd.Off.Count - 1)], TimeSpan.FromSeconds(120), CancellationToken.None);
                throw new TweakException(string.IsNullOrEmpty(r.FirstLine) ? "The command failed." : r.FirstLine);
            }
            ran++;
        }
        return new JsonObject { ["appliedAt"] = DateTimeOffset.Now.ToString("O") };
    }

    public async Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        foreach (var argv in t.Cmd!.Off)
        {
            var r = await _run.RunAsync(argv, TimeSpan.FromSeconds(120), ct);
            if (!r.Success) throw new TweakException("Revert failed: " + r.FirstLine);
        }
    }
}
