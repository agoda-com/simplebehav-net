namespace SimpleBehav.SpecGen;

public static class DocEmitter
{
    /// <summary>
    /// Emits a markdown spec doc from parsed scenarios.
    /// The doc is a read-only artifact — always derived from green test source.
    /// </summary>
    public static string Emit(string featureName, IReadOnlyList<Scenario> scenarios)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {Humanize(featureName)}");
        sb.AppendLine();

        foreach (var scenario in scenarios)
        {
            var tag = scenario.IsProvesBug ? " [ProvesBug]" : "";
            sb.AppendLine($"## Scenario: {Humanize(scenario.MethodName)}{tag}");
            sb.AppendLine();

            foreach (var step in scenario.Steps)
                sb.AppendLine($"**{step.Type}** {step.Label}");

            sb.AppendLine();

            var symbol = scenario.IsProvesBug ? "⚠" : "✅";
            var note = scenario.IsProvesBug
                ? "Bug-proving — this scenario asserts broken behavior and will turn red when the fix ships."
                : $"Verified by `{scenario.MethodName}`";
            sb.AppendLine($"{symbol} {note}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Humanize(string methodName) =>
        string.Join(" ", methodName.Split('_')
            .SelectMany(part => SplitCamelCase(part)));

    private static IEnumerable<string> SplitCamelCase(string s)
    {
        var current = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c) && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
            current.Append(c);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }
}
