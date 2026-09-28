using System;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebDisplay.Services;

public enum UpdateCheckStatus { UpToDate, UpdateAvailable, Unavailable }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion = null, Uri? ReleaseUri = null, TimeSpan? RetryAfter = null)
{
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable;
}

/// <summary>
/// Read-only public release checks, independent of WebView2 and its certificate
/// preferences. Never uses credentials, downloads assets, or executes an update.
/// </summary>
public sealed class UpdateService : IDisposable
{
    internal const string LatestReleaseEndpoint = "https://api.github.com/repos/zc929/webdisplay/releases/latest";
    internal const string ApiVersion = "2026-03-10";
    internal const int MaximumResponseBytes = 512 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>An injected client remains owned by its caller; intended for offline testing.</summary>
    public UpdateService(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            MaxResponseHeadersLength = 16
            // No certificate-validation callback: normal Windows HTTPS validation.
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => CheckAsync(AppVersion.Current, cancellationToken);

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested
            || !SemanticVersion.TryParse(currentVersion, out SemanticVersion current))
            return new(UpdateCheckStatus.Unavailable);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        CancellationToken token = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WebDisplay", AppVersion.Current));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .WaitAsync(token).ConfigureAwait(false);
            // Unpublished releases, rate limits, server errors and redirects do
            // not become update notifications. The UI can retry on its schedule.
            if (response.StatusCode != HttpStatusCode.OK)
                return new(UpdateCheckStatus.Unavailable, RetryAfter: ReadRetryAfter(response, DateTimeOffset.UtcNow));
            if (response.Content.Headers.ContentLength is long length && length > MaximumResponseBytes)
                return new(UpdateCheckStatus.Unavailable);

            using Stream stream = await response.Content.ReadAsStreamAsync(token).WaitAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            while (true)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(), token).AsTask().WaitAsync(token).ConfigureAwait(false);
                if (count == 0) break;
                if (body.Length + count > MaximumResponseBytes) return new(UpdateCheckStatus.Unavailable);
                body.Write(buffer, 0, count);
            }
            using JsonDocument document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, checked((int)body.Length)),
                new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object
                || !TryBoolean(release, "draft", out bool draft)
                || !TryBoolean(release, "prerelease", out bool prerelease))
                return new(UpdateCheckStatus.Unavailable);
            if (draft || prerelease) return new(UpdateCheckStatus.UpToDate);
            if (!TryString(release, "tag_name", out string tag)
                || !SemanticVersion.TryParse(tag, out SemanticVersion latest))
                return new(UpdateCheckStatus.Unavailable);
            // A prerelease-looking tag is never promoted, even if incorrectly
            // marked as stable in the remote release metadata.
            if (latest.IsPrerelease) return new(UpdateCheckStatus.UpToDate);
            if (!TryString(release, "html_url", out string url) || !TryReleaseUri(url, tag, out Uri? releaseUri))
                return new(UpdateCheckStatus.Unavailable);
            return latest.CompareTo(current) > 0
                ? new(UpdateCheckStatus.UpdateAvailable, latest.Normalized, releaseUri)
                : new(UpdateCheckStatus.UpToDate, latest.Normalized);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException
            or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // A background check must not interrupt display, including offline,
            // cancellation, invalid payloads, TLS failures, and disposed clients.
            return new(UpdateCheckStatus.Unavailable);
        }
    }

    internal static TimeSpan? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        TimeSpan? delay = null;
        void Include(double seconds)
        {
            TimeSpan candidate = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, TimeSpan.FromHours(24).TotalSeconds));
            if (delay is null || candidate > delay.Value) delay = candidate;
        }
        if (response.Headers.TryGetValues("Retry-After", out var retryValues))
        {
            foreach (string value in retryValues)
            {
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
                    Include(seconds);
                else if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out DateTimeOffset when))
                    Include((when - now).TotalSeconds);
            }
        }
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
        {
            foreach (string value in resetValues)
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long reset))
                    Include((double)reset - now.ToUnixTimeSeconds());
        }
        return delay;
    }

    private static bool TryBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out JsonElement property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    internal static bool TryReleaseUri(string url, string tag, out Uri? releaseUri)
    {
        releaseUri = null;
        const string prefix = "/zc929/webdisplay/releases/tag/";
        if (url.Length > 2048 || url != url.Trim() || url.Contains('\\')) return false;
        foreach (char character in url) if (char.IsControl(character)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string encodedTag = uri.AbsolutePath[prefix.Length..];
        if (encodedTag.Length == 0 || encodedTag.Contains('/')
            || !string.Equals(Uri.UnescapeDataString(encodedTag), tag, StringComparison.Ordinal))
            return false;
        releaseUri = uri;
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_ownsClient) _client.Dispose();
    }
}
