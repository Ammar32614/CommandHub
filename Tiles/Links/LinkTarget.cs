using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CommandHub.Tiles.Links;

public enum LinkKind { Web, Folder, File, Invalid }

/// <summary>Works out what a quick link points at, and opens it without ever throwing.</summary>
public static class LinkTarget
{
    public static LinkKind Classify(string target)
    {
        var t = target?.Trim() ?? "";
        if (t.Length == 0) return LinkKind.Invalid;
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return LinkKind.Web;
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
            Process.Start(new ProcessStartInfo(target.Trim()) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            error = "Couldn't open it. Check the link and try again.";
            return false;
        }
    }
}
