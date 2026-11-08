using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neox.Core.Models;

public sealed class RegValueSpec
{
    public string Hive { get; init; } = "HKCU";
    public string Path { get; init; } = "";
    public string? Name { get; init; }
    /// <summary>"dword" lub "string".</summary>
    public string Type { get; init; } = "dword";
    public JsonElement Value { get; init; }

    public long AsLong() => Value.ValueKind == JsonValueKind.Number ? Value.GetInt64() : long.Parse(Value.GetString() ?? "0");
    public string AsString() => Value.ValueKind == JsonValueKind.String ? Value.GetString() ?? "" : Value.ToString();
}

public sealed class PerAdapterSpec
{
    public string PathTemplate { get; init; } = "";
    public List<RegValueSpec> Values { get; init; } = new();
}

public sealed class PowerSettingSpec
{
    public string Subgroup { get; init; } = "";
    public string Setting { get; init; } = "";
    public int Value { get; init; }
}

public sealed class BcdSpec
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class NicSpec
{
    public List<string> Keywords { get; init; } = new();
    public string Value { get; init; } = "";
}

public sealed class RegDwordCheck
{
    public string Hive { get; init; } = "HKLM";
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public long Expected { get; init; }
}

public sealed class CmdCheck
{
    public RegDwordCheck? RegDword { get; init; }
    public string? Ps { get; init; }
    public bool EqualsFalse { get; init; }
}

public sealed class CmdSpec
{
    public List<List<string>> On { get; init; } = new();
    public List<List<string>> Off { get; init; } = new();
    public bool HasCheck { get; init; }
    public CmdCheck? Check { get; init; }
}

/// <summary>Jedna pozycja katalogu. Dane w tweaks.json; kod nie zawiera zakodowanych na sztywno ustawień.</summary>
public sealed class TweakDefinition
{
    public string Id { get; init; } = "";
    public TweakCategory Category { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Co zmienia i jaką korzyść deklaruje (opis ze starej wersji).</summary>
    public string Description { get; init; } = "";
    /// <summary>Wady / skutki uboczne.</summary>
    public string Consequences { get; init; } = "";
    public TweakKind Kind { get; init; }
    public RiskLevel Risk { get; init; }
    public bool RequiresAdmin { get; init; }
    public RestartRequirement Restart { get; init; }
    public EffectClaim ClaimedEffect { get; init; }

    // reg*
    public List<RegValueSpec>? Values { get; init; }
    public PerAdapterSpec? PerAdapter { get; init; }
    public List<RegValueSpec>? PerGame { get; init; }
    public string? Post { get; init; }
    // power
    public PowerSettingSpec? Power { get; init; }
    public string? Plan { get; init; }
    // system
    public string? Service { get; init; }
    public List<string>? Tasks { get; init; }
    public BcdSpec? Bcd { get; init; }
    public NicSpec? Nic { get; init; }
    public CmdSpec? Cmd { get; init; }
    // action
    public ActionKind? Action { get; init; }
    public string? Button { get; init; }
    public string? Confirm { get; init; }
    public bool ShowsResult { get; init; }
    public List<string>? Patterns { get; init; }
    public string? Target { get; init; }
    public List<List<string>>? Commands { get; init; }
    public string? SuccessMessage { get; init; }
    public int? TimeoutSeconds { get; init; }
    public string? Native { get; init; }

    [JsonIgnore] public bool IsAction => Kind == TweakKind.Action;
    [JsonIgnore] public bool IsReversible => Kind != TweakKind.Action;
}
