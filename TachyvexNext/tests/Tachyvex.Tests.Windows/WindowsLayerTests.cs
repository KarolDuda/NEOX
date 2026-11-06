using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Models;
using Tachyvex.Core.State;
using Tachyvex.Windows.Handlers;
using Tachyvex.Windows.Infrastructure;
using Xunit;

namespace Tachyvex.Tests.Windows;

/// <summary>Testy na prawdziwym Windows: dotykają tylko kluczy HKCU\Software\TachyvexTests i katalogu tymczasowego.</summary>
public sealed class WindowsLayerTests : IDisposable
{
    private const string TestKey = @"Software\TachyvexTests";
    private readonly RegistryService _reg = new();

    public WindowsLayerTests() => Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);
    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);

    [Theory]
    [InlineData(RegistryValueKind.DWord)]
    [InlineData(RegistryValueKind.String)]
    [InlineData(RegistryValueKind.ExpandString)]
    [InlineData(RegistryValueKind.MultiString)]
    [InlineData(RegistryValueKind.Binary)]
    [InlineData(RegistryValueKind.QWord)]
    public void Snapshot_and_restore_keep_every_value_kind(RegistryValueKind kind)
    {
        object original = kind switch
        {
            RegistryValueKind.DWord => unchecked((int)0xFFFFFFFE),
            RegistryValueKind.String => "tekst ąęś",
            RegistryValueKind.ExpandString => "%TEMP%\\x",
            RegistryValueKind.MultiString => new[] { "a", "b" },
            RegistryValueKind.Binary => new byte[] { 1, 2, 3 },
            _ => 1234567890123L,
        };
        _reg.Write("HKCU", TestKey, "V", kind, original);
        var snap = _reg.Snapshot("HKCU", TestKey, "V");

        _reg.Write("HKCU", TestKey, "V", RegistryValueKind.String, "zmienione");
        _reg.Restore(snap);

        var back = _reg.Read("HKCU", TestKey, "V")!.Value;
        Assert.Equal(kind, back.Kind);
        if (original is Array a) Assert.Equal(a, (Array)back.Value!);
        else Assert.Equal(original, back.Value);
    }

    [Fact]
    public void Restore_of_missing_value_deletes_it()
    {
        var snap = _reg.Snapshot("HKCU", TestKey, "Nie");
        _reg.Write("HKCU", TestKey, "Nie", RegistryValueKind.DWord, 1);
        _reg.Restore(snap);
        Assert.Null(_reg.Read("HKCU", TestKey, "Nie"));
    }

    [Fact]
    public async Task RegistryHandler_applies_verifies_and_reverts_a_catalog_style_tweak()
    {
        var tweak = new TweakDefinition
        {
            Id = "t", Kind = TweakKind.Reg, Title = "t",
            Values = new()
            {
                new RegValueSpec { Hive = "HKCU", Path = TestKey, Name = "A", Type = "dword", Value = System.Text.Json.JsonDocument.Parse("1").RootElement.Clone() },
                new RegValueSpec { Hive = "HKCU", Path = TestKey, Name = "B", Type = "string", Value = System.Text.Json.JsonDocument.Parse("\"x\"").RootElement.Clone() },
            }
        };
        _reg.Write("HKCU", TestKey, "A", RegistryValueKind.DWord, 7);
        var h = new RegistryHandler(_reg);
        Assert.Equal(TweakStatus.NotApplied, await h.GetStatusAsync(tweak, new TweakContext(), default));
        var snap = await h.ApplyAsync(tweak, new TweakContext(), default);
        Assert.Equal(TweakStatus.Applied, await h.GetStatusAsync(tweak, new TweakContext(), default));
        await h.RevertAsync(tweak, snap, new TweakContext(), default);
        Assert.Equal(7, (int)_reg.Read("HKCU", TestKey, "A")!.Value.Value!);
        Assert.Null(_reg.Read("HKCU", TestKey, "B"));
    }

    [Fact]
    public async Task Real_catalog_user_level_tweaks_roundtrip_through_the_engine()
    {
        // Wybieramy jedną pozycję HKCU bez wymagań admina i bez efektów ubocznych (Game Mode) i robimy pełny obieg.
        var catalog = TweakCatalog.LoadEmbedded();
        var store = new InMemoryStateStore();
        var engine = new TweakEngine(catalog, new ITweakHandler[] { new RegistryHandler(_reg) }, Array.Empty<IActionHandler>(),
            store, new ElevationService(), new SystemClock(), NullLogger<TweakEngine>.Instance);
        var id = "game_mode";
        var before = await engine.GetStatusAsync(id, new TweakContext());
        var r = await engine.ApplyAsync(id, new TweakContext());
        Assert.True(r.Success, r.Message);
        Assert.Equal(TweakStatus.Applied, await engine.GetStatusAsync(id, new TweakContext()));
        var back = await engine.RevertAsync(id, new TweakContext());
        Assert.True(back.Success, back.Message);
        Assert.Equal(before, await engine.GetStatusAsync(id, new TweakContext()));
    }

    [Fact]
    public void FileCleaner_deletes_contents_but_not_the_folder_and_ignores_unsafe_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "tvx-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.tmp"), "x");
        File.WriteAllText(Path.Combine(root, "sub", "b.tmp"), "yy");
        var msg = new FileCleaner().Clean(new[] { root, @"C:\", @"C:\Windows" }, default);
        Assert.True(Directory.Exists(root));
        Assert.Empty(Directory.GetFileSystemEntries(root));
        Assert.Contains("2 plików", msg);
        Assert.True(Directory.Exists(@"C:\Windows")); // zbyt płytka ścieżka: nietknięta
        Directory.Delete(root);
    }

    [Fact]
    public async Task ProcessRunner_captures_output_and_exit_code()
    {
        var r = await new ProcessRunner().RunAsync(new[] { "cmd", "/c", "echo", "hello" });
        Assert.True(r.Success);
        Assert.Contains("hello", r.Output);
        var bad = await new ProcessRunner().RunAsync(new[] { "cmd", "/c", "exit", "3" });
        Assert.Equal(3, bad.ExitCode);
    }
}
