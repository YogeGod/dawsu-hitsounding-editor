// Usage: roundtrip <folder>
// Runs the null round-trip check on every .osu and .osb file under the folder and reports any that fail.

using OsuHitsound.Format;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: roundtrip <folder>");
    return 2;
}

var files = RoundTripCheck.FindFiles(args[0]).ToList();

int failed = 0;
foreach (var path in files)
{
    var problems = RoundTripCheck.RunFile(path);
    if (problems.Count == 0) continue;
    failed++;
    Console.WriteLine(path);
    foreach (var p in problems.Take(5)) Console.WriteLine($"  {p}");
    if (problems.Count > 5) Console.WriteLine($"  ... and {problems.Count - 5} more");
}

Console.WriteLine($"{files.Count - failed} of {files.Count} files passed.");
return failed == 0 ? 0 : 1;
