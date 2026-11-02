using System.Net;
using Matchmaker.Domain.ArmyLists;
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

    private static Dictionary<string, string> ImportForm(string code, string name = "Custodes 2000", string format = "Strike Force", string detachment = "", string mfmVersion = "", string forceDisposition = "") =>
        new() { ["Import.Code"] = code, ["Import.Name"] = name, ["Import.Format"] = format, ["Import.Detachment"] = detachment, ["Import.MfmVersion"] = mfmVersion, ["Import.ForceDisposition"] = forceDisposition };

    [Fact]
    public async Task My_lists_require_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/lists");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task My_lists_show_my_imported_lists_and_only_a_button_to_import()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me, "Mes Custodes");

        var html = await client.GetStringAsync("/lists");

        Assert.Contains("Mes Custodes", html);
        Assert.Contains("Adeptus Custodes", html);
        Assert.Contains("href=\"/lists?import=1\"", html);
        Assert.DoesNotContain("Code YellowScribe", html);
    }

    [Fact]
    public async Task A_player_without_a_list_can_still_open_the_import()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/lists");

        Assert.Contains("href=\"/lists?import=1\"", html);
    }

    [Fact]
    public async Task The_import_button_opens_a_modal_asking_for_the_list_and_its_points()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me, "Mes Custodes");

        var html = await client.GetStringAsync("/lists?import=1");

        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("Code YellowScribe", html);
        Assert.Contains(">2000 pts</option>", html);
        Assert.DoesNotContain(">Strike Force", html);
        Assert.Contains("Mes Custodes", html);
    }

    [Fact]
    public async Task A_refused_import_stays_in_the_modal()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm("deadbeef"));

        Assert.Contains("role=\"dialog\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Marking_a_favorite_does_not_open_the_import()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        var response = await client.SubmitAsync("/lists", "favorite", new Dictionary<string, string> { ["FavoriteListId"] = listId.ToString() });

        Assert.DoesNotContain("role=\"dialog\"", await response.Content.ReadAsStringAsync());
    }

    private async Task ImportList(PlayerId owner, string name, GameFormat format, string? detachment = null)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ArmyListService>().ImportAsync(owner, Samples.CustodesCode, name, format, detachment);
    }

    private static void AssertInOrder(string html, params string[] names)
    {
        var positions = names.Select(name => html.IndexOf(name, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task My_lists_can_be_sorted_by_name_in_both_directions()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportList(me, "Bravo", GameFormats.StrikeForce);
        await ImportList(me, "Écarlate", GameFormats.StrikeForce);
        await ImportList(me, "alpha", GameFormats.StrikeForce);
        await ImportList(me, "Zélotes", GameFormats.StrikeForce);

        AssertInOrder(await client.GetStringAsync("/lists?sort=name"), "alpha", "Bravo", "Écarlate", "Zélotes");
        AssertInOrder(await client.GetStringAsync("/lists?sort=name&dir=desc"), "Zélotes", "Écarlate", "Bravo", "alpha");
    }

    [Fact]
    public async Task The_format_column_is_sorted_by_points()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportList(me, "Liste trois", GameFormats.Onslaught);
        await ImportList(me, "Liste une", GameFormats.Incursion);
        await ImportList(me, "Liste deux", GameFormats.StrikeForce);

        AssertInOrder(await client.GetStringAsync("/lists?sort=format"), "Liste une", "Liste deux", "Liste trois");
    }

    [Fact]
    public async Task Lists_without_a_detachment_stay_last_in_both_directions()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportList(me, "Sans détachement", GameFormats.StrikeForce);
        await ImportList(me, "Liste A", GameFormats.StrikeForce, "Aegis");
        await ImportList(me, "Liste Z", GameFormats.StrikeForce, "Zenith");

        AssertInOrder(WebUtility.HtmlDecode(await client.GetStringAsync("/lists?sort=detachment")), "Liste A", "Liste Z", "Sans détachement");
        AssertInOrder(WebUtility.HtmlDecode(await client.GetStringAsync("/lists?sort=detachment&dir=desc")), "Liste Z", "Liste A", "Sans détachement");
    }

    [Fact]
    public async Task An_unknown_sort_falls_back_on_the_default_order()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me);

        var response = await client.GetAsync("/lists?sort=n-importe-quoi&dir=n-importe-quoi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_sorted_column_is_marked_and_its_header_reverses_the_order_keeping_the_search()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me, "Custodes tournoi");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/lists?q=tournoi&sort=name"));

        Assert.Contains("aria-sort=\"ascending\"", html);
        Assert.Contains("href=\"/lists?sort=name&dir=desc&q=tournoi\"", html);
        Assert.Contains("href=\"/lists?sort=units&dir=asc&q=tournoi\"", html);
    }

    [Fact]
    public async Task By_default_the_date_is_the_sorted_column_and_its_header_shows_the_oldest_first()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/lists"));

        Assert.Contains("aria-sort=\"descending\"", html);
        Assert.Contains("href=\"/lists?sort=imported&dir=asc\"", html);
    }

    [Fact]
    public async Task A_new_search_keeps_the_sort()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me);

        var html = await client.GetStringAsync("/lists?sort=name&dir=desc");

        Assert.Contains("type=\"hidden\" name=\"sort\" value=\"name\"", html);
        Assert.Contains("type=\"hidden\" name=\"dir\" value=\"desc\"", html);
    }

    [Fact]
    public async Task Marking_a_favorite_keeps_the_sort()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me, "Alpha");
        await ImportCustodes(me, "Zélotes");

        var response = await client.SubmitAsync("/lists?sort=name&dir=desc", "favorite", new Dictionary<string, string> { ["FavoriteListId"] = listId.ToString() });

        AssertInOrder(await response.Content.ReadAsStringAsync(), "Zélotes", "Alpha");
    }

    [Fact]
    public async Task My_lists_can_be_filtered_by_name()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        await ImportCustodes(me, "Custodes tournoi");
        await ImportCustodes(me, "Liste du club");

        var html = await client.GetStringAsync("/lists?q=TOURNOI");

        Assert.Contains("Custodes tournoi", html);
        Assert.DoesNotContain("Liste du club", html);
        Assert.Contains("value=\"TOURNOI\"", html);
    }

    [Fact]
    public async Task A_list_is_marked_as_favorite_from_my_lists()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        await client.SubmitAsync("/lists", "favorite", new Dictionary<string, string> { ["FavoriteListId"] = listId.ToString() });

        using var scope = _factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArmyListService>().FindAsync(listId))!.IsFavorite);
        Assert.Contains("Retirer des favoris", await client.GetStringAsync("/lists"));
    }

    [Fact]
    public async Task Importing_a_valid_code_stores_the_list_and_opens_it()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, "Nouvelle liste"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches("^/lists/[0-9a-f-]{36}$", response.Headers.Location!.AbsolutePath);
        using var scope = _factory.Services.CreateScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me));
        Assert.Equal("Nouvelle liste", stored.Name);
        Assert.Equal(GameFormats.StrikeForce, stored.Format);
    }

    [Fact]
    public async Task A_long_name_of_512_characters_is_accepted_when_importing()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var longName = new string('x', 512);

        await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, longName));

        using var scope = _factory.Services.CreateScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me));
        Assert.Equal(longName, stored.Name);
    }

    [Fact]
    public async Task The_name_is_optional_when_importing()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, name: ""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me));
        Assert.Equal("Adeptus Custodes – 2000 pts", stored.Name);
    }

    [Fact]
    public async Task An_expired_code_is_explained_to_the_player()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm("deadbeef"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("inconnu ou expiré", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_malformed_code_is_refused_by_the_form()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm("pas-un-code"));

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

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, "Avec détachement", detachment: "Shield Host"));

        var listUrl = response.Headers.Location!.AbsolutePath;
        Assert.Contains("Shield Host", WebUtility.HtmlDecode(await client.GetStringAsync("/lists")));
        Assert.Contains("Détachement Shield Host", WebUtility.HtmlDecode(await client.GetStringAsync(listUrl)));
    }

    [Fact]
    public async Task The_mfm_version_given_at_import_is_shown_on_the_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        Assert.Contains("Version du MFM", await client.GetStringAsync("/lists?import=1"));

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, mfmVersion: "3.4"));

        Assert.Contains("· MFM 3.4 ·", WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.AbsolutePath)));
    }

    [Fact]
    public async Task The_force_disposition_given_at_import_is_shown_on_the_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        Assert.Contains("Force disposition", await client.GetStringAsync("/lists?import=1"));

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode, forceDisposition: "Vanguard"));

        Assert.Contains("· Force disposition Vanguard ·", WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.AbsolutePath)));
    }

    [Fact]
    public async Task A_list_imported_without_force_disposition_does_not_mention_it()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode));

        Assert.DoesNotContain("· Force disposition", WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.AbsolutePath)));
    }

    [Fact]
    public async Task The_owner_can_change_the_force_disposition_of_a_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        await client.SubmitAsync($"/lists/{listId}", "edit",
            new Dictionary<string, string> { ["Edit.Name"] = "Custodes", ["Edit.ForceDisposition"] = "Vanguard" });

        Assert.Contains("· Force disposition Vanguard ·", WebUtility.HtmlDecode(await client.GetStringAsync($"/lists/{listId}")));
    }

    [Fact]
    public async Task A_list_imported_without_mfm_version_does_not_mention_it()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);

        var response = await client.SubmitAsync("/lists?import=1", "import", ImportForm(Samples.CustodesCode));

        Assert.DoesNotContain("· MFM", WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.AbsolutePath)));
    }

    [Fact]
    public async Task The_owner_can_change_the_mfm_version_of_a_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);

        await client.SubmitAsync($"/lists/{listId}", "edit",
            new Dictionary<string, string> { ["Edit.Name"] = "Custodes", ["Edit.MfmVersion"] = "3.5" });

        Assert.Contains("· MFM 3.5 ·", WebUtility.HtmlDecode(await client.GetStringAsync($"/lists/{listId}")));
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
    public async Task The_import_form_offers_the_three_list_types_with_hybrid_by_default()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/lists?import=1"));

        Assert.Contains("Type de liste", html);
        var offered = System.Text.RegularExpressions.Regex.Matches(html, "<option value=\"(Team|Solo|Hybrid)\"[^>]*>([^<]+)</option>")
            .Select(m => m.Groups[2].Value);
        Assert.Equal(["Liste d'équipe", "Liste solo", "Liste mixte"], offered);
        Assert.Matches("<option value=\"Hybrid\" selected", html);
    }

    [Fact]
    public async Task A_list_imported_as_a_team_list_is_shown_as_such()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var form = ImportForm(Samples.CustodesCode, "Custodes ETC");
        form["Import.Type"] = "Team";

        var response = await client.SubmitAsync("/lists?import=1", "import", form);

        Assert.Contains("· Liste d'équipe ·", WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.AbsolutePath)));
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(ArmyListType.Team, Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(me)).Type);
    }

    [Fact]
    public async Task The_owner_can_change_the_type_of_a_list()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        var listId = await ImportCustodes(me);
        Assert.Contains("· Liste mixte ·", WebUtility.HtmlDecode(await client.GetStringAsync($"/lists/{listId}")));

        await client.SubmitAsync($"/lists/{listId}", "edit",
            new Dictionary<string, string> { ["Edit.Name"] = "Custodes", ["Edit.Type"] = "Solo" });

        Assert.Contains("· Liste solo ·", WebUtility.HtmlDecode(await client.GetStringAsync($"/lists/{listId}")));
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
