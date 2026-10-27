using System.Net;
using System.Text.Json;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Tts;

[Collection(PostgresCollection.Name)]
public sealed class TtsObjectDownloadTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private static string RepositoryFile(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "Matchmaker.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }

    [Fact]
    public async Task The_object_is_downloaded_as_a_tts_saved_object_carrying_the_script()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/tts/WarhammerMatchmaker.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WarhammerMatchmaker.json", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tile = Assert.Single(saved.RootElement.GetProperty("ObjectStates").EnumerateArray());
        Assert.Equal("Warhammer Matchmaker", tile.GetProperty("Nickname").GetString());
        Assert.Equal(RepositoryFile("tts/WarhammerMatchmaker.lua"), tile.GetProperty("LuaScript").GetString());
    }

    [Fact]
    public async Task The_install_page_explains_how_to_add_the_object_and_send_the_score()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/tts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("/tts/WarhammerMatchmaker.json", html);
        Assert.Contains("Saved Objects", html);
        Assert.Contains("Envoyer le score final", html);
        Assert.Contains("Rouge", html);
    }

    public void Dispose() => _factory.Dispose();
}
