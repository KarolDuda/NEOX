using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Tachyvex.Windows.Infrastructure;

public sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
    public string FirstLine => Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> argv, TimeSpan? timeout = null, CancellationToken ct = default);
}

/// <summary>Uruchamia polecenia bez okna, z limitem czasu, bez powłoki (argumenty przekazywane osobno, bez wstrzykiwania).</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> argv, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (argv.Count == 0) throw new ArgumentException("Puste polecenie.", nameof(argv));
        var psi = new ProcessStartInfo(argv[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in argv.Skip(1)) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Win32Exception ex) { return new ProcessResult(-1, $"Nie można uruchomić {argv[0]}: {ex.Message}"); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = p.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return new ProcessResult(p.ExitCode, ((await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false))).Trim());
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-2, "Przekroczono limit czasu polecenia.");
        }
    }

    public static IReadOnlyList<string> PowerShell(string script) =>
        new[] { "powershell", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script };
}
