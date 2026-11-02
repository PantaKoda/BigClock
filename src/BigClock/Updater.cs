using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigClock;

public sealed record UpdateInfo(Version Version, string Tag, Uri DownloadUrl, long Size, string? Sha256);

public sealed class UpdateException(string message) : Exception(message);

public sealed class Updater(HttpClient http)
{
    public const string AssetName = "BigClock.exe";
    public const string LatestReleaseUrl = "https://api.github.com/repos/PantaKoda/BigClock/releases/latest";

    private const string NewSuffix = ".new";
    private const string OldSuffix = ".old";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    public static HttpClient CreateHttpClient()
    {
        // HttpClient.Timeout would not cover reading the body; each call has its own deadline instead.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BigClock-Updater");
        return client;
    }

    /// <summary>Returns the latest release when it is newer than <paramref name="current"/>, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current)
    {
        using var deadline = new CancellationTokenSource(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new UpdateException("GitHub is rate limiting update checks. Try again later.");
        if (!response.IsSuccessStatusCode)
            throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");

        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(body, cancellationToken: deadline.Token)
            ?? throw new UpdateException("GitHub returned an empty release.");

        return SelectUpdate(release, current);
    }

    public static UpdateInfo? SelectUpdate(ReleaseDto release, Version current)
    {
        var latest = ParseVersion(release.TagName);
        if (latest is null || Normalize(latest) <= Normalize(current))
            return null;

        var asset = release.Assets?.FirstOrDefault(a => a.Name == AssetName)
            ?? throw new UpdateException($"Release {release.TagName} has no {AssetName}.");

        return new UpdateInfo(latest, release.TagName!, new Uri(asset.DownloadUrl), asset.Size, ParseSha256(asset.Digest));
    }

    /// <summary>Accepts tags such as "v1.2.3"; pre-release suffixes are not treated as versions.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = tag?.TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }

    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        return digest is not null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    // "1.2" and "1.2.0.0" must compare as equal, but Version treats missing parts as lower.
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Downloads next to <paramref name="exePath"/> so the swap stays on one volume.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, string exePath, IProgress<int> progress)
    {
        using var deadline = new CancellationTokenSource(DownloadTimeout);
        var target = exePath + NewSuffix;

        try
        {
            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;

            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var file = File.Create(target))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    if (update.Size > 0)
                        progress.Report((int)(received * 100 / update.Size));
                }
            }

            if (received != update.Size)
                throw new UpdateException($"Download was incomplete ({received} of {update.Size} bytes).");

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (update.Sha256 is not null && !actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("Download failed its integrity check.");

            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Windows allows renaming a running exe, so the old file moves aside for the next start to delete.</summary>
    public static void SwapIn(string exePath, string newExePath)
    {
        var old = exePath + OldSuffix;
        File.Delete(old);
        File.Move(exePath, old);
        try
        {
            File.Move(newExePath, exePath);
        }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
    }

    public static void RollBack(string exePath)
    {
        File.Move(exePath + OldSuffix, exePath, overwrite: true);
    }

    public static void DeleteLeftovers(string exePath)
    {
        TryDelete(exePath + NewSuffix);
        TryDelete(exePath + OldSuffix);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retried on the next start.
        }
    }
}

public sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("assets")] public List<ReleaseAssetDto>? Assets { get; set; }
}

public sealed class ReleaseAssetDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}
