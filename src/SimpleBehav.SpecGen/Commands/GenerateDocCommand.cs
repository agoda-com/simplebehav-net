namespace SimpleBehav.SpecGen.Commands;

internal sealed class GenerateDocCommand(string sourcePath, string outDir) : ICommand
{
    public async Task<int> ExecuteAsync()
    {
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Source file not found: {sourcePath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var source = await File.ReadAllTextAsync(sourcePath);
        var scenarios = SpecParser.ParseTestSource(source);
        var featureName = Path.GetFileNameWithoutExtension(sourcePath)
            .Replace("Tests", "");

        var markdown = DocEmitter.Emit(featureName, scenarios);
        var outPath = Path.Combine(outDir, $"{featureName.ToLower().Replace(" ", "-")}.md");
        await File.WriteAllTextAsync(outPath, markdown);
        Console.WriteLine($"Generated doc: {outPath}");
        return 0;
    }
}
