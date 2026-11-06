using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tachyvex.Core.Models;

namespace Tachyvex.Core.Catalog;

public sealed class TweakCatalog
{
    private const string ResourceName = "Tachyvex.Core.Catalog.tweaks.json";
    private readonly Dictionary<string, TweakDefinition> _byId;

    public IReadOnlyList<TweakDefinition> All { get; }

    private TweakCatalog(List<TweakDefinition> items)
    {
        All = items;
        _byId = items.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static TweakCatalog LoadEmbedded()
    {
        using var s = typeof(TweakCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Brak zasobu katalogu: " + ResourceName);
        return Load(s);
    }

    public static TweakCatalog Load(Stream stream)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("Pusty katalog.");
        if (file.SchemaVersion != 1) throw new InvalidDataException($"Nieobsługiwana wersja katalogu: {file.SchemaVersion}");
        return new TweakCatalog(file.Tweaks);
    }

    public TweakDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    public TweakDefinition Get(string id) => Find(id) ?? throw new KeyNotFoundException("Nieznana optymalizacja: " + id);

    public IEnumerable<TweakDefinition> Search(string? text, TweakCategory? category = null, RiskLevel? risk = null)
    {
        var q = (text ?? "").Trim();
        return All.Where(t =>
            (category is null || t.Category == category) &&
            (risk is null || t.Risk == risk) &&
            (q.Length == 0 ||
             t.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
             t.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
             t.Id.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class CatalogFile
    {
        public int SchemaVersion { get; init; }
        public List<TweakDefinition> Tweaks { get; init; } = new();
    }
}
