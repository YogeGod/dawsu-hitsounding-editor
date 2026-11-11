namespace OsuHitsound.App;

/// <summary>
/// osu!lazer's File → Edit externally: lazer copies the beatmap set into a folder named by a 64-character SHA-256 under
/// the temp folder, waits while other tools edit it, and imports it back when "Finish editing" is clicked.
/// </summary>
internal static class OsuLazerExternalEdit
{
    /// <summary>
    /// The .osu to open from the newest such folder: the difficulty saved last (lazer's editor is paused meanwhile, so
    /// that's the one being worked on), or null when no set is being edited externally.
    /// </summary>
    public static string? FindMountedBeatmap(string? tempDirectory = null)
    {
        string root = tempDirectory ?? Path.GetTempPath();
        try
        {
            if (!Directory.Exists(root)) return null;
            // Newest mount first; only the first one with a .osu is listed in full.
            foreach (var mount in Directory.EnumerateDirectories(root).Where(IsMount).OrderByDescending(Directory.GetCreationTimeUtc))
            {
                var maps = Directory.GetFiles(mount, "*.osu", SearchOption.TopDirectoryOnly);
                if (maps.Length > 0) return maps.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            }
            return null;
        }
        // Lazer removes the folder when editing finishes, possibly while it's being listed.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool IsMount(string? directory) =>
        directory is not null && Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) is { Length: 64 } name && name.All(Uri.IsHexDigit);
}
