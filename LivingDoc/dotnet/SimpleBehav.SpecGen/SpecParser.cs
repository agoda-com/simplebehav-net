using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SimpleBehav.SpecGen;

/// <param name="TypeArg">
/// Generic type on the call when present (e.g. <c>CheckoutPage</c> from <c>.When&lt;CheckoutPage&gt;(...)</c>).
/// Used by the page graph; ignored by living-doc prose pages.
/// </param>
public record ScenarioStep(string Type, string Label, string? TypeArg = null);

public record Scenario(
    string MethodName,
    bool IsProvesBug,
    IReadOnlyList<ScenarioStep> Steps,
    IReadOnlyList<string>? Cases = null
);

public static class SpecParser
{
    /// <summary>
    /// Parses a test file. <c>.kt</c> and <c>.ts</c>/<c>.tsx</c> use the polyglot scanner;
    /// everything else is C# via Roslyn.
    /// </summary>
    public static IReadOnlyList<Scenario> ParseFile(string path, string source) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".kt" => ParseKotlin(source),
            ".ts" or ".tsx" => ParseTypeScript(source),
            _ => ParseTestSource(source),
        };

    /// <summary>Kotlin JUnit tests: <c>@Test fun</c> and <c>.given/.When/.then</c> chains.</summary>
    public static IReadOnlyList<Scenario> ParseKotlin(string source) => PolyglotParser.ParseKotlin(source);

    /// <summary>TypeScript <c>node:test</c> tests: <c>test("title", ...)</c> and <c>.given/.when/.then</c> chains.</summary>
    public static IReadOnlyList<Scenario> ParseTypeScript(string source) => PolyglotParser.ParseTypeScript(source);

    /// <summary>
    /// Mode 1: parse SimpleBehav test source (.cs) via Roslyn → scenarios.
    /// Reads .Given/.When/.Then/.And chain labels from each [Test] method.
    /// </summary>
    public static IReadOnlyList<Scenario> ParseTestSource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var scenarios = new List<Scenario>();

        var methods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a => a.Name.ToString() is "Test" or "TestMethod" or "TestCase" or "TestCaseSource"));

        foreach (var method in methods)
        {
            var isProvesBug = method.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a => a.ToString().Contains("ProvesBug"));

            var steps = ExtractChainSteps(method);
            var cases = method.AttributeLists
                .SelectMany(a => a.Attributes)
                .Where(a => a.Name.ToString() is "TestCase")
                .Select(a => string.Join(", ", a.ArgumentList?.Arguments
                    .Select(arg => arg.Expression is LiteralExpressionSyntax lit ? lit.Token.ValueText : arg.Expression.ToString())
                    ?? []))
                .ToList();

            scenarios.Add(new Scenario(method.Identifier.Text, isProvesBug, steps, cases.Count > 0 ? cases : null));
        }

        return scenarios;
    }

    /// <summary>
    /// Mode 2: parse structured spec markdown → scenarios.
    /// Expects ## Scenario: headers with Given/When/Then/And bullet lines.
    /// </summary>
    public static IReadOnlyList<Scenario> ParseSpecMarkdown(string markdown)
    {
        var scenarios = new List<Scenario>();
        var lines = markdown.Split('\n', StringSplitOptions.TrimEntries);

        string? scenarioTitle = null;
        bool isProvesBug = false;
        var steps = new List<ScenarioStep>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## Scenario:"))
            {
                if (scenarioTitle != null)
                    scenarios.Add(new Scenario(ToMethodName(scenarioTitle), isProvesBug, steps.ToList()));

                scenarioTitle = line["## Scenario:".Length..].Trim();
                isProvesBug = scenarioTitle.Contains("[ProvesBug]", StringComparison.OrdinalIgnoreCase);
                scenarioTitle = scenarioTitle.Replace("[ProvesBug]", "", StringComparison.OrdinalIgnoreCase).Trim();
                steps = [];
                continue;
            }

            if (scenarioTitle == null) continue;

            foreach (var keyword in new[] { "- Given:", "- When:", "- Then:", "- And:" })
            {
                if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                var type = keyword.TrimStart('-').TrimEnd(':').Trim();
                var label = line[keyword.Length..].Trim();
                steps.Add(new ScenarioStep(type, label));
                break;
            }
        }

        if (scenarioTitle != null)
            scenarios.Add(new Scenario(ToMethodName(scenarioTitle), isProvesBug, steps.ToList()));

        return scenarios;
    }

    private static IReadOnlyList<ScenarioStep> ExtractChainSteps(MethodDeclarationSyntax method)
    {
        var steps = new List<ScenarioStep>();
        // A chain nests outermost-last-call-first; order by the call's name token to get source order.
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .OrderBy(inv => inv.Expression is MemberAccessExpressionSyntax m ? m.Name.SpanStart : inv.SpanStart);

        foreach (var inv in invocations)
        {
            SimpleNameSyntax? memberName = inv.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name,
                _ => null
            };
            if (memberName is null) continue;

            var name = memberName.Identifier.Text;
            if (name is not ("Given" or "When" or "Then" or "And")) continue;

            string? typeArg = memberName is GenericNameSyntax g
                ? g.TypeArgumentList.Arguments.FirstOrDefault()?.ToString()
                : null;

            // First string argument is the label; ISetup refs (Pages.Checkout) keep their expression text.
            var firstArg = inv.ArgumentList.Arguments.FirstOrDefault();
            if (firstArg?.Expression is LiteralExpressionSyntax lit)
                steps.Add(new ScenarioStep(name, lit.Token.ValueText, typeArg));
            else if (firstArg?.Expression is not null)
                steps.Add(new ScenarioStep(name, firstArg.Expression.ToString().Trim('"'), typeArg));
        }

        return steps;
    }

    private static string ToMethodName(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", words.Select(w =>
            string.Concat(w.Where(char.IsLetterOrDigit))));
    }
}
