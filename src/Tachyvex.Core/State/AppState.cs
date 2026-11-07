using System.Text.Json.Nodes;
using Tachyvex.Core.Models;

namespace Tachyvex.Core.State;

public enum HistoryAction { Apply, Revert, Run, ProfileApply }

public sealed class HistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Time { get; init; }
    public string TweakId { get; init; } = "";
    public string Title { get; init; } = "";
    public HistoryAction Action { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

public sealed class Profile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool BuiltIn { get; init; }
    public List<string> TweakIds { get; set; } = new();
}

public sealed class GameProfile
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string? ProfileId { get; set; }
}

public sealed class Baseline
{
    public string Name { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public Dictionary<string, TweakStatus> Statuses { get; set; } = new();
}

public sealed class AppSettings
{
    public bool CreateRestorePointBeforeProfiles { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;
    public string? GameExePath { get; set; }
    public bool FirstRunDone { get; set; }
    public bool GameBoost { get; set; }
    /// <summary>GUID planu zasilania do przywrócenia po grze (zapisany, by przetrwać awarię).</summary>
    public string? GameBoostRestoreScheme { get; set; }
    public DateTimeOffset? LastAutoTune { get; set; }
}

/// <summary>Cały trwały stan aplikacji. Zapisywany w %LOCALAPPDATA%\Neox\state.json.</summary>
public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Migawki do cofania, klucz = id optymalizacji.</summary>
    public Dictionary<string, JsonNode?> Snapshots { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
    public List<Profile> CustomProfiles { get; set; } = new();
    public List<GameProfile> Games { get; set; } = new();
    public List<Baseline> Baselines { get; set; } = new();
    public List<string> PendingRestart { get; set; } = new();
    /// <summary>Kopie zapasowe DNS: klucz = GUID karty, wartość = ręczne serwery (pusta lista = DHCP).</summary>
    public Dictionary<string, List<string>> DnsBackups { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
