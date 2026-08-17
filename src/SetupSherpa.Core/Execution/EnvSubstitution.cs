namespace SetupSherpa.Core.Execution;

/// <summary>Raised when an expansionToken listed on a step is missing from `.env`.</summary>
public sealed class EnvSubstitutionException : Exception
{
    public EnvSubstitutionException(string message) : base(message) { }
}

/// <summary>
/// Deterministic <c>$VAR</c> / <c>${VAR}</c> substitution for docker-run and
/// docker-volume commands (D5-compatible: this is NOT shell interpretation — it
/// substitutes only the explicitly-listed tokens from a controlled `.env` file).
/// </summary>
public static class EnvSubstitution
{
    /// <summary>
    /// Replaces the listed tokens in <paramref name="command"/> with their
    /// values from <paramref name="env"/>. Unlisted <c>$VAR</c> is left literal.
    /// </summary>
    /// <exception cref="EnvSubstitutionException">
    /// A listed token is absent from <paramref name="env"/>.
    /// </exception>
    public static string Expand(string command, IReadOnlyList<string> listedTokens, IReadOnlyDictionary<string, string> env)
    {
        if (listedTokens.Count == 0)
            return command;

        // First verify every listed token exists — fail before doing any work
        // so a missing secret is a clean, up-front error naming the token.
        foreach (var token in listedTokens)
        {
            if (!env.ContainsKey(token))
                throw new EnvSubstitutionException(
                    $"'{token}' requested in expansionTokens but not present in .env");
        }

        var result = command;
        // Replace ${VAR} form first (longer, so it isn't partially consumed by $VAR).
        foreach (var token in listedTokens)
        {
            result = result.Replace("${" + token + "}", env[token]);
        }
        // Then the $VAR form. Only exact matches for a listed token are replaced.
        foreach (var token in listedTokens)
        {
            result = ReplaceBare(result, token, env[token]);
        }
        return result;
    }

    /// <summary>
    /// Expands a step's command for docker executors. No-op when no
    /// expansionTokens are listed. Throws if tokens are listed but no `.env`
    /// was loaded (all tokens would be missing).
    /// </summary>
    public static string ExpandCommand(string command, IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string>? env)
    {
        if (tokens.Count == 0)
            return command;
        if (env is null)
            throw new EnvSubstitutionException(
                $"expansionTokens listed ({string.Join(", ", tokens)}) but no .env was available");
        return Expand(command, tokens, env);
    }

    /// <summary>
    /// Expands a bash script's listed tokens, shell-quoting each value so the
    /// substituted text is safe inside the script (bash would otherwise treat
    /// spaces/special chars in the value as syntax). No-op when no tokens are
    /// listed. Throws if tokens are listed but no `.env` was loaded.
    /// </summary>
    public static string ExpandBash(string script, IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string>? env)
    {
        if (tokens.Count == 0)
            return script;
        if (env is null)
            throw new EnvSubstitutionException(
                $"expansionTokens listed ({string.Join(", ", tokens)}) but no .env was available");

        var result = script;
        foreach (var token in tokens)
        {
            if (!env.ContainsKey(token))
                throw new EnvSubstitutionException(
                    $"'{token}' requested in expansionTokens but not present in .env");
            string quoted = ShellQuote(env[token]);
            result = result.Replace("${" + token + "}", quoted);
            result = ReplaceBare(result, token, quoted);
        }
        return result;
    }

    /// <summary>Single-quotes a value for safe embedding in a bash script.</summary>
    private static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'") + "'";

    private static string ReplaceBare(string input, string token, string value)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] == '$')
            {
                // Match $token as a whole identifier (must be followed by non-identifier).
                int end = i + 1 + token.Length;
                if (end <= input.Length
                    && string.CompareOrdinal(input, i + 1, token, 0, token.Length) == 0
                    && (end == input.Length || !IsIdentifierChar(input[end])))
                {
                    sb.Append(value);
                    i = end;
                    continue;
                }
            }
            sb.Append(input[i]);
            i++;
        }
        return sb.ToString();
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
