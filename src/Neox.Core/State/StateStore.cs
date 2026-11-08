using System.Text.Json;
using Microsoft.Extensions.Logging;
using Neox.Core.Catalog;

namespace Neox.Core.State;

public interface IStateStore
{
    AppState State { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

/// <summary>Plik JSON zapisywany atomowo (plik tymczasowy + podmiana), z kopią .bak poprzedniej wersji.</summary>
public sealed class JsonStateStore : IStateStore
{
    private readonly string _path;
    private readonly ILogger<JsonStateStore> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppState State { get; private set; } = new();

    public JsonStateStore(string path, ILogger<JsonStateStore> log)
    {
        _path = path;
        _log = log;
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neox", "state.json");

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) { State = new AppState(); return; }
            try
            {
                await using var s = File.OpenRead(_path);
                State = await JsonSerializer.DeserializeAsync<AppState>(s, TweakCatalog.JsonOptions, ct) ?? new AppState();
            }
            catch (JsonException ex)
            {
                // Uszkodzony plik: zachowaj go do analizy i nie gub kopii poprzedniej.
                _log.LogError(ex, "Corrupted state file {Path}", _path);
                var broken = _path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(_path, broken, overwrite: true);
                var bak = _path + ".bak";
                if (File.Exists(bak))
                {
                    await using var s = File.OpenRead(bak);
                    State = await JsonSerializer.DeserializeAsync<AppState>(s, TweakCatalog.JsonOptions, ct) ?? new AppState();
                }
                else State = new AppState();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await using (var s = File.Create(tmp))
                await JsonSerializer.SerializeAsync(s, State, TweakCatalog.JsonOptions, ct);
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", overwrite: true);
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Magazyn w pamięci do testów.</summary>
public sealed class InMemoryStateStore : IStateStore
{
    public AppState State { get; private set; } = new();
    public int Saves { get; private set; }
    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
}
