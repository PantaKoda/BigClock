using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace BigClock.Tests;

public sealed class UpdaterTests : IDisposable
{
    private static readonly Version Current = new(1, 2, 0, 0);

    private readonly string _dir = Directory.CreateTempSubdirectory("bigclock-tests").FullName;

    private string ExePath => Path.Combine(_dir, "BigClock.exe");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string ReleaseJson(string tag, string digest = "sha256:abc", string asset = "BigClock.exe") => $$"""
        {"tag_name":"{{tag}}","assets":[{"name":"{{asset}}","size":7,
         "browser_download_url":"https://example.test/{{asset}}","digest":"{{digest}}"}]}
        """;

    private static Updater UpdaterReturning(HttpStatusCode status, string body = "") =>
        new(new HttpClient(new StubHandler(status, Encoding.UTF8.GetBytes(body))));

    [Theory]
    [InlineData("v1.2.1", true)]
    [InlineData("v1.10.0", true)] // Numeric, not alphabetical.
    [InlineData("2.0", true)]
    [InlineData("v1.2.0", false)]
    [InlineData("v1.2", false)] // Same version, written shorter.
    [InlineData("v1.1.9", false)]
    [InlineData("v1.3.0-beta", false)]
    [InlineData("nightly", false)]
    public async Task Offers_only_strictly_newer_releases(string tag, bool expected)
    {
        var update = await UpdaterReturning(HttpStatusCode.OK, ReleaseJson(tag)).CheckAsync(Current);

        Assert.Equal(expected, update is not null);
    }

    [Fact]
    public async Task Reads_download_details_from_the_release()
    {
        var update = await UpdaterReturning(HttpStatusCode.OK, ReleaseJson("v1.3.0", "sha256:ABC123")).CheckAsync(Current);

        Assert.Equal("v1.3.0", update!.Tag);
        Assert.Equal("ABC123", update.Sha256);
        Assert.Equal(7, update.Size);
        Assert.Equal("https://example.test/BigClock.exe", update.DownloadUrl.ToString());
    }

    [Fact]
    public async Task Repo_without_releases_means_no_update()
    {
        Assert.Null(await UpdaterReturning(HttpStatusCode.NotFound).CheckAsync(Current));
    }

    [Fact]
    public async Task Rate_limit_is_reported()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() => UpdaterReturning(HttpStatusCode.Forbidden).CheckAsync(Current));

        Assert.Contains("rate limiting", ex.Message);
    }

    [Fact]
    public async Task Newer_release_without_the_exe_is_an_error()
    {
        var updater = UpdaterReturning(HttpStatusCode.OK, ReleaseJson("v9.0.0", asset: "other.zip"));

        await Assert.ThrowsAsync<UpdateException>(() => updater.CheckAsync(Current));
    }

    private static UpdateInfo Info(long size, string? sha256) =>
        new(new Version(2, 0), "v2.0.0", new Uri("https://example.test/BigClock.exe"), size, sha256);

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public async Task Download_writes_verified_file_beside_the_exe()
    {
        var payload = Encoding.UTF8.GetBytes("new exe");
        var updater = new Updater(new HttpClient(new StubHandler(HttpStatusCode.OK, payload)));

        var path = await updater.DownloadAsync(Info(payload.Length, Sha256Hex(payload)), ExePath, new Progress<int>());

        Assert.Equal(ExePath + ".new", path);
        Assert.Equal(payload, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Corrupt_download_is_rejected_and_removed()
    {
        var payload = Encoding.UTF8.GetBytes("tampered");
        var updater = new Updater(new HttpClient(new StubHandler(HttpStatusCode.OK, payload)));

        await Assert.ThrowsAsync<UpdateException>(() =>
            updater.DownloadAsync(Info(payload.Length, new string('0', 64)), ExePath, new Progress<int>()));

        Assert.False(File.Exists(ExePath + ".new"));
    }

    [Fact]
    public async Task Truncated_download_is_rejected_and_removed()
    {
        var payload = Encoding.UTF8.GetBytes("short");
        var updater = new Updater(new HttpClient(new StubHandler(HttpStatusCode.OK, payload)));

        await Assert.ThrowsAsync<UpdateException>(() =>
            updater.DownloadAsync(Info(payload.Length + 10, null), ExePath, new Progress<int>()));

        Assert.False(File.Exists(ExePath + ".new"));
    }

    [Fact]
    public void Swap_replaces_exe_and_keeps_old_for_cleanup()
    {
        File.WriteAllText(ExePath, "old");
        File.WriteAllText(ExePath + ".new", "new");

        Updater.SwapIn(ExePath, ExePath + ".new");

        Assert.Equal("new", File.ReadAllText(ExePath));
        Assert.Equal("old", File.ReadAllText(ExePath + ".old"));

        Updater.DeleteLeftovers(ExePath);
        Assert.False(File.Exists(ExePath + ".old"));
        Assert.True(File.Exists(ExePath));
    }

    [Fact]
    public void Failed_swap_restores_the_current_exe()
    {
        File.WriteAllText(ExePath, "old");

        Assert.Throws<FileNotFoundException>(() => Updater.SwapIn(ExePath, ExePath + ".missing"));

        Assert.Equal("old", File.ReadAllText(ExePath));
    }

    [Fact]
    public void Rollback_restores_the_previous_exe()
    {
        File.WriteAllText(ExePath, "old");
        File.WriteAllText(ExePath + ".new", "new");
        Updater.SwapIn(ExePath, ExePath + ".new");

        Updater.RollBack(ExePath);

        Assert.Equal("old", File.ReadAllText(ExePath));
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
