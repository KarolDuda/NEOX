using System.Text.Json.Nodes;
using Microsoft.Win32;
using Neox.Core.Abstractions;
using Neox.Core.Models;
using Neox.Windows.Infrastructure;

namespace Neox.Windows.Handlers;

/// <summary>reg, regPerAdapter (karty sieciowe) i regPerGame (ścieżka pliku exe wskazana przez użytkownika).</summary>
public sealed class RegistryHandler : ITweakHandler
{
    private readonly RegistryService _reg;
    public RegistryHandler(RegistryService reg) => _reg = reg;

    public IReadOnlyCollection<TweakKind> Kinds { get; } = new[] { TweakKind.Reg, TweakKind.RegPerAdapter, TweakKind.RegPerGame };

    private static IEnumerable<RegValueSpec> Resolve(TweakDefinition t, TweakContext c)
    {
        switch (t.Kind)
        {
            case TweakKind.Reg:
                return t.Values!;
            case TweakKind.RegPerAdapter:
                var adapters = NetworkAdapters.GetActive();
                if (adapters.Count == 0) throw new TweakException("No active network adapter found.");
                return adapters.SelectMany(a => t.PerAdapter!.Values.Select(v => new RegValueSpec
                {
                    Hive = v.Hive, Path = t.PerAdapter.PathTemplate.Replace("{guid}", "{" + a.Guid + "}"),
                    Name = v.Name, Type = v.Type, Value = v.Value
                })).ToList();
            case TweakKind.RegPerGame:
                var exe = c.GameExePath?.Trim().Trim('"');
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                    throw new TweakException("Choose a valid path to the game's .exe file.");
                return t.PerGame!.Select(v => new RegValueSpec { Hive = v.Hive, Path = v.Path, Name = exe, Type = v.Type, Value = v.Value }).ToList();
            default:
                throw new NotSupportedException();
        }
    }

    public Task<TweakStatus> GetStatusAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        IEnumerable<RegValueSpec> specs;
        try { specs = Resolve(t, c); } catch (TweakException) { return Task.FromResult(TweakStatus.Unknown); }
        foreach (var s in specs)
        {
            var cur = _reg.Read(s.Hive, s.Path, s.Name);
            if (cur is null) return Task.FromResult(TweakStatus.NotApplied);
            var same = s.Type == "dword" ? RegistryService.DwordToLong(cur.Value.Value!) == s.AsLong()
                                         : string.Equals(cur.Value.Value?.ToString(), s.AsString(), StringComparison.Ordinal);
            if (!same) return Task.FromResult(TweakStatus.NotApplied);
        }
        return Task.FromResult(TweakStatus.Applied);
    }

    public Task<JsonNode?> ApplyAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        var specs = Resolve(t, c).ToList();
        var snapshots = new JsonArray();
        foreach (var s in specs) snapshots.Add(_reg.Snapshot(s.Hive, s.Path, s.Name));

        var written = 0;
        try
        {
            foreach (var s in specs)
            {
                if (s.Type == "dword") _reg.Write(s.Hive, s.Path, s.Name, RegistryValueKind.DWord, RegistryService.DwordFromLong(s.AsLong()));
                else _reg.Write(s.Hive, s.Path, s.Name, RegistryValueKind.String, s.AsString());
                written++;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Wycofaj to, co już zapisano, żeby nie zostawić zmiany połowicznej.
            for (var i = 0; i < written; i++) _reg.Restore((JsonObject)snapshots[i]!);
            throw new TweakException("Registry access denied (policy or missing permissions): " + ex.Message, ex);
        }

        if (t.Post == "refreshMouse") MouseRefresh.Apply();
        return Task.FromResult<JsonNode?>(snapshots);
    }

    public Task RevertAsync(TweakDefinition t, JsonNode? snapshot, TweakContext c, CancellationToken ct)
    {
        foreach (var n in snapshot!.AsArray()) _reg.Restore((JsonObject)n!);
        if (t.Post == "refreshMouse") MouseRefresh.Apply();
        return Task.CompletedTask;
    }
}
