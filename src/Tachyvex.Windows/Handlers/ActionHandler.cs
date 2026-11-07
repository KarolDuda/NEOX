using System.Diagnostics;
using System.Runtime.InteropServices;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Models;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.Windows.Handlers;

/// <summary>Akcje jednorazowe: czyszczenie folderów, otwieranie narzędzi, polecenia i funkcje natywne.</summary>
public sealed class ActionHandler : IActionHandler
{
    private readonly IProcessRunner _run;
    private readonly FileCleaner _cleaner;

    public ActionHandler(IProcessRunner run, FileCleaner cleaner) { _run = run; _cleaner = cleaner; }

    public bool CanHandle(TweakDefinition t) => t.IsAction;

    public async Task<string> RunAsync(TweakDefinition t, TweakContext c, CancellationToken ct)
    {
        switch (t.Action)
        {
            case ActionKind.Clean:
                return await Task.Run(() => _cleaner.Clean(t.Patterns!, ct), ct);
            case ActionKind.Open:
                Open(t.Target!);
                return "Otwarto.";
            case ActionKind.Info:
            {
                var r = await _run.RunAsync(ProcessRunner.PowerShell(t.Target!), TimeSpan.FromSeconds(60), ct);
                var text = r.Output.Trim();
                if (!r.Success && text.Length == 0) throw new TweakException("Polecenie nie powiodło się.");
                return text.Length == 0 ? "Brak danych." : text[..Math.Min(text.Length, 4000)];
            }
            case ActionKind.Command:
            {
                var timeout = TimeSpan.FromSeconds(t.TimeoutSeconds ?? 60);
                foreach (var argv in t.Commands!)
                {
                    var r = await _run.RunAsync(argv, timeout, ct);
                    if (!r.Success) throw new TweakException(string.IsNullOrEmpty(r.FirstLine) ? "Polecenie zakończyło się błędem." : r.FirstLine);
                }
                return t.SuccessMessage ?? "Wykonano.";
            }
            case ActionKind.Native:
                return await RunNativeAsync(t.Native!, ct);
            default:
                throw new TweakException("Nieznany rodzaj akcji.");
        }
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new TweakException("Nie udało się otworzyć: " + ex.Message, ex); }
    }

    private async Task<string> RunNativeAsync(string name, CancellationToken ct)
    {
        switch (name)
        {
            case "RestorePoint":
                return (await new RestorePointService(_run).CreateAsync("NEOX", ct)) is { Success: true } ok ? ok.Message
                    : throw new TweakException("Nie udało się: ochrona systemu może być wyłączona (lub obowiązuje limit 1 punktu na 24 h).");
            case "CleanTemp":
                return await Task.Run(() => _cleaner.Clean(new[] { "%TEMP%", @"%SystemRoot%\Temp" }, ct), ct);
            case "EmptyRecycleBin":
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x7); // bez pytań, dźwięku i paska postępu
                return "Kosz opróżniony (lub był pusty).";
            case "EnergyReport":
            {
                var path = Path.Combine(Path.GetTempPath(), "tachyvex_energy.html");
                await _run.RunAsync(new[] { "powercfg", "/energy", "/output", path, "/duration", "10" }, TimeSpan.FromSeconds(120), ct);
                if (!File.Exists(path)) throw new TweakException("Nie udało się wygenerować raportu.");
                Open(path);
                return "Raport zapisano: " + path;
            }
            case "RestartExplorer":
                await _run.RunAsync(new[] { "taskkill", "/f", "/im", "explorer.exe" }, TimeSpan.FromSeconds(15), ct);
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                return "Eksplorator zrestartowany.";
            case "StoreReset":
                Process.Start(new ProcessStartInfo("wsreset.exe") { UseShellExecute = false, CreateNoWindow = true });
                return "Uruchomiono reset pamięci podręcznej Sklepu (otworzy się okno Sklepu).";
            case "RenewIp":
            {
                var rel = await _run.RunAsync(new[] { "ipconfig", "/release" }, TimeSpan.FromSeconds(60), ct);
                if (!rel.Success) throw new TweakException(rel.FirstLine);
                var ren = await _run.RunAsync(new[] { "ipconfig", "/renew" }, TimeSpan.FromSeconds(90), ct);
                if (!ren.Success) throw new TweakException(ren.FirstLine);
                return "Adres IP odnowiony.";
            }
            case "SystemInfo":
                return SystemInfo.Describe();
            default:
                throw new TweakException("Nieznana akcja natywna: " + name);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
