using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CommandHub.AddOns;

/// <summary>An add-on on disk, ready to load.</summary>
public sealed record InstalledAddOn(AddOnManifest Manifest, string Folder, string EntryPath);

/// <summary>
/// Add-on files under one root folder:
/// <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/</c> holds each installed version, <c>&lt;id&gt;/current.txt</c> names the one to load,
/// and <c>&lt;id&gt;/removed</c> marks one the user removed. Loaded DLLs can't be deleted, so old versions and removed
/// add-ons are cleaned up at the next start, before anything loads.
/// </summary>
public sealed class AddOnFiles(string root)
{
    public string Root { get; } = root;

    public static string DefaultRoot(string dataFile) => Path.Combine(Path.GetDirectoryName(dataFile)!, "addons");

    private string Folder(string id) => Path.Combine(Root, id);
    private string CurrentFile(string id) => Path.Combine(Folder(id), "current.txt");
    private string RemovedFile(string id) => Path.Combine(Folder(id), "removed");

    public bool IsPendingRemoval(string id) => File.Exists(RemovedFile(id));

    /// <summary>Installed add-ons that aren't marked removed and whose files look whole.</summary>
    public List<InstalledAddOn> Installed()
    {
        var list = new List<InstalledAddOn>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var id = Path.GetFileName(dir);
            if (!AddOnCatalog.IsValidId(id) || IsPendingRemoval(id)) continue;
            try
            {
                if (!File.Exists(CurrentFile(id))) continue;
                var version = File.ReadAllText(CurrentFile(id)).Trim();
                var folder = Path.Combine(dir, version);
                var manifestPath = Path.Combine(folder, "manifest.json");
                if (!System.Version.TryParse(version, out _) || !File.Exists(manifestPath)) continue;
                var manifest = AddOnCatalog.ParseManifest(File.ReadAllText(manifestPath));
                if (manifest is null || manifest.Id != id || manifest.Version != version) continue;
                var entry = Path.Combine(folder, manifest.Entry);
                if (File.Exists(entry)) list.Add(new InstalledAddOn(manifest, folder, entry));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Checks the download against the catalogue, unpacks it and makes it the current version. Throws <see cref="AddOnException"/>.</summary>
    public InstalledAddOn Install(CatalogEntry entry, byte[] zip)
    {
        if (!AddOnCatalog.IsValidId(entry.Id)) throw new AddOnException("That add-on has an invalid name.");
        var hash = Convert.ToHexString(SHA256.HashData(zip));
        if (!hash.Equals(entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new AddOnException("The download didn't match the catalogue, so it wasn't installed. Try again later.");

        var target = Path.Combine(Folder(entry.Id), entry.Version);
        var staging = target + ".installing";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            ExtractSafely(zip, staging);

            var manifestPath = Path.Combine(staging, "manifest.json");
            var manifest = File.Exists(manifestPath) ? AddOnCatalog.ParseManifest(File.ReadAllText(manifestPath)) : null;
            if (manifest is null || manifest.Id != entry.Id || manifest.Version != entry.Version || manifest.Api != AddOnApi.Version)
                throw new AddOnException("The add-on package doesn't match its catalogue entry.");
            if (!File.Exists(Path.Combine(staging, manifest.Entry)))
                throw new AddOnException("The add-on package is missing its program file.");

            if (Directory.Exists(target))
            {
                // Same version again (a reinstall). If it's loaded right now its DLL is locked: keep what's there.
                try { Directory.Delete(target, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Directory.Delete(staging, true); return Finish(entry.Id, target); }
            }
            Directory.Move(staging, target);
            return Finish(entry.Id, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(staging);
            throw new AddOnException("Couldn't install the add-on: " + ex.Message, ex);
        }
        catch (AddOnException)
        {
            TryDelete(staging);
            throw;
        }
    }

    private InstalledAddOn Finish(string id, string folder)
    {
        File.WriteAllText(CurrentFile(id), Path.GetFileName(folder));
        if (File.Exists(RemovedFile(id))) File.Delete(RemovedFile(id));
        var manifest = AddOnCatalog.ParseManifest(File.ReadAllText(Path.Combine(folder, "manifest.json")))!;
        return new InstalledAddOn(manifest, folder, Path.Combine(folder, manifest.Entry));
    }

    /// <summary>Unzips, refusing any entry that would land outside the folder ("zip slip").</summary>
    private static void ExtractSafely(byte[] zip, string folder)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (var e in archive.Entries)
        {
            var dest = Path.GetFullPath(Path.Combine(root, e.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new AddOnException("The add-on package tried to write outside its folder, so it wasn't installed.");
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    /// <summary>Hides the add-on now; its files go at the next start.</summary>
    public void MarkRemoved(string id)
    {
        if (!AddOnCatalog.IsValidId(id) || !Directory.Exists(Folder(id))) return;
        File.WriteAllText(RemovedFile(id), DateTime.UtcNow.ToString("o"));
    }

    /// <summary>Run before loading anything: deletes removed add-ons, old versions and half-finished installs. Skips what's locked.</summary>
    public void CleanUp()
    {
        if (!Directory.Exists(Root)) return;
        string[] dirs;
        try { dirs = Directory.GetDirectories(Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var dir in dirs)
        {
            try
            {
                var id = Path.GetFileName(dir);
                if (IsPendingRemoval(id)) { TryDelete(dir); continue; }
                var current = File.Exists(CurrentFile(id)) ? File.ReadAllText(CurrentFile(id)).Trim() : null;
                if (current is null) continue; // half-installed: leave it for Install to retry
                foreach (var version in Directory.GetDirectories(dir))
                    if (!string.Equals(Path.GetFileName(version), current, StringComparison.OrdinalIgnoreCase)) TryDelete(version);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // try again next start
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // still locked: next start
    }
}
