using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.ArmyLists;

[Collection(PostgresCollection.Name)]
public sealed class ArmyListPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task<Guid> ImportCustodes(PlayerId owner, string name = "Custodes 2000")
    {
        using var scope = _factory.Services.CreateScope();
        var lists = scope.ServiceProvider.GetRequiredService<ArmyListService>();
        return (await lists.ImportAsync(owner, Samples.CustodesCode, name, GameFormats.StrikeForce)).Id;
    }

    private static Dictionary<string, string> ImportForm(string code, string name = "Custodes 2000", string format = "Strike Force", string detachment = "") =>
        new() { ["Import.Code"] = code, ["Import.Name"] = name, ["Import.Format"] = format, ["Import.Detachment"] = detachment };

    [Fact]
    public async Task My_lists_require_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/lists");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task My_lists_show_my_imported_lists_and_the_import_form()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me, "Mes Custodes");

        var html = await client.GetStringAsync("/lists");

        Assert.Contains("Mes Custodes", html);
        Assert.Contains("Adeptus Custodes", html);
        Assert.Contains("Code YellowScribe", html);
        Assert.Contains("Strike Force (2000 pts)", html);
    }

    [Fact]
    public async Task Importing_a_valid_code_stores_the_list_and_opens_it()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists", "import", ImportForm(Samples.CustodesCode, "Nouvelle liste"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches("^/lists/[0-9a-f-]{36}$", response.Headers.Location!.AbsolutePath);
        using var scope = _factory.Services.CreateScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me));
        Assert.Equal("Nouvelle liste", stored.Name);
        Assert.Equal(GameFormats.StrikeForce, stored.Format);
    }

    [Fact]
    public async Task The_name_is_optional_when_importing()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists", "import", ImportForm(Samples.CustodesCode, name: ""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me));
        Assert.Equal("Adeptus Custodes – Strike Force", stored.Name);
    }

    [Fact]
    public async Task An_expired_code_is_explained_to_the_player()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.SubmitAsync("/lists", "import", ImportForm("deadbeef"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("inconnu ou expiré", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_malformed_code_is_refused_by_the_form()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.SubmitAsync("/lists", "import", ImportForm("pas-un-code"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("8 caractères", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Any_signed_in_player_can_browse_a_list()
    {
        var owner = MatchmakerWebFactory.NewPlayerId();
        await _factory.CreateClientAsAsync(owner);
        var listId = await ImportCustodes(owner);
        var opponent = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await opponent.GetStringAsync($"/lists/{listId}");

        Assert.Contains("Trajann Valoris", html);
        Assert.Contains("Watcher&#x27;s Axe", html);
        Assert.Contains("Living Fortress", html);
        Assert.DoesNotContain("Supprimer", html);
    }

    [Fact]
    public async Task An_unknown_list_is_not_found()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.GetAsync($"/lists/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_owner_can_rename_their_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        await client.SubmitAsync($"/lists/{listId}", "edit", new Dictionary<string, string> { ["Edit.Name"] = "Custodes tournoi" });

        Assert.Contains("Custodes tournoi", await client.GetStringAsync($"/lists/{listId}"));
    }

    [Fact]
    public async Task The_detachment_given_at_import_is_shown_in_my_lists_and_on_the_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists", "import", ImportForm(Samples.CustodesCode, "Avec détachement", detachment: "Shield Host"));

        var listUrl = response.Headers.Location!.AbsolutePath;
        Assert.Contains("Shield Host", WebUtility.HtmlDecode(await client.GetStringAsync("/lists")));
        Assert.Contains("Détachement Shield Host", WebUtility.HtmlDecode(await client.GetStringAsync(listUrl)));
    }

    [Fact]
    public async Task The_owner_can_set_the_detachment_of_a_list_imported_without_it()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        await client.SubmitAsync($"/lists/{listId}", "edit",
            new Dictionary<string, string> { ["Edit.Name"] = "Custodes", ["Edit.Detachment"] = "Talons of the Emperor" });

        Assert.Contains("Détachement Talons of the Emperor", WebUtility.HtmlDecode(await client.GetStringAsync($"/lists/{listId}")));
    }

    [Fact]
    public async Task The_owner_can_delete_their_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        var response = await client.SubmitAsync($"/lists/{listId}", "delete", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/lists/{listId}")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
