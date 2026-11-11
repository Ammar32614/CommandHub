using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace CommandHub.Services;

/// <summary>The one HTTP client Command hub (and its add-ons) use to go online.</summary>
public static class Net
{
    public static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CommandHub/1.2 (+https://github.com/Ammar32614/CommandHub)");
        return http;
    }

    /// <summary>IPv4 addresses first, IPv6 after, as a fallback.</summary>
    public static IPAddress[] ConnectOrder(IEnumerable<IPAddress> addresses) =>
        addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();

    /// <summary>
    /// .NET tries addresses strictly in DNS order. On networks where IPv6 is listed first but doesn't really
    /// work, that stalls for ~20 s before falling back. Try IPv4 first, each address for at most 4 s.
    /// </summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = ConnectOrder(await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct));
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, ctx.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException($"Couldn't connect to {ctx.DnsEndPoint.Host}.", last);
    }
}
