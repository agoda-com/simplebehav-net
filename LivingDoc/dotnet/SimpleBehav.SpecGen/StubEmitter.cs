namespace SimpleBehav.SpecGen;

public static class StubEmitter
{
    /// <summary>
    /// Emits three stub files from parsed scenarios.
    /// All ISetup and IAssertion references throw NotImplementedException —
    /// CI stays red until the engineer implements them.
    /// </summary>
    public static async Task EmitAsync(
        string featureName,
        IReadOnlyList<Scenario> scenarios,
        string outDir)
    {
        var setups = scenarios
            .SelectMany(s => s.Steps.Where(x => x.Type == "Given"))
            .Select(s => ToIdentifier(s.Label))
            .Distinct()
            .ToList();

        var assertions = scenarios
            .SelectMany(s => s.Steps.Where(x => x.Type is "Then" or "And"))
            .Select(s => ToIdentifier(s.Label))
            .Distinct()
            .ToList();

        await File.WriteAllTextAsync(
            Path.Combine(outDir, $"{featureName}Tests.cs"),
            EmitTests(featureName, scenarios));

        await File.WriteAllTextAsync(
            Path.Combine(outDir, $"{featureName}Context.cs"),
            EmitContext(featureName, setups));

        await File.WriteAllTextAsync(
            Path.Combine(outDir, $"{featureName}Assertions.cs"),
            EmitAssertions(featureName, assertions));
    }

    private static string EmitTests(string featureName, IReadOnlyList<Scenario> scenarios)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using SimpleBehav;");
        sb.AppendLine("using static SimpleBehav.Spec;");
        sb.AppendLine();
        sb.AppendLine("[TestFixture]");
        sb.AppendLine($"public class {featureName}Tests");
        sb.AppendLine("{");

        foreach (var scenario in scenarios)
        {
            sb.AppendLine("    [Test]");
            if (scenario.IsProvesBug)
                sb.AppendLine("    [Category(\"ProvesBug\")]");

            sb.AppendLine($"    public async Task {scenario.MethodName}()");
            sb.AppendLine("    {");
            sb.AppendLine("        await Scenario()");

            foreach (var step in scenario.Steps)
            {
                var id = ToIdentifier(step.Label);
                var line = step.Type switch
                {
                    "Given" => $"            .Given({featureName}Context.{id})",
                    "When"  => $"            .When<object>(\"{step.Label}\")",
                    "Then"  => $"            .Then(\"{step.Label}\", See.{id})",
                    "And"   => $"            .And(\"{step.Label}\", See.{id})",
                    _       => $"            // {step.Type}: {step.Label}"
                };
                sb.AppendLine(line);
            }

            sb.AppendLine("            ;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitContext(string featureName, List<string> setups)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using SimpleBehav;");
        sb.AppendLine();
        sb.AppendLine($"public static class {featureName}Context");
        sb.AppendLine("{");
        foreach (var setup in setups)
            sb.AppendLine($"    public static ISetup {setup} => throw new NotImplementedException();");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitAssertions(string featureName, List<string> assertions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using SimpleBehav;");
        sb.AppendLine();
        sb.AppendLine("public static partial class See");
        sb.AppendLine("{");
        foreach (var assertion in assertions)
            sb.AppendLine($"    public static IAssertion {assertion} => throw new NotImplementedException();");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToIdentifier(string label)
    {
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(w =>
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            return clean.Length == 0 ? "" : char.ToUpper(clean[0]) + clean[1..];
        }));
    }
}
