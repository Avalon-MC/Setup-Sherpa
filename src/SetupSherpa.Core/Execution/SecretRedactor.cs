namespace SetupSherpa.Core.Execution;

/// <summary>
/// Redacts substituted `.env` values so a secret never surfaces in output or
/// error messages. Sherpa doesn't echo commands, but the expanded command can
/// appear in step-failure exceptions — the guard masks any listed token's value.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    /// Replaces each token's value in <paramref name="text"/> with
    /// <c>&lt;redacted&gt;</c>. If <paramref name="env"/> is null or a token is
    /// absent, the text is returned unchanged (nothing to mask).
    /// </summary>
    public static string Redact(string text, IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string>? env)
    {
        if (env is null || tokens.Count == 0)
            return text;

        var result = text;
        foreach (var token in tokens)
        {
            if (env.TryGetValue(token, out var value) && value.Length > 0)
                result = result.Replace(value, "<redacted>");
        }
        return result;
    }
}
