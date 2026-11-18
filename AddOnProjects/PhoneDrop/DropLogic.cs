using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CommandHub.AddOn.PhoneDrop;

/// <summary>A parsed HTTP request line and headers.</summary>
public sealed record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers)
{
    public long ContentLength => Headers.TryGetValue("content-length", out var v) && long.TryParse(v, out var n) && n >= 0 ? n : -1;
}

/// <summary>The rules behind Phone drop that don't touch the network. Pure, so they're tested.</summary>
public static class DropLogic
{
    /// <summary>A random URL-safe key for pairing (128 bits).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string NewPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");

    public static bool SameSecret(string? a, string? b) =>
        a is not null && b is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>Only devices on your own network may connect: private, link-local, Tailscale (100.64/10) and loopback addresses.</summary>
    public static bool IsLocal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A file name that's safe to create here: no folders, no forbidden characters, not a device name.</summary>
    public static string SafeName(string? name)
    {
        // Only the last part of any path the phone sends (Path.GetFileName would treat "a:" as a drive).
        var n = (name ?? "").Replace('\\', '/').Split('/').Last().Trim().TrimEnd('.', ' ');
        // Invisible formatting characters (like right-to-left override) can disguise "fdp.exe" as "exe.pdf": drop them.
        n = new string(n.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
        n = new string(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c < 32 ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (n.Length == 0 || n.All(c => c == '.' || c == '_')) n = $"file-{DateTime.Now:yyyyMMdd-HHmmss}";
        var stem = Path.GetFileNameWithoutExtension(n);
        if (Reserved.Contains(stem)) n = "_" + n;
        if (n.Length > 150)
        {
            var ext = Path.GetExtension(n);
            if (ext.Length > 20) ext = ""; // a "name" that's all extension: just cut it
            n = n[..(150 - ext.Length)].TrimEnd('.', ' ') + ext;
        }
        return n;
    }

    /// <summary>
    /// "photo.jpg", then "photo (2).jpg"… so nothing already there is overwritten. <paramref name="taken"/> says which
    /// paths other uploads in progress have claimed.
    /// </summary>
    public static string UniquePath(string folder, string safeName, Func<string, bool>? taken = null)
    {
        taken ??= _ => false;
        var path = Path.Combine(folder, safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        for (int i = 2; File.Exists(path) || File.Exists(path + ".part") || taken(path) || taken(path + ".part"); i++)
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    /// <summary>
    /// A request must be addressed to this PC by one of its own addresses (or localhost) on our port. That stops
    /// "DNS rebinding", where a website you visit points its own name at this PC to reach it through your browser.
    /// </summary>
    public static bool HostIsUs(string? host, int port, IEnumerable<IPAddress> ours)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().ToLowerInvariant();
        string name = h;
        int colon = h.LastIndexOf(':');
        if (colon > 0 && !h.EndsWith(']'))
        {
            if (!int.TryParse(h[(colon + 1)..], out var p) || p != port) return false;
            name = h[..colon];
        }
        else return false; // browsers always send the port for a non-default one
        if (name is "localhost" or "127.0.0.1") return true;
        return IPAddress.TryParse(name, out var ip) && ours.Any(o => o.Equals(ip));
    }

    /// <summary>Parses "GET /path?a=1&amp;b=2 HTTP/1.1" and the header lines. Null if it isn't a request.</summary>
    public static Request? Parse(string head)
    {
        var lines = head.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
        var target = first[1];
        int q = target.IndexOf('?');
        var path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q >= 0)
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int e = pair.IndexOf('=');
                var k = Uri.UnescapeDataString((e < 0 ? pair : pair[..e]).Replace('+', ' '));
                var v = e < 0 ? "" : Uri.UnescapeDataString(pair[(e + 1)..].Replace('+', ' '));
                query[k] = v;
            }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int c = line.IndexOf(':');
            if (c > 0) headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }
        return new Request(first[0].ToUpperInvariant(), path, query, headers);
    }

    /// <summary>"3.4 MB", "820 KB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    /// <summary>The addresses your phone can reach this PC at: Wi-Fi and Ethernet first, then the rest (like Tailscale).</summary>
    public static List<(string Name, IPAddress Address)> Addresses()
    {
        var list = new List<(string, IPAddress, int)>();
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || !IsLocal(a.Address) || IPAddress.IsLoopback(a.Address)) continue;
                    if (a.Address.GetAddressBytes() is [169, 254, ..]) continue;
                    int rank = nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? 0
                        : hasGateway ? 1 : 2;
                    list.Add((nic.Name, a.Address, rank));
                }
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException) { }
        return list.OrderBy(x => x.Item3).Select(x => (x.Item1, x.Item2)).ToList();
    }
}
