namespace SimpleBehav.SpecGen;

/// <summary>
/// Reads Given / When / Then chains from Kotlin (JUnit) and TypeScript (node:test) sources.
/// C# stays on Roslyn. The chain is the same shape in every language, so the living doc
/// does not need a Kotlin or TypeScript compiler.
/// </summary>
internal static class PolyglotParser
{
    private enum Language { Kotlin, TypeScript }

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "internal", "protected", "final", "open", "abstract", "override",
        "suspend", "tailrec", "operator", "infix", "inline", "external", "expect", "actual",
        "lateinit", "const", "enum", "data", "sealed", "inner", "annotation", "companion", "value",
    };

    private static readonly HashSet<string> TestAnnotations = new(StringComparer.Ordinal)
    {
        "Test", "ParameterizedTest", "RepeatedTest", "TestFactory", "TestTemplate",
    };

    public static IReadOnlyList<Scenario> ParseKotlin(string source) => ParseFunctions(source);

    public static IReadOnlyList<Scenario> ParseTypeScript(string source) => ParseNodeTests(source);

    private static List<Scenario> ParseFunctions(string source)
    {
        var scenarios = new List<Scenario>();
        var pending = new List<Annotation>();
        var i = 0;
        while (i < source.Length)
        {
            if (TryConsumeString(source, ref i, Language.Kotlin, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (source[i] == '@')
            {
                pending.Add(ReadAnnotation(source, ref i));
                continue;
            }

            if (!TryReadIdentifier(source, ref i, out var word))
            {
                i++;
                continue;
            }

            if (Modifiers.Contains(word)) continue;

            if (word == "fun")
            {
                var annotations = pending;
                pending = [];
                var atFun = i - word.Length;
                if (!TryReadFunction(source, ref i, out var name, out var bodyStart, out var bodyEnd))
                {
                    i = atFun + word.Length;
                    continue;
                }

                if (!annotations.Any(a => TestAnnotations.Contains(a.SimpleName))) continue;

                scenarios.Add(new Scenario(
                    name,
                    annotations.Any(a => a.FullName.Contains("ProvesBug", StringComparison.OrdinalIgnoreCase)),
                    ExtractSteps(source, bodyStart, bodyEnd, Language.Kotlin),
                    CasesOf(annotations)));
                continue;
            }

            pending = [];
        }

        return scenarios;
    }

    private static List<Scenario> ParseNodeTests(string source)
    {
        var scenarios = new List<Scenario>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < source.Length)
        {
            if (TryConsumeString(source, ref i, Language.TypeScript, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (!TryReadIdentifier(source, i, out var word, out var after) || word is not ("test" or "it"))
            {
                i++;
                continue;
            }

            var cursor = after;
            SkipTrivia(source, ref cursor);
            if (cursor < source.Length && source[cursor] == '.')
            {
                var memberAt = cursor + 1;
                SkipTrivia(source, ref memberAt);
                if (TryReadIdentifier(source, memberAt, out var member, out var memberEnd) && member is "only" or "skip" or "todo")
                {
                    cursor = memberEnd;
                    SkipTrivia(source, ref cursor);
                }
                else
                {
                    i = after;
                    continue;
                }
            }

            if (cursor >= source.Length || source[cursor] != '(')
            {
                i = after;
                continue;
            }

            var close = CloseParen(source, cursor, Language.TypeScript);
            var args = SplitArguments(source, cursor, Language.TypeScript);
            i = close + 1;
            if (args.Count == 0 || !TryDecodeString(args[0], Language.TypeScript, out var title)) continue;

            var provesBug = title.Contains("[ProvesBug]", StringComparison.OrdinalIgnoreCase);
            title = title.Replace("[ProvesBug]", "", StringComparison.OrdinalIgnoreCase).Trim();
            var method = Unique(MethodName(title), used);
            scenarios.Add(new Scenario(
                method,
                provesBug,
                ExtractSteps(source, cursor + 1, close, Language.TypeScript)));
        }

        return scenarios;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name.Length == 0 ? "Scenario" : name;
        var next = candidate;
        var n = 2;
        while (!used.Add(next)) next = candidate + "_" + n++;
        return next;
    }

    private static string MethodName(string title)
    {
        var parts = title.Split([' ', '\t', '-', '_', '.', ',', ':', ';', '!', '?', '/', '\\', '(', ')', '[', ']', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        var name = string.Concat(parts.Select(part =>
        {
            var letters = string.Concat(part.Where(char.IsLetterOrDigit));
            return letters.Length == 0 ? "" : char.ToUpperInvariant(letters[0]) + letters[1..];
        }));
        return name.Length == 0 ? "Scenario" : name;
    }

    private static List<ScenarioStep> ExtractSteps(string source, int start, int end, Language language)
    {
        var steps = new List<ScenarioStep>();
        var i = start;
        while (i < end)
        {
            if (TryConsumeString(source, ref i, language, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (source[i] != '.' || i + 1 >= end || !TryReadIdentifier(source, i + 1, out var name, out var afterName) || !Canonical(name, out var type))
            {
                i++;
                continue;
            }

            var cursor = afterName;
            SkipTrivia(source, ref cursor);
            string? typeArg = null;
            if (cursor < source.Length && source[cursor] == '<')
                typeArg = FirstTypeArgument(ReadBalanced(source, ref cursor, '<', '>', language));
            SkipTrivia(source, ref cursor);
            if (cursor >= source.Length || source[cursor] != '(')
            {
                i++;
                continue;
            }

            var args = SplitArguments(source, cursor, language);
            i = CloseParen(source, cursor, language) + 1;
            if (args.Count == 0) continue;

            var label = TryDecodeString(args[0], language, out var decoded) ? decoded : args[0].Trim();
            if (language == Language.TypeScript && type == "When" && typeArg is null && args.Count >= 2 && IsTypeReference(args[1]))
                typeArg = args[1].Trim();
            steps.Add(new ScenarioStep(type, label, typeArg));
        }

        return steps;
    }

    private static bool Canonical(string name, out string type)
    {
        type = name switch
        {
            "Given" or "given" or "Arrange" or "arrange" => "Given",
            "When" or "when" or "Act" or "act" => "When",
            "Then" or "then" or "Assert" or "assert" => "Then",
            "And" or "and" => "And",
            _ => "",
        };
        return type.Length > 0;
    }

    private static bool IsTypeReference(string argument)
    {
        var text = argument.Trim();
        var generic = text.IndexOf('<');
        if (generic >= 0) text = text[..generic].Trim();
        if (text.Length == 0 || text.Contains("=>") || text.Contains('{') || text.Contains('(')) return false;
        return text.Split('.').All(part => part.Length > 0 && IsIdStart(part[0]) && part.All(IsIdPart));
    }

    private readonly record struct Annotation(string SimpleName, string FullName, string Arguments);

    private static List<string>? CasesOf(List<Annotation> annotations)
    {
        var cases = new List<string>();
        foreach (var annotation in annotations)
        {
            if (annotation.SimpleName is not ("ValueSource" or "CsvSource")) continue;
            var literals = StringLiterals(annotation.Arguments, Language.Kotlin);
            if (literals.Count > 0)
            {
                cases.AddRange(literals);
                continue;
            }

            if (annotation.SimpleName != "ValueSource") continue;
            var open = annotation.Arguments.IndexOf('[');
            var close = annotation.Arguments.LastIndexOf(']');
            if (open < 0 || close <= open) continue;
            foreach (var part in annotation.Arguments[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                cases.Add(part);
        }

        return cases.Count == 0 ? null : cases;
    }

    private static Annotation ReadAnnotation(string source, ref int i)
    {
        i++; // @
        SkipTrivia(source, ref i);
        var full = ReadDottedName(source, ref i);
        var simple = full.Contains('.') ? full[(full.LastIndexOf('.') + 1)..] : full;
        SkipTrivia(source, ref i);
        var arguments = "";
        if (i < source.Length && source[i] == '(')
        {
            var close = CloseParen(source, i, Language.Kotlin);
            arguments = source[(i + 1)..close];
            i = close + 1;
        }

        return new Annotation(simple, full, arguments);
    }

    private static bool TryReadFunction(string source, ref int i, out string name, out int bodyStart, out int bodyEnd)
    {
        name = "";
        bodyStart = bodyEnd = i;
        SkipTrivia(source, ref i);
        if (i < source.Length && source[i] == '<')
            ReadBalanced(source, ref i, '<', '>', Language.Kotlin);
        SkipTrivia(source, ref i);
        if (!TryReadIdentifier(source, ref i, out name)) return false;
        SkipTrivia(source, ref i);
        while (i < source.Length && source[i] == '.')
        {
            i++;
            SkipTrivia(source, ref i);
            if (!TryReadIdentifier(source, ref i, out name)) return false;
            SkipTrivia(source, ref i);
        }

        if (i < source.Length && source[i] == '<')
            ReadBalanced(source, ref i, '<', '>', Language.Kotlin);
        SkipTrivia(source, ref i);
        if (i >= source.Length || source[i] != '(') return false;
        i = CloseParen(source, i, Language.Kotlin) + 1;
        bodyStart = i;
        bodyEnd = FindBodyEnd(source, bodyStart);
        i = bodyEnd;
        return true;
    }

    private static int FindBodyEnd(string source, int start)
    {
        var i = start;
        var brace = 0;
        var paren = 0;
        while (i < source.Length)
        {
            if (TryConsumeString(source, ref i, Language.Kotlin, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (char.IsWhiteSpace(source[i]))
            {
                i++;
                continue;
            }

            if (brace == 0 && paren == 0 && IsNextDeclaration(source, i)) return i;

            var c = source[i++];
            if (c == '(') paren++;
            else if (c == ')' && paren > 0) paren--;
            else if (c == '{') brace++;
            else if (c == '}')
            {
                if (brace == 0) return i - 1;
                brace--;
                if (brace == 0 && paren == 0) return i;
            }
        }

        return i;
    }

    private static bool IsNextDeclaration(string source, int i)
    {
        if (source[i] == '@') return true;
        return TryReadIdentifier(source, i, out var word, out _) && word is
            "fun" or "class" or "object" or "interface" or "val" or "var" or "typealias" or "constructor" or "init";
    }

    private static List<string> SplitArguments(string source, int openParen, Language language)
    {
        var args = new List<string>();
        var i = openParen + 1;
        var start = i;
        var paren = 1;
        var brace = 0;
        var bracket = 0;
        while (i < source.Length && paren > 0)
        {
            if (TryConsumeString(source, ref i, language, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            var c = source[i];
            if (c == '(') paren++;
            else if (c == ')')
            {
                paren--;
                if (paren == 0)
                {
                    Add(source[start..i]);
                    return args;
                }
            }
            else if (c == '{') brace++;
            else if (c == '}') brace--;
            else if (c == '[') bracket++;
            else if (c == ']') bracket--;
            else if (c == ',' && paren == 1 && brace == 0 && bracket == 0)
            {
                Add(source[start..i]);
                start = i + 1;
            }

            i++;
        }

        return args;

        void Add(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 0) args.Add(trimmed);
        }
    }

    private static int CloseParen(string source, int openParen, Language language)
    {
        var i = openParen + 1;
        var paren = 1;
        while (i < source.Length && paren > 0)
        {
            if (TryConsumeString(source, ref i, language, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (source[i] == '(') paren++;
            else if (source[i] == ')') paren--;
            i++;
        }

        return i - 1;
    }

    private static string ReadBalanced(string source, ref int i, char open, char close, Language language)
    {
        var start = i + 1;
        var depth = 1;
        i++;
        while (i < source.Length && depth > 0)
        {
            if (TryConsumeString(source, ref i, language, out _)) continue;
            if (TryConsumeComment(source, ref i)) continue;
            if (source[i] == open) depth++;
            else if (source[i] == close) depth--;
            i++;
        }

        return source[start..(i - 1)];
    }

    private static string? FirstTypeArgument(string inside)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inside.Length; i++)
        {
            var c = inside[i];
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
            else if (c == ',' && depth == 0)
                return inside[start..i].Trim();
        }

        var first = inside[start..].Trim();
        return first.Length == 0 ? null : first;
    }

    private static string ReadDottedName(string source, ref int i)
    {
        if (!TryReadIdentifier(source, ref i, out var name)) return "";
        while (true)
        {
            var cursor = i;
            SkipTrivia(source, ref cursor);
            if (cursor >= source.Length || source[cursor] != '.') return name;
            var afterDot = cursor + 1;
            SkipTrivia(source, ref afterDot);
            if (!TryReadIdentifier(source, ref afterDot, out var next)) return name;
            name = name + "." + next;
            i = afterDot;
        }
    }

    private static List<string> StringLiterals(string text, Language language)
    {
        var values = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (TryConsumeString(text, ref i, language, out var value))
            {
                if (value is not null) values.Add(value);
                continue;
            }

            i++;
        }

        return values;
    }

    private static bool TryDecodeString(string text, Language language, out string value)
    {
        value = "";
        var i = 0;
        SkipTrivia(text, ref i);
        if (!TryConsumeString(text, ref i, language, out var decoded) || decoded is null) return false;
        SkipTrivia(text, ref i);
        if (i != text.Length) return false;
        value = decoded;
        return true;
    }

    private static bool TryConsumeString(string source, ref int i, Language language, out string? value)
    {
        value = null;
        if (i >= source.Length) return false;

        if (language == Language.Kotlin && i + 2 < source.Length && source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"')
        {
            var start = i + 3;
            var end = source.IndexOf("\"\"\"", start, StringComparison.Ordinal);
            if (end < 0)
            {
                i = source.Length;
                value = source[start..];
                return true;
            }

            value = source[start..end];
            i = end + 3;
            return true;
        }

        var quote = source[i];
        var allowed = language == Language.TypeScript ? quote is '"' or '\'' or '`' : quote == '"';
        if (!allowed) return false;

        var builder = new System.Text.StringBuilder();
        i++;
        while (i < source.Length)
        {
            var c = source[i++];
            if (c == '\\' && i < source.Length)
            {
                var escaped = source[i++];
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                });
                continue;
            }

            if (c == quote)
            {
                value = builder.ToString();
                return true;
            }

            builder.Append(c);
        }

        value = builder.ToString();
        return true;
    }

    private static bool TryConsumeComment(string source, ref int i)
    {
        if (i + 1 >= source.Length || source[i] != '/') return false;
        if (source[i + 1] == '/')
        {
            while (i < source.Length && source[i] != '\n') i++;
            return true;
        }

        if (source[i + 1] != '*') return false;
        i += 2;
        while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
        i = Math.Min(source.Length, i + 2);
        return true;
    }

    private static void SkipTrivia(string source, ref int i)
    {
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i]))
            {
                i++;
                continue;
            }

            if (!TryConsumeComment(source, ref i)) return;
        }
    }

    private static bool TryReadIdentifier(string source, ref int i, out string identifier)
    {
        if (!TryReadIdentifier(source, i, out identifier, out var next)) return false;
        i = next;
        return true;
    }

    private static bool TryReadIdentifier(string source, int i, out string identifier, out int next)
    {
        identifier = "";
        next = i;
        if (i >= source.Length || !IsIdStart(source[i])) return false;
        if (i > 0 && IsIdPart(source[i - 1])) return false;
        var start = i;
        i++;
        while (i < source.Length && IsIdPart(source[i])) i++;
        identifier = source[start..i];
        next = i;
        return true;
    }

    private static bool IsIdStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
