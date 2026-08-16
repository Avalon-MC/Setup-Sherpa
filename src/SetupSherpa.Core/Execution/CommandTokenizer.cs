namespace SetupSherpa.Core.Execution;

/// <summary>
/// Splits a command string into argv tokens the way a shell would for quoting,
/// but WITHOUT any expansion — no globbing, no <c>$</c>, no operators, no
/// <c>;</c>/<c>&amp;&amp;</c>/<c>|</c>. This is the reproducibility guarantee in
/// D5: identical input yields identical tokens every run, and nothing is ever
/// interpreted by a shell.
/// </summary>
public static class CommandTokenizer
{
    public static IReadOnlyList<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        int i = 0;
        bool tokenOpen = false;

        while (i < command.Length)
        {
            char c = command[i];

            // Outside any quote: whitespace ends the current token.
            if (char.IsWhiteSpace(c))
            {
                if (tokenOpen)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                    tokenOpen = false;
                }
                i++;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                tokenOpen = true;
                i = ReadQuoted(command, i, c, sb);
                continue;
            }

            // Any other char, including backslash, is literal.
            tokenOpen = true;
            sb.Append(c);
            i++;
        }

        if (tokenOpen)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    private static int ReadQuoted(string command, int start, char quote, System.Text.StringBuilder sb)
    {
        int i = start + 1;
        while (i < command.Length)
        {
            char c = command[i];

            if (c == quote)
            {
                return i + 1; // closing quote
            }

            if (quote == '"' && c == '\\' && i + 1 < command.Length)
            {
                // Inside double quotes, backslash escapes the next char (like a shell),
                // but produces a literal char — never an expansion.
                char next = command[i + 1];
                if (next == '"' || next == '\\' || next == '$' || next == '`')
                {
                    sb.Append(next);
                    i += 2;
                    continue;
                }
                // A backslash before any other char inside double quotes stays literal.
                sb.Append(c);
                i++;
                continue;
            }

            if (quote == '\'' && c == '\\')
            {
                // Inside single quotes, backslash is literal (POSIX behavior).
                sb.Append(c);
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        // Unterminated quote: consume to end. (Could surface as a validation
        // error later, but for tokenization we are lenient.)
        return i;
    }
}
