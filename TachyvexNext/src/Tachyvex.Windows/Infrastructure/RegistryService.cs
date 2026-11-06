using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Tachyvex.Windows.Infrastructure;

/// <summary>
/// Odczyt/zapis rejestru z pełną migawką: zapamiętuje, czy wartość istniała, jakiego była typu i co zawierała
/// (DWORD, QWORD, String, ExpandString, MultiString, Binary), więc cofnięcie jest wierne.
/// </summary>
public sealed class RegistryService
{
    private static RegistryKey Root(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        _ => throw new ArgumentException("Nieobsługiwany hive: " + hive)
    };

    public (object? Value, RegistryValueKind Kind)? Read(string hive, string path, string? name)
    {
        using var root = Root(hive);
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null) return null;
        var v = key.GetValue(name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return v is null ? null : (v, key.GetValueKind(name ?? ""));
    }

    public void Write(string hive, string path, string? name, RegistryValueKind kind, object value)
    {
        using var root = Root(hive);
        using var key = root.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Nie można otworzyć klucza {hive}\\{path}");
        key.SetValue(name ?? "", value, kind);
    }

    public void Delete(string hive, string path, string? name)
    {
        using var root = Root(hive);
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name ?? "", throwOnMissingValue: false);
    }

    public static object DwordFromLong(long v) => unchecked((int)(uint)v);

    public static long DwordToLong(object o) => o switch { int i => unchecked((uint)i), uint u => u, long l => l, _ => Convert.ToInt64(o) };

    /// <summary>Migawka jednej wartości do JSON.</summary>
    public JsonObject Snapshot(string hive, string path, string? name)
    {
        var o = new JsonObject { ["hive"] = hive, ["path"] = path, ["name"] = name };
        var cur = Read(hive, path, name);
        if (cur is null) { o["exists"] = false; return o; }
        o["exists"] = true;
        o["kind"] = cur.Value.Kind.ToString();
        o["data"] = cur.Value.Kind switch
        {
            RegistryValueKind.DWord => JsonValue.Create(DwordToLong(cur.Value.Value!)),
            RegistryValueKind.QWord => JsonValue.Create((long)cur.Value.Value!),
            RegistryValueKind.Binary => JsonValue.Create(Convert.ToBase64String((byte[])cur.Value.Value!)),
            RegistryValueKind.MultiString => new JsonArray(((string[])cur.Value.Value!).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            _ => JsonValue.Create(cur.Value.Value!.ToString()),
        };
        return o;
    }

    public void Restore(JsonObject snap)
    {
        var hive = snap["hive"]!.GetValue<string>();
        var path = snap["path"]!.GetValue<string>();
        var name = snap["name"]?.GetValue<string>();
        if (!snap["exists"]!.GetValue<bool>()) { Delete(hive, path, name); return; }
        var kind = Enum.Parse<RegistryValueKind>(snap["kind"]!.GetValue<string>());
        var data = snap["data"];
        object value = kind switch
        {
            RegistryValueKind.DWord => DwordFromLong(data!.GetValue<long>()),
            RegistryValueKind.QWord => data!.GetValue<long>(),
            RegistryValueKind.Binary => Convert.FromBase64String(data!.GetValue<string>()),
            RegistryValueKind.MultiString => data!.AsArray().Select(n => n!.GetValue<string>()).ToArray(),
            _ => data!.GetValue<string>(),
        };
        Write(hive, path, name, kind, value);
    }
}
