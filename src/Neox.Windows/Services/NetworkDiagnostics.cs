using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Neox.Core.Models;
using Neox.Core.State;
using Neox.Windows.Infrastructure;

namespace Neox.Windows;

public sealed record PingStats(string Host, int Sent, int Received, double MinMs, double AvgMs, double MaxMs, double JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
}

public sealed record DnsProvider(string Name, string Primary, string Secondary);
public sealed record DnsBenchResult(DnsProvider Provider, double? MedianMs, int Failures);

public sealed class NetworkDiagnostics
{
    public static readonly IReadOnlyList<(string Name, string Host)> PingTargets = new[]
    {
        ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8"),
        ("AWS Frankfurt (EU)", "ec2.eu-central-1.amazonaws.com"), ("AWS Irlandia (EU)", "ec2.eu-west-1.amazonaws.com"),
        ("AWS London (EU)", "ec2.eu-west-2.amazonaws.com"), ("AWS Paris (EU)", "ec2.eu-west-3.amazonaws.com"),
        ("AWS Sztokholm (EU)", "ec2.eu-north-1.amazonaws.com"), ("AWS Wirginia (NA-East)", "ec2.us-east-1.amazonaws.com"),
    };

    public static readonly IReadOnlyList<DnsProvider> DnsProviders = new DnsProvider[]
    {
        new("Cloudflare", "1.1.1.1", "1.0.0.1"), new("Google", "8.8.8.8", "8.8.4.4"),
        new("Quad9", "9.9.9.9", "149.112.112.112"), new("OpenDNS", "208.67.222.222", "208.67.220.220"),
        new("AdGuard", "94.140.14.14", "94.140.15.15"), new("CleanBrowsing", "185.228.168.9", "185.228.169.9"),
        new("Control D", "76.76.2.0", "76.76.10.0"), new("DNS.WATCH", "84.200.69.80", "84.200.70.40"),
    };

    public static readonly string[] DnsTestDomains = { "epicgames.com", "fortnite.com", "unrealengine.com", "amazonaws.com", "akamai.net", "cloudflare.com" };

    private readonly IProcessRunner _run;
    private readonly IStateStore _store;
    public NetworkDiagnostics(IProcessRunner run, IStateStore store) { _run = run; _store = store; }

