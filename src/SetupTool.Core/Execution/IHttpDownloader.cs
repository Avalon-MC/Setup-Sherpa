namespace SetupTool.Core.Execution;

/// <summary>
/// Downloads a URL to a tool-managed temp location. Used by the compose
/// executor to fetch remote compose files (D7). Wrapped so tests can fake it.
/// </summary>
public interface IHttpDownloader
{
    /// <summary>Downloads <paramref name="url"/> to a fresh temp file and returns its path.</summary>
    Task<string> DownloadToTempAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// A thin wrapper around HttpClient that writes to a temp file.
/// </summary>
public sealed class HttpDownloader : IHttpDownloader
{
    private readonly HttpClient _http;

    public HttpDownloader(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<string> DownloadToTempAsync(string url, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), "setuptool-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var fs = File.Create(path))
        {
            await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        return path;
    }
}
