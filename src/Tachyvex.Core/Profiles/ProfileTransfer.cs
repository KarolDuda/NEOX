using System.Text.Json;
using System.Text.Json.Nodes;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Models;
using Tachyvex.Core.State;

namespace Tachyvex.Core.Profiles;

public sealed record ImportResult(Profile? Profile, int Dropped, string? Error)
{
    public bool Success => Profile is not null;
}

/// <summary>Eksport i import profili jako JSON. Import traktuje plik jak niezaufane dane: waliduje wszystko i odrzuca ryzykowne pozycje.</summary>
public sealed class ProfileTransfer
{
    public const string Format = "tachyvex-profile";
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
        if (string.IsNullOrWhiteSpace(json)) return new(null, 0, "Plik jest pusty.");
        if (json.Length > MaxBytes) return new(null, 0, "Plik jest zbyt duży na profil.");
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return new(null, 0, "To nie jest poprawny plik JSON."); }
        if (root is not JsonObject o || o["format"]?.GetValue<string>() != Format) return new(null, 0, "To nie jest profil NEOX.");
        if (o["tweaks"] is not JsonArray arr) return new(null, 0, "Profil nie zawiera listy optymalizacji.");

        var name = (o["name"]?.GetValue<string>() ?? "").Trim();
        if (name.Length == 0) name = "Zaimportowany profil";
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
        if (ids.Count == 0) return new(null, dropped, "Profil nie zawiera żadnych dozwolonych optymalizacji.");
        return new(new Profile { Name = name, Description = desc.Length == 0 ? "Zaimportowany profil." : desc, TweakIds = ids }, dropped, null);
    }
}
