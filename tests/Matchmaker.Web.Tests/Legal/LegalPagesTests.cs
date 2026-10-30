using System.Net;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Legal;

[Collection(PostgresCollection.Name)]
public sealed class LegalPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task<string> AnonymousText(string url)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_legal_notice_names_the_publisher_contact_and_host()
    {
        var html = await AnonymousText("/mentions-legales");

        Assert.Contains("emilien@pecoul.fr", html);
        Assert.Contains("Gandi SAS", html);
        Assert.Contains("63-65 boulevard Masséna", html);
        Assert.Contains("Games Workshop", html);
    }

    [Fact]
    public async Task The_privacy_policy_lists_the_data_kept_and_the_rights_of_players()
    {
        var html = await AnonymousText("/confidentialite");

        Assert.Contains("Steam ID", html);
        Assert.Contains("publi", html);
        Assert.Contains("cookie", html);
        Assert.Contains("CNIL", html);
        Assert.DoesNotContain("push", html);
        Assert.Contains("emilien@pecoul.fr", html);
    }

    [Fact]
    public async Task The_terms_of_use_set_the_rules_of_fair_play()
    {
        var html = await AnonymousText("/cgu");

        Assert.Contains("Conditions générales d'utilisation", html);
        Assert.Contains("fair-play", html);
        Assert.Contains("droit français", html);
        Assert.Contains("Munitorum Field Manual", html);
    }

    [Fact]
    public async Task The_about_page_explains_the_project_and_its_unofficial_status()
    {
        var html = await AnonymousText("/a-propos");

        Assert.Contains("À propos", html);
        Assert.Contains("non officiel", html);
        Assert.Contains("github.com/Ouarzy/warhammer-matchmaker", html);
    }

    [Fact]
    public async Task The_about_page_links_to_the_community_tools_and_credits_their_authors()
    {
        var html = await AnonymousText("/a-propos");

        Assert.Contains("href=\"https://github.com/Las0mbra/lct\"", html);
        Assert.Contains("href=\"https://yellowscribe.link\"", html);
        foreach (var author in new[] { "Las0mbra", "Hutber", "Yellow Man", "ThePants999", "Raikoh" })
            Assert.Contains(author, html);
        Assert.DoesNotContain("New Recruit", html);
    }

    [Fact]
    public async Task The_support_page_points_to_the_tipeee_page()
    {
        var html = await AnonymousText("/me-soutenir");

        Assert.Contains("Me soutenir", html);
        Assert.Contains("href=\"https://fr.tipeee.com/ouarzy/\"", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ranking")]
    public async Task Every_page_links_to_the_legal_pages(string url)
    {
        var html = await AnonymousText(url);

        foreach (var link in new[] { "/a-propos", "/me-soutenir", "/cgu", "/confidentialite", "/mentions-legales" })
            Assert.Contains($"href=\"{link}\"", html);
    }

    [Fact]
    public async Task Signing_in_means_accepting_the_terms_of_use()
    {
        var html = await AnonymousText("/");

        Assert.Contains("vous acceptez les", html);
    }

    public void Dispose() => _factory.Dispose();
}
