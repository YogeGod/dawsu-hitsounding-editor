using System.Security.Cryptography;
using System.Text.Json;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MapMoments <staged.osu> <moments.json>");
    return 2;
}
var path = Path.GetFullPath(args[0]);
if (!Path.GetExtension(args[1]).Equals(".json", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Output must be a new .json file.");
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var file = OsuFile.Read(path);
var map = Decompiler.Decompile(file, MapFolder.FromDirectory(Path.GetDirectoryName(path)!));
var result = new
{
    mapSha256 = hash,
    moments = map.Moments.Select(m => new
    {
        timeMs = m.Time,
        objectIndex = Compiler.KeyOf(file, m).ObjectIndex,
        node = m.Node,
        position = m.Position.ToString(),
        volume = m.Volume
    }).OrderBy(m => m.timeMs)
};
using var output = new FileStream(args[1], FileMode.CreateNew);
JsonSerializer.Serialize(output, result, new JsonSerializerOptions { WriteIndented = true });
return 0;
