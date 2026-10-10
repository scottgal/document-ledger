using System.Security.Cryptography;
using System.Text.Json;
using Mostlylucid.Morph.Documents;

internal static class DatasetInspection
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string root, string output)
    {
        Directory.CreateDirectory(output);
        var paths = Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var groups = paths.GroupBy(Path.GetDirectoryName).OrderBy(g => g.Key).ToArray();
        var observations = new List<object>();
        foreach (var group in groups)
        {
            foreach (var path in group.Take(3))
            {
                await using var document = await DocumentSession.OpenAsync(path);
                var text = await document.GetPageTextAsync(0);
                var stem = Path.GetFileNameWithoutExtension(path);
                await File.WriteAllBytesAsync(Path.Combine(output, stem + ".png"), await document.RenderPageAsync(0));
                await File.WriteAllTextAsync(Path.Combine(output, stem + ".txt"), text);
                observations.Add(new { path = Path.GetRelativePath(root,path), hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),
                    pages = document.PageCount, textCharacters = text.Length, firstPage = document.Pages[0] });
                if (path == group.First()) Console.WriteLine($"{Path.GetRelativePath(root,group.Key!)} ({group.Count()} PDFs):\n{text[..Math.Min(1800,text.Length)]}");
            }
        }
        var report = new { totalPdfs = paths.Length, groups = groups.Select(g => new { path = Path.GetRelativePath(root,g.Key!), count = g.Count() }), observations };
        await File.WriteAllTextAsync(Path.Combine(output,"inspection.json"), JsonSerializer.Serialize(report, JsonOptions));
        Console.WriteLine($"Inspected {observations.Count} samples from {paths.Length} PDFs. Artifacts: {output}");
    }
}
