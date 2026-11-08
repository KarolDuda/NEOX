using System.Text.Json;
using System.Text.Json.Nodes;
using Neox.Core.Catalog;
using Neox.Core.Models;
using Neox.Core.State;

namespace Neox.Core.Profiles;

public sealed record ImportResult(Profile? Profile, int Dropped, string? Error)
{
    public bool Success => Profile is not null;
}

/// <summary>Eksport i import profili jako JSON. Import traktuje plik jak niezaufane dane: waliduje wszystko i odrzuca ryzykowne pozycje.</summary>
public sealed class ProfileTransfer
{
    public const string Format = "neox-profile";
    public const int MaxBytes = 64 * 1024;
    private readonly TweakCatalog _catalog;
    public ProfileTransfer(TweakCatalog catalog) => _catalog = catalog;

    public string Export(Profile p)
    {
        var o = new JsonObject
        {
            ["format"] = Format,
            ["version"] = 1,
            ["name"] = p.Name,
            ["description"] = p.Description,
            ["tweaks"] = new JsonArray(p.TweakIds.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public ImportResult Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, 0, "The file is empty.");
        if (json.Length > MaxBytes) return new(null, 0, "The file is too large to be a profile.");
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return new(null, 0, "This is not a valid JSON file."); }
        if (root is not JsonObject o || o["format"]?.GetValue<string>() != Format) return new(null, 0, "This is not a NEOX profile.");
        if (o["tweaks"] is not JsonArray arr) return new(null, 0, "The profile has no optimization list.");

        var name = (o["name"]?.GetValue<string>() ?? "").Trim();
        if (name.Length == 0) name = "Imported profile";
        if (name.Length > 60) name = name[..60];
        var desc = (o["description"]?.GetValue<string>() ?? "").Trim();
        if (desc.Length > 300) desc = desc[..300];

        var ids = new List<string>();
        var dropped = 0;
        foreach (var n in arr)
        {
            string? id;
            try { id = n?.GetValue<string>(); } catch (InvalidOperationException) { dropped++; continue; }
            var t = id is null ? null : _catalog.Find(id);
            var allowed = t is not null && !t.IsAction
                && t.Kind is TweakKind.Reg or TweakKind.RegPerAdapter or TweakKind.PowerIdx or TweakKind.PowerPlan
                && t.Risk is RiskLevel.Recommended or RiskLevel.Caution;
            if (allowed && !ids.Contains(id!)) ids.Add(id!); else dropped++;
        }
        if (ids.Count == 0) return new(null, dropped, "The profile contains no allowed optimizations.");
        return new(new Profile { Name = name, Description = desc.Length == 0 ? "Imported profile." : desc, TweakIds = ids }, dropped, null);
    }
}
