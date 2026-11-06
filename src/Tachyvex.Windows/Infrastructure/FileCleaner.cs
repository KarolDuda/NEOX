using System.Text.RegularExpressions;

namespace Tachyvex.Windows.Infrastructure;

/// <summary>
/// Usuwa ZAWARTOŚĆ wskazanych folderów (lub pliki pasujące do wzorca). Zabezpieczenia:
/// pomija ścieżki płytsze niż 3 segmenty, nie schodzi w dowiązania/junctiony i nie rusza własnego katalogu aplikacji.
/// </summary>
public sealed class FileCleaner
{
    private readonly string _appDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');

    public string Clean(IEnumerable<string> patterns, CancellationToken ct)
    {
        long bytes = 0; int files = 0;
        foreach (var raw in patterns)
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.Contains('%') || expanded.Contains('$')) continue; // nierozwiązana zmienna: nie zgadujemy
            foreach (var match in Expand(expanded))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafe(match)) continue;
                if (Directory.Exists(match) && !IsReparse(match))
                {
                    foreach (var child in SafeEnumerate(match)) Delete(child, ref bytes, ref files, ct);
                }
                else if (File.Exists(match)) Delete(match, ref bytes, ref files, ct);
            }
        }
        return $"Usunięto {files} plików ({bytes / 1048576.0:F0} MB).";
    }

    public long Measure(IEnumerable<string> patterns)
    {
        long total = 0;
        foreach (var raw in patterns)
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.Contains('%')) continue;
            foreach (var m in Expand(expanded))
            {
                if (!IsSafe(m)) continue;
                try
                {
                    if (File.Exists(m)) total += new FileInfo(m).Length;
                    else if (Directory.Exists(m) && !IsReparse(m))
                        total += new DirectoryInfo(m).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Sum(f => f.Length);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return total;
    }

    private bool IsSafe(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        var depth = full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        if (depth < 3) return false;
        if (full.StartsWith(_appDir, StringComparison.OrdinalIgnoreCase)) return false;
        if (full.Split('\\').Any(s => s.StartsWith("_MEI", StringComparison.Ordinal))) return false;
        return true;
    }

    private static bool IsReparse(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try { return Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>Rozwija * i ? w dowolnym segmencie ścieżki.</summary>
    internal static IEnumerable<string> Expand(string pattern)
    {
        var root = Path.GetPathRoot(pattern);
        if (string.IsNullOrEmpty(root)) return Array.Empty<string>();
        var segs = pattern[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> current = new[] { root };
        foreach (var seg in segs)
        {
            if (seg.IndexOfAny(new[] { '*', '?' }) < 0) { current = current.Select(c => Path.Combine(c, seg)).ToList(); continue; }
            var next = new List<string>();
            foreach (var c in current)
            {
                try { if (Directory.Exists(c)) next.AddRange(Directory.EnumerateFileSystemEntries(c, seg)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            current = next;
        }
        return current.Where(p => Directory.Exists(p) || File.Exists(p)).ToList();
    }

    private static void Delete(string path, ref long bytes, ref int files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var attr = File.GetAttributes(path);
            if (attr.HasFlag(FileAttributes.ReparsePoint))
            {
                if (attr.HasFlag(FileAttributes.Directory)) Directory.Delete(path); else File.Delete(path); // usuwa tylko dowiązanie
                return;
            }
            if (attr.HasFlag(FileAttributes.Directory))
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path).ToList()) Delete(child, ref bytes, ref files, ct);
                try { Directory.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            else
            {
                var len = new FileInfo(path).Length;
                File.Delete(path);
                bytes += len; files++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* plik w użyciu: pomijamy */ }
    }
}
