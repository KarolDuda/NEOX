using Tachyvex.Core.Abstractions;
using Tachyvex.Core.Models;

namespace Tachyvex.Windows.Infrastructure;

public sealed class RestorePointService : IRestorePointService
{
    private readonly IProcessRunner _run;
    public RestorePointService(IProcessRunner run) => _run = run;

    public async Task<TweakResult> CreateAsync(string description, CancellationToken ct)
    {
        var safe = description.Replace("'", "''");
        var r = await _run.RunAsync(ProcessRunner.PowerShell(
            $"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop"), TimeSpan.FromSeconds(180), ct);
        return r.Success ? TweakResult.Ok("Punkt przywracania utworzony.")
                         : TweakResult.Fail(string.IsNullOrEmpty(r.FirstLine) ? "System Protection may be turned off." : r.FirstLine);
    }
}
