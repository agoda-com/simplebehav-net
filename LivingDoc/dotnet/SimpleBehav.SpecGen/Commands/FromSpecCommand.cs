namespace SimpleBehav.SpecGen.Commands;

internal sealed class FromSpecCommand(string specPath, string outDir) : ICommand
{
    public async Task<int> ExecuteAsync()
    {
        if (!File.Exists(specPath))
        {
            Console.Error.WriteLine($"Spec file not found: {specPath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var specSource = await File.ReadAllTextAsync(specPath);
        var scenarios = SpecParser.ParseSpecMarkdown(specSource);
        var featureName = Path.GetFileNameWithoutExtension(specPath)
            .Replace("-", "")
            .Replace("_", "");
        featureName = char.ToUpper(featureName[0]) + featureName[1..];

        await StubEmitter.EmitAsync(featureName, scenarios, outDir);
        Console.WriteLine($"Generated stubs in {outDir}");
        return 0;
    }
}
