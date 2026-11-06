using System.Text.Json.Nodes;
using Tachyvex.Core.Models;

namespace Tachyvex.Core.Abstractions;

/// <summary>Obsługa odwracalnych zmian jednego lub kilku rodzajów (rejestr, usługi, bcdedit...).</summary>
public interface ITweakHandler
{
    IReadOnlyCollection<TweakKind> Kinds { get; }

    Task<TweakStatus> GetStatusAsync(TweakDefinition tweak, TweakContext context, CancellationToken ct);

    /// <summary>Zapisuje stan poprzedni, wprowadza zmianę i zwraca migawkę potrzebną do cofnięcia.</summary>
    Task<JsonNode?> ApplyAsync(TweakDefinition tweak, TweakContext context, CancellationToken ct);

    Task RevertAsync(TweakDefinition tweak, JsonNode? snapshot, TweakContext context, CancellationToken ct);
}

/// <summary>Akcje jednorazowe: czyszczenie, otwieranie narzędzi, polecenia diagnostyczne.</summary>
public interface IActionHandler
{
    bool CanHandle(TweakDefinition tweak);
    Task<string> RunAsync(TweakDefinition tweak, TweakContext context, CancellationToken ct);
}

public interface IElevationService
{
    bool IsElevated { get; }
}

public interface IRestorePointService
{
    Task<TweakResult> CreateAsync(string description, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
