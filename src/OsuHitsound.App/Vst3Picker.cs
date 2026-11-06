using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OsuHitsound.App;

/// <summary>Finding VST3 plug-ins: the installed ones in Common Files\VST3, and a file picker for others.</summary>
internal static class Vst3Picker
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");

    /// <summary>The installed .vst3s by label: the file name, with its folder when two share a name.</summary>
    public static IReadOnlyDictionary<string, string> Installed()
    {
        var root = Root;
        var installed = Directory.Exists(root) ? Directory.EnumerateFileSystemEntries(root, "*.vst3", new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
        }).Where(p => !InsideBundle(root, p)).Order().ToArray() : [];
        return installed.Select(p => (Name: Path.GetFileNameWithoutExtension(p), Path: p))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Select(p => (Label: g.Count() == 1 ? p.Name : p.Name + " — " + Path.GetRelativePath(root, p.Path), p.Path)))
            .OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.Label, p => p.Path);
    }

    // A bundle (Serum2.vst3\Contents\x86_64-win\Serum2.vst3) holds its binary under the same name: list the bundle once.
    internal static bool InsideBundle(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).SkipLast(1)
            .Any(folder => folder.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase));

    /// <summary>A file picker for a .vst3, starting in the installed plug-ins' folder. Null when cancelled.</summary>
    public static async Task<string?> Browse(Window owner, string title = "Choose an installed VST3 instrument")
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(Root),
            FileTypeFilter = [new FilePickerFileType("VST3 instrument") { Patterns = ["*.vst3"] }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
