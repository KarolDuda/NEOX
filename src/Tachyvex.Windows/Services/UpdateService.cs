using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Tachyvex.Windows;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size, string Notes);

/// <summary>
/// Aktualizacje z GitHub Releases (REST, bez tokenu): wymaga publicznego repozytorium.
/// Instalator jest pobierany po HTTPS wyłącznie z github.com i uruchamiany w trybie cichym.
/// </summary>
public sealed class UpdateService
{
    public const string Owner = "KarolDuda";
    public const string Repo = "tachyvax-nextgeneration";
    public const string AssetName = "TACHYVEX_Setup.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tachyvex-Updater", "2"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(2, 0, 0);
            return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        }
    }

    public async Task<(UpdateInfo? Update, string Message)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "Nie można sprawdzić aktualizacji: repozytorium jest prywatne lub nie ma jeszcze wydania.");
            if (!resp.IsSuccessStatusCode)
                return (null, $"Serwer aktualizacji odpowiedział błędem {(int)resp.StatusCode}.");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return (null, "Nieznany format wersji wydania: " + tag);

            string? url = null; long size = 0;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                { url = a.GetProperty("browser_download_url").GetString(); size = a.GetProperty("size").GetInt64(); }
            if (url is null) return (null, "Wydanie nie zawiera pliku instalatora.");
            if (!IsTrustedUrl(url)) return (null, "Adres pobierania nie pochodzi z github.com: pominięto.");

            var notes = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            return latest > Current
                ? (new UpdateInfo(latest, tag, url, size, notes), $"Dostępna wersja {latest} (masz {Current}).")
                : (null, $"Masz najnowszą wersję ({Current}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (null, "Brak połączenia z serwerem aktualizacji.");
        }
    }

    private static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TACHYVEX_Setup_{info.Version}.exe");
        using var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? info.Size;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0) progress?.Report(done / (double)total);
            }
        }
        if (info.Size > 0 && new FileInfo(path).Length != info.Size)
        {
            File.Delete(path);
            throw new IOException("Pobrany plik ma inny rozmiar niż w wydaniu; pobieranie przerwano.");
        }
        return path;
    }

    /// <summary>Uruchamia instalator po cichu (pojawi się okno UAC). False, gdy użytkownik odmówił.</summary>
    public bool LaunchInstaller(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
