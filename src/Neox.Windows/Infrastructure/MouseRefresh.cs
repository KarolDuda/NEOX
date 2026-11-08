using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Neox.Windows.Infrastructure;

/// <summary>Stosuje ustawienia myszy z rejestru od razu, bez wylogowania.</summary>
public static class MouseRefresh
{
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x03;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] value, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);

    public static void Apply()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (k is null) return;
            int Get(string n) => int.TryParse(k.GetValue(n)?.ToString(), out var v) ? v : 0;
            SystemParametersInfo(SPI_SETMOUSE, 0, new[] { Get("MouseThreshold1"), Get("MouseThreshold2"), Get("MouseSpeed") }, SPIF_UPDATEINIFILE_SENDCHANGE);
            var sens = Get("MouseSensitivity");
            if (sens is >= 1 and <= 20)
                SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)sens, SPIF_UPDATEINIFILE_SENDCHANGE);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException) { }
    }
}
