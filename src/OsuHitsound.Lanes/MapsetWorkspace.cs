using System.Text;

namespace OsuHitsound.Lanes;

/// <summary>Creates one independent, reusable working mapset in Songs; never overwrites an existing working copy.</summary>
public static class MapsetWorkspace
{
    private const string Marker = ".dawsu-working-mapset";

    public static string Prepare(string osuPath, string songsDirectory)
    {
        string sourcePath = Path.GetFullPath(osuPath), source = Path.GetDirectoryName(sourcePath)!;
        string songs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(songsDirectory));
        if (!Directory.Exists(songs)) throw new IOException("Choose an existing osu! Songs folder in Preferences first.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The difficulty could not be found.", sourcePath);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Open the actual mapset folder and difficulty rather than a link.");
        if (string.Equals(source, songs, StringComparison.OrdinalIgnoreCase)) throw new IOException("Open a difficulty inside its own mapset folder, not directly in Songs.");
        string sourceMarker = Path.Combine(source, Marker);
        if (string.Equals(Path.GetDirectoryName(source), songs, StringComparison.OrdinalIgnoreCase) && File.Exists(sourceMarker) &&
            (File.GetAttributes(sourceMarker) & FileAttributes.ReparsePoint) == 0 && new FileInfo(sourceMarker).Length < 32768)
        {
            string origin = File.ReadAllText(sourceMarker);
            if (Path.IsPathFullyQualified(origin) && string.Equals(DestinationFor(songs, origin), source, StringComparison.OrdinalIgnoreCase)) return sourcePath;
        }

        string destination = DestinationFor(songs, source);
        string marker = Path.Combine(destination, Marker), result = Path.Combine(destination, Path.GetFileName(sourcePath));
        if (Directory.Exists(destination))
        {
            if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("The working folder is a link; it was not opened or changed.");
            if (!MarkerMatches(marker, source))
                throw new IOException("The working-mapset folder is occupied by another folder; it was not changed.");
            if (!File.Exists(result)) throw new IOException("This difficulty is missing from the existing working copy. No original files were overwritten.");
            return result;
        }
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The working mapset cannot be created inside its source mapset.");

        string staging = Path.Combine(songs, ".dawsu-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (string file in Directory.EnumerateFiles(source, "*", options))
            {
                if (Path.GetFileName(file) == Marker) continue;
                string target = Path.Combine(staging, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
                if (Path.GetExtension(file).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                {
                    var map = OsuHitsound.Format.OsuFile.Read(target);
                    foreach (var line in map.Lines.Where(l => l.Section == "Metadata"))
                    {
                        if (line.Text.TrimStart().StartsWith("BeatmapID:", StringComparison.Ordinal)) line.Text = "BeatmapID:0";
                        else if (line.Text.TrimStart().StartsWith("BeatmapSetID:", StringComparison.Ordinal)) line.Text = "BeatmapSetID:-1";
                    }
                    File.WriteAllBytes(target, map.ToBytes());
                }
            }
            File.WriteAllText(Path.Combine(staging, Marker), source);
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination) &&
                (File.GetAttributes(destination) & FileAttributes.ReparsePoint) == 0 && MarkerMatches(marker, source) && File.Exists(result))
            { /* Another open finished the same copy first. Reuse it without overwriting anything. */ }
            return result;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static string DestinationFor(string songs, string source)
    {
        string hash = GenerateManifest.Hash(Encoding.UTF8.GetBytes(source.ToUpperInvariant()))[..12];
        string name = SafeFile.Name(Path.GetFileName(source));
        if (name.Length > 70) name = name[..70];
        return Path.Combine(songs, $"dawsu - {name} [{hash}]");
    }

    private static bool MarkerMatches(string path, string source) => File.Exists(path) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && new FileInfo(path).Length < 32768 &&
        string.Equals(File.ReadAllText(path), source, StringComparison.OrdinalIgnoreCase);
}
