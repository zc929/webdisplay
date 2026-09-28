using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebDisplay.Services;

/// <summary>Offline-only update-service checks; never contacts GitHub or changes settings.</summary>
internal static class UpdateServiceTests
{
    internal static IReadOnlyList<string> RunAll()
    {
        var passed = new List<string>();
        void Check(string name, Action test) { test(); passed.Add(name); }
        Check("Update versions use semantic precedence and assembly metadata normalization", VersionCases);
        Check("Update checks use the public GitHub endpoint and required headers", () => RequestCasesAsync().GetAwaiter().GetResult());
        Check("Update checks ignore drafts/prereleases and reject unsafe release links", () => ReleaseCasesAsync().GetAwaiter().GetResult());
        Check("Update checks tolerate HTTP, JSON, network, and cancellation failures", () => FailureCasesAsync().GetAwaiter().GetResult());
        Check("Update responses are bounded and rate-limit delays are honored", () => LimitCasesAsync().GetAwaiter().GetResult());
        return passed;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static SemanticVersion Version(string value)
    {
        Assert(SemanticVersion.TryParse(value, out SemanticVersion parsed), "Valid semantic version was rejected");
        return parsed;
    }

    private static void VersionCases()
    {
        Assert(Version("v2.10.0").CompareTo(Version("2.9.0")) > 0, "Versions were compared lexically");
        Assert(Version("2.6.0+first").CompareTo(Version("v2.6.0+second")) == 0, "Build metadata changed version precedence");
        Assert(Version("2.6.0").CompareTo(Version("2.6.0-rc.99")) > 0, "A prerelease outranked a stable release");
        Assert(Version("2.6.0-rc.10").CompareTo(Version("2.6.0-rc.2")) > 0, "Prerelease numbers were compared lexically");
        foreach (string value in new[] { "", "2.6", "2.6.0.1", "02.6.0", "2.6.0-", "2.6.0-01", "2.6.0+", "2.6.0+bad..id", " 2.6.0", "2.6.0/evil" })
            Assert(!SemanticVersion.TryParse(value, out _), "Malformed semantic version was accepted");
        Assert(AppVersion.Normalize("2.6.0+abcdef") == "2.6.0", "Source metadata leaked into display version");
        Assert(AppVersion.Normalize("2.6.0.0") == "2.6.0", "File version was not normalized");
        Assert(SemanticVersion.TryParse(AppVersion.Current, out _) && AppVersion.Display == "v" + AppVersion.Current,
            "The display version does not match its assembly-derived source");
    }

    private static async Task RequestCasesAsync()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == UpdateService.LatestReleaseEndpoint, "Unexpected update request");
            Assert(request.Headers.UserAgent.ToString() == "WebDisplay/" + AppVersion.Current, "Missing product User-Agent");
            Assert(request.Headers.Accept.ToString() == "application/vnd.github+json", "Missing GitHub media type");
            Assert(request.Headers.GetValues("X-GitHub-Api-Version").GetEnumerator().MoveNext(), "Missing GitHub API version");
            Assert(string.Join("", request.Headers.GetValues("X-GitHub-Api-Version")) == UpdateService.ApiVersion, "Wrong GitHub API version");
            Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "Credentials were sent in a public release check");
            return Task.FromResult(Response(Release("v2.10.0")));
        });
        using var client = new HttpClient(handler);
        using (var service = new UpdateService(client))
        {
            UpdateCheckResult result = await service.CheckAsync("2.9.0").ConfigureAwait(false);
            Assert(result.HasUpdate && result.LatestVersion == "2.10.0"
                && result.ReleaseUri?.AbsoluteUri == "https://github.com/zc929/webdisplay/releases/tag/v2.10.0", "New release was not reported correctly");
        }
        Assert(!handler.Disposed, "The service disposed its caller-owned HTTP client");
        UpdateCheckResult same = await CheckJsonAsync(Release("v2.6.0+build.1"), "2.6.0+build.2").ConfigureAwait(false);
        Assert(same.Status == UpdateCheckStatus.UpToDate && !same.HasUpdate, "Equivalent metadata-only versions triggered an update");
        UpdateCheckResult older = await CheckJsonAsync(Release("v2.5.0"), "2.6.0").ConfigureAwait(false);
        Assert(older.Status == UpdateCheckStatus.UpToDate, "An older release was offered as an update");
    }

    private static async Task ReleaseCasesAsync()
    {
        foreach (string json in new[] { Release("v9.0.0", draft: true), Release("v9.0.0", prerelease: true), Release("v9.0.0-preview.1") })
            Assert(!(await CheckJsonAsync(json).ConfigureAwait(false)).HasUpdate, "A draft or prerelease was offered");
        foreach (string url in new[]
        {
            "http://github.com/zc929/webdisplay/releases/tag/v9.0.0",
            "https://github.com.evil.invalid/zc929/webdisplay/releases/tag/v9.0.0",
            "https://name@github.com/zc929/webdisplay/releases/tag/v9.0.0",
            "https://github.com:444/zc929/webdisplay/releases/tag/v9.0.0",
            "https://github.com/another/webdisplay/releases/tag/v9.0.0",
            "https://github.com/zc929/webdisplay/releases/download/v9.0.0/setup.exe",
            "https://github.com/zc929/webdisplay/releases/tag/v8.0.0",
            "https://github.com/zc929/webdisplay/releases/tag/v9.0.0?next=other",
            "https://github.com/zc929/webdisplay/releases/tag/v9.0.0#fragment",
            "https://github.com/zc929/webdisplay/releases/tag/v9.0.0/extra"
        })
        {
            UpdateCheckResult result = await CheckJsonAsync(Release("v9.0.0", url: url)).ConfigureAwait(false);
            Assert(result.Status == UpdateCheckStatus.Unavailable && result.ReleaseUri is null, "An untrusted release URL was exposed");
        }
    }

    private static async Task FailureCasesAsync()
    {
        foreach (HttpStatusCode status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests,
            HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.Redirect })
        {
            using var handler = new FakeHandler((_, _) => Task.FromResult(Response(Release("v9.0.0"), status)));
            using var client = new HttpClient(handler);
            using var service = new UpdateService(client);
            Assert((await service.CheckAsync("2.6.0").ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "HTTP failure was treated as an update");
        }
        foreach (string json in new[] { "{broken", "[]", "{}", "{\"draft\":\"false\",\"prerelease\":false}",
            Release("not-a-version"), "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v9.0.0\",\"html_url\":42}" })
            Assert((await CheckJsonAsync(json).ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "Malformed release data was accepted");

        using (var handler = new FakeHandler((_, _) => throw new HttpRequestException("Offline test")))
        using (var client = new HttpClient(handler))
        using (var service = new UpdateService(client))
            Assert((await service.CheckAsync("2.6.0").ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "Network failure escaped the service");

        using (var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            throw new InvalidOperationException("Canceled fake request resumed");
        }))
        using (var client = new HttpClient(handler))
        using (var service = new UpdateService(client))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30)))
            Assert((await service.CheckAsync("2.6.0", cancel.Token).ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "Cancellation escaped the service");

        using (var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Invalid input should not make a request")))
        using (var client = new HttpClient(handler))
        using (var service = new UpdateService(client))
        {
            Assert((await service.CheckAsync("invalid").ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable && handler.Calls == 0, "Invalid current version reached the network");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert((await service.CheckAsync("2.6.0", canceled.Token).ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable && handler.Calls == 0, "An already-canceled check reached the network");
            client.Dispose();
            Assert((await service.CheckAsync("2.6.0").ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "A disposed client crashed the check");
        }
    }

    private static async Task LimitCasesAsync()
    {
        using (var stream = new NonSeekableStream(new byte[UpdateService.MaximumResponseBytes + 100_000]))
        using (var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })))
        using (var client = new HttpClient(handler))
        using (var service = new UpdateService(client))
        {
            Assert((await service.CheckAsync("2.6.0").ConfigureAwait(false)).Status == UpdateCheckStatus.Unavailable, "An oversized response was accepted");
            Assert(stream.BytesRead <= UpdateService.MaximumResponseBytes + 16 * 1024, "Unknown-length response exceeded its bounded read budget");
        }
        DateTimeOffset now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "120");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", now.AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        Assert(UpdateService.ReadRetryAfter(response, now) == TimeSpan.FromMinutes(5), "The later rate-limit reset was ignored");
        response.Headers.Remove("Retry-After");
        response.Headers.Remove("X-RateLimit-Reset");
        response.Headers.TryAddWithoutValidation("Retry-After", now.AddMinutes(7).ToString("R", CultureInfo.InvariantCulture));
        Assert(UpdateService.ReadRetryAfter(response, now) == TimeSpan.FromMinutes(7), "HTTP-date Retry-After was not parsed");
        response.Headers.Remove("Retry-After");
        response.Headers.TryAddWithoutValidation("Retry-After", "999999999");
        Assert(UpdateService.ReadRetryAfter(response, now) == TimeSpan.FromHours(24), "Retry delay was not capped at 24 hours");
        response.Headers.Remove("Retry-After");
        response.Headers.TryAddWithoutValidation("Retry-After", "not-a-delay");
        Assert(UpdateService.ReadRetryAfter(response, now) is null, "Malformed retry delay was accepted");

        using var retryHandler = new FakeHandler((_, _) =>
        {
            var result = Response("{}", HttpStatusCode.TooManyRequests);
            result.Headers.TryAddWithoutValidation("Retry-After", "600");
            return Task.FromResult(result);
        });
        using var retryClient = new HttpClient(retryHandler);
        using var retryService = new UpdateService(retryClient);
        Assert((await retryService.CheckAsync("2.6.0").ConfigureAwait(false)).RetryAfter == TimeSpan.FromMinutes(10), "Rate-limit delay was not returned to the UI");
    }

    private static string Release(string tag, bool draft = false, bool prerelease = false, string? url = null)
        => JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease,
            html_url = url ?? "https://github.com/zc929/webdisplay/releases/tag/" + Uri.EscapeDataString(tag) });

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<UpdateCheckResult> CheckJsonAsync(string json, string currentVersion = "2.6.0")
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response(json)));
        using var client = new HttpClient(handler);
        using var service = new UpdateService(client);
        return await service.CheckAsync(currentVersion).ConfigureAwait(false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        internal int Calls;
        internal bool Disposed;
        internal FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return _respond(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        internal int BytesRead;
        internal NonSeekableStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { int read = _inner.Read(buffer, offset, count); BytesRead += read; return read; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = _inner.Read(buffer.Span); BytesRead += read; return ValueTask.FromResult(read);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
