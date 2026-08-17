namespace SetupSherpa.Core.Execution;

/// <summary>
/// Parses a simple <c>.env</c> file into a key/value map. Rules:
/// <list type="bullet">
/// <item><c>KEY=VALUE</c>; the first <c>=</c> splits key from value, so values
/// may themselves contain <c>=</c> (e.g. a password).</item>
/// <item>Surrounding matching single or double quotes are stripped from the value.</item>
/// <item>Whitespace around the <c>=</c> is trimmed (<c>KEY = value</c> works).</item>
/// <item><c>#</c> comment lines and blank lines are ignored.</item>
/// <item>A line with no <c>=</c> is ignored silently (never fails the whole file).</item>
/// </list>
/// </summary>
public static class DotEnvFile
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue; // no '=' — ignore silently

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (key.Length == 0)
                continue;

            map[key] = StripQuotes(value);
        }
        return map;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2)
        {
            char first = value[0];
            char last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                return value[1..^1];
        }
        return value;
    }
}
