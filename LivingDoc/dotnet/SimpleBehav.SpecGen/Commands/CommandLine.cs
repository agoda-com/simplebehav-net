namespace SimpleBehav.SpecGen.Commands;

internal static class CommandLine
{
    public static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  simplebehav-gen <TestClass.cs> --out <dir>          # test source → markdown doc");
        Console.Error.WriteLine("  simplebehav-gen --from-spec <spec.md> --out <dir>   # structured spec md → stubs");
        Console.Error.WriteLine("  simplebehav-gen --livingdoc <testsDir> --out <dir> [--reports <relPath>]");
        Console.Error.WriteLine("                          [--results <artifactsDir>] [--baseline <suite.json>]");
        Console.Error.WriteLine("                          [--confluence [--confluence-dry-run] [--confluence-space <key>] [--confluence-name <name>]");
        Console.Error.WriteLine("                           [--confluence-scope main|mr-<iid>] [--confluence-parent-id <id>] [--confluence-prune]");
        Console.Error.WriteLine("                           [--reports-url <https://…>]]");
        Console.Error.WriteLine("                                                      # all tests → living doc site (HTML), optionally → Confluence");
        Console.Error.WriteLine("                          Confluence credentials: CONFLUENCE_BASE_URL, CONFLUENCE_USER, CONFLUENCE_TOKEN (environment only)");
    }

    /// <summary>Resolves a command from argv, or null with an error message when parsing fails.</summary>
    public static ICommand? TryParse(string[] args, out string? error)
    {
        error = null;

        if (args.Length < 2)
        {
            error = null; // caller prints usage
            return null;
        }

        var outDir = Option(args, "--out");
        if (outDir is null)
        {
            error = "Missing --out <dir>";
            return null;
        }

        return args[0] switch
        {
            "--livingdoc" => new LivingDocCommand(
                testsDir: args[1],
                outDir: outDir,
                reports: Option(args, "--reports"),
                results: Option(args, "--results"),
                baseline: Option(args, "--baseline"),
                args: args),
            "--from-spec" => new FromSpecCommand(args[1], outDir),
            _ => new GenerateDocCommand(args[0], outDir),
        };
    }

    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
    }
}
