using System.Diagnostics;
using System.Security.Principal;
using Neox.Core.Abstractions;

namespace Neox.Windows.Infrastructure;

public sealed class ElevationService : IElevationService
{
    public bool IsElevated { get; } = Check();

    private static bool Check()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Uruchamia aplikację ponownie z podniesionymi uprawnieniami (UAC). Zwraca false, gdy użytkownik odmówi.</summary>
    public static bool RestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path not available.");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // anulowano UAC
    }
}
