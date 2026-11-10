using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace CommandHub.Tiles.Weather;

public record DayForecast(DateOnly Date, int Code, double Max, double Min);

public record WeatherReport(double Temperature, double FeelsLike, int Code, double Wind, bool IsDay, IReadOnlyList<DayForecast> Days);

public record Place(string Label, double Latitude, double Longitude);

public enum Sky { Clear, Cloudy, Fog, Rain, Snow, Storm, Unknown }

/// <summary>
/// Open-Meteo (free, no account or key). Only a city name the user types, and then that place's
/// coordinates, are ever sent. Parsing never throws.
/// </summary>
public static class WeatherApi
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { ConnectCallback = ConnectAsync })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static WeatherApi() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("CommandHub/0.1");

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
        throw new HttpRequestException("Couldn't connect to the weather service.", last);
    }

    public static string GeocodeUrl(string city) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language=en&format=json";

    public static string ForecastUrl(double lat, double lon) =>
        "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("0.##", CultureInfo.InvariantCulture)
        + "&longitude=" + lon.ToString("0.##", CultureInfo.InvariantCulture)
        + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
        + "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4";

    public static async Task<Place?> FindPlaceAsync(string city, CancellationToken ct = default)
    {
        try { return ParsePlace(await Http.GetStringAsync(GeocodeUrl(city), ct)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>Returns the raw JSON too, so the tile can keep the last good report for offline starts.</summary>
    public static async Task<(WeatherReport Report, string Json)?> FetchAsync(double lat, double lon, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(ForecastUrl(lat, lon), ct);
            return ParseForecast(json) is { } r ? (r, json) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public static Place? ParsePlace(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
            foreach (var r in results.EnumerateArray())
            {
                var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                if (!r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo)) return null;
                return new Place(country.Length > 0 ? $"{name}, {country}" : name, la.GetDouble(), lo.GetDouble());
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    public static WeatherReport? ParseForecast(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("current", out var cur) || !root.TryGetProperty("daily", out var daily)) return null;

            var days = new List<DayForecast>();
            var times = daily.GetProperty("time").EnumerateArray().ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().ToList();
            var max = daily.GetProperty("temperature_2m_max").EnumerateArray().ToList();
            var min = daily.GetProperty("temperature_2m_min").EnumerateArray().ToList();
            for (int i = 0; i < times.Count && i < codes.Count && i < max.Count && i < min.Count; i++)
                if (DateOnly.TryParseExact(times[i].GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    days.Add(new DayForecast(d, codes[i].GetInt32(), max[i].GetDouble(), min[i].GetDouble()));

            return new WeatherReport(
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("apparent_temperature").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.TryGetProperty("wind_speed_10m", out var w) ? w.GetDouble() : 0,
                !cur.TryGetProperty("is_day", out var isDay) || isDay.GetInt32() == 1,
                days);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    /// <summary>WMO weather codes, as used by Open-Meteo.</summary>
    public static (Sky Sky, string Text) Describe(int code) => code switch
    {
        0 => (Sky.Clear, "Clear"),
        1 => (Sky.Clear, "Mostly clear"),
        2 => (Sky.Cloudy, "Partly cloudy"),
        3 => (Sky.Cloudy, "Overcast"),
        45 or 48 => (Sky.Fog, "Fog"),
        51 or 53 or 55 or 56 or 57 => (Sky.Rain, "Drizzle"),
        61 or 63 or 65 or 66 or 67 => (Sky.Rain, "Rain"),
        80 or 81 or 82 => (Sky.Rain, "Showers"),
        71 or 73 or 75 or 77 or 85 or 86 => (Sky.Snow, "Snow"),
        95 or 96 or 99 => (Sky.Storm, "Thunderstorm"),
        _ => (Sky.Unknown, "Unknown"),
    };
}
