using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CommandHub.AddOns;

namespace CommandHub.AddOn.PhoneDrop;

/// <summary>Saved settings (phonedrop.json in the add-on's data folder).</summary>
public sealed class DropSettings
{
    public bool Enabled { get; set; }
    public string Folder { get; set; } = "";
    public string Key { get; set; } = "";
    public int Port { get; set; } = 48123;
    public List<Received> History { get; set; } = [];
}

public sealed class Received
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime At { get; set; }
}

/// <summary>A file waiting on this PC for the phone to download.</summary>
public sealed record Outgoing(string Id, string Path, string Name, long Size);

/// <summary>A file arriving from the phone right now.</summary>
public sealed class Transfer
{
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public long Done;
    public DateTime Started { get; } = DateTime.UtcNow;
}

/// <summary>
/// The Wi-Fi server. A plain TCP listener speaking just enough HTTP for the phone page, so it needs no admin rights.
/// Only devices on your own network may connect, requests must be addressed to this PC, and everything except the
/// page and pairing needs the key.
/// </summary>
public sealed class DropService(IAddOnHost host)
{
    public const string ClientHeader = "x-phone-drop";
    private static readonly TimeSpan PinLife = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private const int MaxConnections = 24;
    private const int MaxWrongPins = 20;

    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<Outgoing> _outgoing = [];
    private readonly List<Transfer> _incoming = [];
    private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connections = new(MaxConnections);
    private readonly SemaphoreSlim _pairing = new(1, 1);
    private int _wrongPins;
    private DateTime _pinIssued = DateTime.UtcNow;
    private string _pin = DropLogic.NewPin();

    public DropSettings Settings { get; private set; } = new();
    public int Port { get; private set; }
    public bool Running => _listener is not null;
    public string Error { get; private set; } = "";
    /// <summary>Too many wrong codes: typing a code is off until "New pairing code" (scanning the QR still works).</summary>
    public bool PinLocked { get; private set; }

    /// <summary>The 6-digit code; a fresh one every 10 minutes.</summary>
    public string Pin
    {
        get
        {
            if (DateTime.UtcNow - _pinIssued > PinLife) { _pin = DropLogic.NewPin(); _pinIssued = DateTime.UtcNow; }
            return _pin;
        }
    }

    /// <summary>Something changed (a file arrived, progress moved, the server started). Raised on a background thread.</summary>
    public event Action? Changed;
    private void Raise() => Changed?.Invoke();

    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Phone drop");

    public void Load()
    {
        Settings = host.Load<DropSettings>("phonedrop");
        Settings.History ??= [];
        if (string.IsNullOrWhiteSpace(Settings.Folder)) Settings.Folder = DefaultFolder();
        if (string.IsNullOrWhiteSpace(Settings.Key) || Settings.Key.Length < 16) { Settings.Key = DropLogic.NewKey(); Save(); }
        if (Settings.Port is < 1024 or > 65000) Settings.Port = 48123;
    }