    public async Task<PingStats> PingAsync(string host, int count = 10, int timeoutMs = 2000, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var ping = new Ping();
        var times = new List<double>();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
                if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
            }
            catch (PingException) { /* host nieosiągalny: liczymy jako utratę */ }
            progress?.Report((i + 1) / (double)count);
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        if (times.Count == 0) return new PingStats(host, count, 0, 0, 0, 0, 0);
        var avg = times.Average();
        var jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average() : 0;
        return new PingStats(host, count, times.Count, times.Min(), avg, times.Max(), jitter);
    }

    /// <summary>Największy rozmiar pakietu bez fragmentacji; zwraca MTU albo null, gdy host nie odpowiada na ping.</summary>
    public async Task<int?> FindMtuAsync(string host = "1.1.1.1", CancellationToken ct = default)
    {
        async Task<bool> Ok(int payload)
        {
            using var p = new Ping();
            try
            {
                var r = await p.SendPingAsync(host, 1500, new byte[payload], new PingOptions(64, dontFragment: true)).ConfigureAwait(false);
                return r.Status == IPStatus.Success;
            }
            catch (PingException) { return false; }
        }
        if (!await Ok(32)) return null;
        if (await Ok(1472)) return 1500;
        int lo = 32, hi = 1472;
        while (hi - lo > 1) { ct.ThrowIfCancellationRequested(); var mid = (lo + hi) / 2; if (await Ok(mid)) lo = mid; else hi = mid; }
        return lo + 28;
    }

    // ---- DNS ----
    public async Task<DnsBenchResult> BenchDnsAsync(DnsProvider provider, int rounds = 2, CancellationToken ct = default)
    {
        var times = new List<double>(); var fails = 0;
        foreach (var domain in DnsTestDomains)
            for (var r = 0; r < rounds; r++)
            {
                var ms = await QueryDnsAsync(provider.Primary, domain, ct).ConfigureAwait(false);
                if (ms is null) fails++; else times.Add(ms.Value);
            }
        times.Sort();
        return new DnsBenchResult(provider, times.Count == 0 ? null : times[times.Count / 2], fails);
    }

    /// <summary>Minimalne zapytanie DNS typu A po UDP; zwraca czas odpowiedzi w ms.</summary>
    public static async Task<double?> QueryDnsAsync(string server, string domain, CancellationToken ct, int timeoutMs = 1500)
    {
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var packet = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in domain.Split('.')) { packet.Add((byte)label.Length); packet.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        packet.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        using var udp = new UdpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(packet.ToArray(), new IPEndPoint(IPAddress.Parse(server), 53), cts.Token).ConfigureAwait(false);
            var res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();
            var b = res.Buffer;
            return b.Length >= 12 && b[0] == (byte)(id >> 8) && b[1] == (byte)id && (b[3] & 0x0F) == 0 ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException) { ct.ThrowIfCancellationRequested(); return null; }
    }

    public static (string[] Servers, bool Manual) AdapterDns(string guid)
    {
        var reg = new RegistryService();
        var key = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{" + guid + "}";
        static string[] Split(object? v) => (v?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var manual = Split(reg.Read("HKLM", key, "NameServer")?.Value);
        if (manual.Length > 0) return (manual, true);
        return (Split(reg.Read("HKLM", key, "DhcpNameServer")?.Value), false);
    }

    public async Task<TweakResult> SetDnsAsync(IReadOnlyList<string> servers, CancellationToken ct = default)
    {
        if (!servers.All(s => IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork))
            return TweakResult.Fail("Invalid DNS address.");
        var done = 0;
        foreach (var a in NetworkAdapters.GetActive())
        {
            var (cur, manual) = AdapterDns(a.Guid);
            _store.State.DnsBackups.TryAdd(a.Guid, manual ? cur.ToList() : new List<string>());
            await _store.SaveAsync(ct).ConfigureAwait(false);
            var list = string.Join(",", servers.Select(s => $"'{s}'"));
            var r = await _run.RunAsync(ProcessRunner.PowerShell(
                $"Set-DnsClientServerAddress -InterfaceAlias '{a.Name.Replace("'", "''")}' -ServerAddresses ({list}) -ErrorAction Stop"), TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (r.Success) done++;
        }
        await _run.RunAsync(new[] { "ipconfig", "/flushdns" }, null, ct).ConfigureAwait(false);
        return done > 0 ? TweakResult.Ok($"DNS ustawiony na {string.Join(" / ", servers)} ({done} kart).")
                        : TweakResult.Fail("Could not set DNS (administrator rights required).");
    }

    public async Task<TweakResult> RestoreDnsAsync(CancellationToken ct = default)
    {
        if (_store.State.DnsBackups.Count == 0) return TweakResult.Fail("No saved DNS backup.");
        var done = 0;
        foreach (var a in NetworkAdapters.GetActive())
        {
            if (!_store.State.DnsBackups.TryGetValue(a.Guid, out var old)) continue;
            var alias = a.Name.Replace("'", "''");
            var script = old.Count == 0
                ? $"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ResetServerAddresses -ErrorAction Stop"
                : $"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ServerAddresses ({string.Join(",", old.Select(s => $"'{s}'"))}) -ErrorAction Stop";
            var r = await _run.RunAsync(ProcessRunner.PowerShell(script), TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (r.Success) { _store.State.DnsBackups.Remove(a.Guid); done++; }
        }
        await _store.SaveAsync(ct).ConfigureAwait(false);
        return done > 0 ? TweakResult.Ok($"Previous DNS restored ({done} adapters).") : TweakResult.Fail("Could not restore DNS.");
    }
}
