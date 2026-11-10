using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CommandHub.Tiles.Links;

public enum LinkKind { Web, Folder, File, Invalid }

/// <summary>Works out what a quick link points at, and opens it without ever throwing.</summary>
public static class LinkTarget
{
    /// <summary>Trims, and turns a bare "www.example.com" into "https://www.example.com".</summary>
    public static string Normalize(string target)
    {
        var t = target?.Trim() ?? "";
        return t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + t : t;
    }

    private static bool IsWeb(string normalized) =>
        Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// What a link looks like it points at, from its text alone. Used for icons, so drawing a tile
    /// never waits on the disk (a link to an offline network share would otherwise freeze the window).
    /// </summary>
    public static LinkKind Guess(string target)
    {
        var t = Normalize(target);
        if (t.Length == 0) return LinkKind.Invalid;
        if (IsWeb(t)) return LinkKind.Web;
        bool looksLikePath = t.StartsWith(@"\\") || (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' && (t[2] == '\\' || t[2] == '/'));
        if (!looksLikePath) return LinkKind.Invalid;
        return Path.HasExtension(t.TrimEnd('\\', '/')) ? LinkKind.File : LinkKind.Folder;
    }

    /// <summary>What a link really points at, checking the disk. Only call this when the user acts on a link.</summary>
    public static LinkKind Classify(string target)
    {
        var t = Normalize(target);
        if (t.Length == 0) return LinkKind.Invalid;
        if (IsWeb(t)) return LinkKind.Web;
        try
        {
            if (Directory.Exists(t)) return LinkKind.Folder;
            if (File.Exists(t)) return LinkKind.File;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return LinkKind.Invalid;
    }

    public static bool TryOpen(string target, out string error)
    {
        error = "";
        if (Classify(target) == LinkKind.Invalid)
        {
            error = "Can't find that website, folder or file.";
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(Normalize(target)) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            error = "Couldn't open it. Check the link and try again.";
            return false;
        }
    }
}