    public void Save()
    {
        lock (_gate)
        {
            try { host.Save("phonedrop", Settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public (List<Outgoing> Outgoing, List<Transfer> Incoming, List<Received> History) Snapshot()
    {
        lock (_gate) return ([.. _outgoing], [.. _incoming], [.. Settings.History]);
    }

    // ---------------- Control ----------------

    public bool Start()
    {
        if (Running) return true;
        Error = "";
        CleanLeftovers();
        for (int port = Settings.Port; port < Settings.Port + 20; port++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                _listener = l;
                Port = port;
                _cts = new CancellationTokenSource();
                _ = AcceptLoop(l, _cts.Token);
                Raise();
                return true;
            }
            catch (SocketException) { } // in use: try the next one
        }
        Error = "Couldn't open a port for your phone to connect to.";
        Raise();
        return false;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
        _listener = null;
        Raise();
    }

    public void NewPairing()
    {
        Settings.Key = DropLogic.NewKey(); // phones paired before have to pair again
        _pin = DropLogic.NewPin();
        _pinIssued = DateTime.UtcNow;
        _wrongPins = 0;
        PinLocked = false;
        Save();
        Raise();
    }

    public void AddOutgoing(IEnumerable<string> paths)
    {
        lock (_gate)
            foreach (var p in paths)
            {
                if (!File.Exists(p) || _outgoing.Any(o => o.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
                _outgoing.Add(new Outgoing(Guid.NewGuid().ToString("N")[..12], p, Path.GetFileName(p), new FileInfo(p).Length));
            }
        Raise();
    }

    public void RemoveOutgoing(string id)
    {
        lock (_gate) _outgoing.RemoveAll(o => o.Id == id);
        Raise();
    }

    public void ClearHistory()
    {
        lock (_gate) Settings.History.Clear();
        Save();
        Raise();
    }

    /// <summary>Half-received files from a crash or power cut: gone after an hour.</summary>
    private void CleanLeftovers()
    {
        try
        {
            if (!Directory.Exists(Settings.Folder)) return;
            foreach (var f in Directory.EnumerateFiles(Settings.Folder, "*.part"))
            {
                bool active;
                lock (_gate) active = _claimed.Contains(f);
                if (!active && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-1)) File.Delete(f);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------------- HTTP ----------------

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { break; }
            if (!_connections.Wait(0)) { client.Dispose(); continue; } // too many at once: refuse, don't queue
            _ = Task.Run(async () =>
            {
                try { await Handle(client, ct); }
                finally { _connections.Release(); }
            }, CancellationToken.None);
        }
    }

    private async Task Handle(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            NetworkStream? stream = null;
            try
            {
                client.NoDelay = true;
                stream = client.GetStream();
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (remote is null || !DropLogic.IsLocal(remote)) { await Send(stream, 403, "text/plain", "Only devices on your own network can use Phone drop."); return; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var head = await ReadHead(stream, timeout.Token);
                if (head is null || DropLogic.Parse(head) is not { } req) { await Send(stream, 400, "text/plain", "Bad request"); return; }
                if (!DropLogic.HostIsUs(req.Headers.GetValueOrDefault("host"), Port, DropLogic.Addresses().Select(a => a.Address)))
                {
                    await Send(stream, 421, "text/plain", "Open Phone drop by this PC's address.");
                    return;
                }
                await Route(client, stream, req, ct);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The phone went away (screen locked, Wi-Fi dropped, stalled too long): nothing to tell anyone.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Anything else (a folder that's read-only or blocked by Windows security, say): tell the phone.
                if (stream is not null)
                {
                    try { await Json(stream, 500, new { error = ex is UnauthorizedAccessException ? "Windows didn't allow saving to the Phone drop folder." : ex.Message }); }
                    catch (Exception sendEx) when (sendEx is IOException or SocketException or ObjectDisposedException) { }
                }
            }
        }
    }

    /// <summary>Reads up to the blank line after the headers, one byte at a time so nothing of the body is consumed.</summary>
    private static async Task<string?> ReadHead(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        int n = 0;
        var one = new byte[1];
        while (n < buffer.Length)
        {
            if (await stream.ReadAsync(one, ct) == 0) return null;
            buffer[n++] = one[0];
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                return Encoding.ASCII.GetString(buffer, 0, n - 4);
        }
        return null;
    }

    /// <summary>A read that gives up if the other side sends nothing for 30 seconds.</summary>
    private static async Task<int> ReadIdle(NetworkStream s, Memory<byte> buffer, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(IdleTimeout);
        return await s.ReadAsync(buffer, idle.Token);
    }

    private bool Authorised(Request r)
    {
        var key = r.Headers.GetValueOrDefault("x-key") ?? r.Query.GetValueOrDefault("k") ?? CookieKey(r);
        return DropLogic.SameSecret(key, Settings.Key);
    }

    private static string? CookieKey(Request r)
    {
        foreach (var part in (r.Headers.GetValueOrDefault("cookie") ?? "").Split(';'))
        {
            var kv = part.Trim();
            if (kv.StartsWith("pd=", StringComparison.Ordinal)) return kv[3..];
        }
        return null;
    }

    private async Task Route(TcpClient client, NetworkStream s, Request r, CancellationToken ct)
    {
        switch (r.Method, r.Path)
        {
            case ("GET", "/" or "/index.html"):
                await Send(s, 200, "text/html; charset=utf-8", PhonePage.Html(Environment.MachineName));
                return;
            case ("GET", "/icon.svg"):
                await Send(s, 200, "image/svg+xml", PhonePage.Icon);
                return;
            case ("POST", "/api/pair"):
                await Pair(client, s, r, ct);
                return;
        }
        if (!Authorised(r)) { await Refuse(client, s, r, 401, new { error = "pair" }); return; }
        if (r.Method == "GET" && r.Path == "/api/state")
        {
            var (outgoing, _, _) = Snapshot();
            await Json(s, 200, new { pc = Environment.MachineName, folder = Path.GetFileName(Settings.Folder), files = outgoing.Select(o => new { id = o.Id, name = o.Name, size = o.Size }) });
        }
        else if (r.Method == "POST" && r.Path == "/api/upload") await Upload(client, s, r, ct);
        else if (r.Method == "GET" && r.Path.StartsWith("/api/file/", StringComparison.Ordinal)) await Download(s, r.Path["/api/file/".Length..], ct);
        else await Refuse(client, s, r, 404, new { error = "Not found" });
    }

    private async Task Pair(TcpClient client, NetworkStream s, Request r, CancellationToken ct)
    {
        // Only the Phone drop page sends this header; a web page elsewhere can't without asking first (and is refused).
        if (!r.Headers.ContainsKey(ClientHeader) || r.ContentLength is < 0 or > 64) { await Refuse(client, s, r, 400, new { error = "bad" }); return; }
        var body = new byte[r.ContentLength];
        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            idle.CancelAfter(TimeSpan.FromSeconds(10));
            await s.ReadExactlyAsync(body, idle.Token);
        }
        var pin = new string(Encoding.UTF8.GetString(body).Where(char.IsDigit).ToArray());

        // One guess at a time across every connection, about one a second: guessing can't be sped up by opening more.
        await _pairing.WaitAsync(ct);
        try
        {
            if (PinLocked) { await Json(s, 423, new { error = "locked" }); return; }
            if (DropLogic.SameSecret(pin, Pin))
            {
                _wrongPins = 0;
                await Json(s, 200, new { key = Settings.Key });
                return;
            }
            if (++_wrongPins >= MaxWrongPins) { PinLocked = true; Raise(); } // someone's guessing: stop until you say so
            await Task.Delay(1000, ct);
            await Json(s, 403, new { error = "pin" });
        }
        finally { _pairing.Release(); }
    }

    private async Task Upload(TcpClient client, NetworkStream s, Request r, CancellationToken ct)
    {
        long length = r.ContentLength;
        if (length < 0) { await Refuse(client, s, r, 411, new { error = "length" }); return; }
        if (!r.Headers.ContainsKey(ClientHeader)) { await Refuse(client, s, r, 400, new { error = "bad" }); return; }
        var folder = Settings.Folder;
        Directory.CreateDirectory(folder);
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (root is not null)
            {
                long arriving;
                lock (_gate) arriving = _incoming.Sum(t => t.Size - Interlocked.Read(ref t.Done));
                long free = new DriveInfo(root).AvailableFreeSpace - 50_000_000 - arriving;
                if (length > free) { await Refuse(client, s, r, 507, new { error = "This PC is out of space." }); return; }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }

        var name = DropLogic.SafeName(r.Query.GetValueOrDefault("name"));
        string final, part;
        var transfer = new Transfer { Name = name, Size = length };
        lock (_gate)
        {
            final = DropLogic.UniquePath(folder, name, _claimed.Contains);
            part = final + ".part";
            _claimed.Add(final);
            _claimed.Add(part);
            _incoming.Add(transfer);
        }
        Raise();
        bool ok = false;
        try
        {
            if ((r.Headers.GetValueOrDefault("expect") ?? "").Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                await s.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), ct);
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                long left = length;
                var lastRaise = DateTime.UtcNow;
                while (left > 0)
                {
                    int n = await ReadIdle(s, buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                    if (n == 0) throw new IOException("The phone stopped sending.");
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    left -= n;
                    Interlocked.Add(ref transfer.Done, n);
                    if (DateTime.UtcNow - lastRaise > TimeSpan.FromMilliseconds(250)) { lastRaise = DateTime.UtcNow; Raise(); }
                }
            }
            File.Move(part, final);
            ok = true;
            MarkFromNetwork(final);
            lock (_gate)
            {
                Settings.History.Insert(0, new Received { Name = Path.GetFileName(final), Path = final, Size = length, At = DateTime.Now });
                if (Settings.History.Count > 200) Settings.History.RemoveRange(200, Settings.History.Count - 200);
            }
            Save();
            await Json(s, 200, new { ok = true, name = Path.GetFileName(final) });
        }
        finally
        {
            if (!ok) { try { File.Delete(part); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
            lock (_gate)
            {
                _incoming.Remove(transfer);
                _claimed.Remove(final);
                _claimed.Remove(part);
            }
            Raise();
        }
    }

    /// <summary>
    /// Marks a received file as coming from another computer (the "Mark of the Web"), so Windows asks before running
    /// anything that arrived, just as it does for downloads.
    /// </summary>
    private static void MarkFromNetwork(string path)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { } // not NTFS (a USB stick, say)
    }

    private async Task Download(NetworkStream s, string id, CancellationToken ct)
    {
        Outgoing? o;
        lock (_gate) o = _outgoing.FirstOrDefault(x => x.Id == id);
        if (o is null || !File.Exists(o.Path)) { await Send(s, 404, "text/plain", "That file isn't shared any more."); return; }
        await using var file = new FileStream(o.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, useAsync: true);
        var ascii = new string(o.Name.Select(c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
        var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {file.Length}\r\n" +
                   $"Content-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(o.Name)}\r\n" +
                   "X-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await file.CopyToAsync(s, 1 << 20, ct);
    }

    /// <summary>
    /// An error before the body was read: answer, then read and discard what the phone is still sending (briefly), so
    /// the connection closes cleanly and the phone sees the answer instead of "connection lost".
    /// </summary>
    private static async Task Refuse(TcpClient client, NetworkStream s, Request r, int code, object body)
    {
        await Json(s, code, body);
        if (r.ContentLength <= 0) return;
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buffer = new byte[64 * 1024];
            long left = Math.Min(r.ContentLength, 256L * 1024 * 1024);
            while (left > 0)
            {
                int n = await s.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), limit.Token);
                if (n == 0) break;
                left -= n;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    private static Task Json(NetworkStream s, int code, object body) =>
        Send(s, code, "application/json; charset=utf-8", JsonSerializer.Serialize(body));

    private static async Task Send(NetworkStream s, int code, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = code switch
        {
            200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 411 => "Length Required",
            421 => "Misdirected Request", 423 => "Locked", 500 => "Internal Server Error", 507 => "Insufficient Storage", _ => "Error",
        };
        var head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\n" +
                   "X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(bytes);
    }
}
