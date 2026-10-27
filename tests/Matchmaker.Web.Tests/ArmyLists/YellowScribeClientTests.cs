using System.Net;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.ArmyLists.YellowScribe;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.ArmyLists;

public class YellowScribeClientTests
{
    private static readonly string CustodesJson = Samples.CustodesJson;

    private sealed class FakeYellowScribe(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static (YellowScribeClient Client, FakeYellowScribe Server) ClientAnswering(
        HttpStatusCode status, string body = "")
    {
        var server = new FakeYellowScribe(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var http = new HttpClient(server) { BaseAddress = new Uri("https://yellowscribe.link/") };
        return (new YellowScribeClient(http), server);
    }

    [Fact]
    public async Task Fetches_the_roster_behind_a_code()
    {
        var (client, server) = ClientAnswering(HttpStatusCode.OK, CustodesJson);

        var roster = await client.FetchAsync("0c6cfa93");

        Assert.Equal("Adeptus Custodes", roster.Faction);
        Assert.Equal("https://yellowscribe.link/get_army_by_id?id=0c6cfa93", Assert.Single(server.Requests).ToString());
    }

    [Fact]
    public async Task The_code_is_trimmed_and_lower_cased()
    {
        var (client, server) = ClientAnswering(HttpStatusCode.OK, CustodesJson);

        await client.FetchAsync("  0C6CFA93 ");

        Assert.EndsWith("id=0c6cfa93", Assert.Single(server.Requests).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0c6cfa9")]
    [InlineData("0c6cfa933")]
    [InlineData("0c6cfa9z")]
    [InlineData("../admin")]
    public async Task An_invalid_code_is_refused_without_calling_yellowscribe(string code)
    {
        var (client, server) = ClientAnswering(HttpStatusCode.OK, CustodesJson);

        var error = await Assert.ThrowsAsync<RosterImportException>(() => client.FetchAsync(code));

        Assert.Equal(RosterImportError.InvalidCode, error.Error);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task An_unknown_or_expired_code_is_reported()
    {
        var (client, _) = ClientAnswering(HttpStatusCode.NotFound, "{ \"err\": \"Your roster code appears to have expired\" }");

        var error = await Assert.ThrowsAsync<RosterImportException>(() => client.FetchAsync("deadbeef"));

        Assert.Equal(RosterImportError.NotFound, error.Error);
    }

    [Fact]
    public async Task A_server_error_is_reported_as_unavailable()
    {
        var (client, _) = ClientAnswering(HttpStatusCode.BadGateway);

        var error = await Assert.ThrowsAsync<RosterImportException>(() => client.FetchAsync("deadbeef"));

        Assert.Equal(RosterImportError.Unavailable, error.Error);
    }

    [Fact]
    public async Task A_network_failure_is_reported_as_unavailable()
    {
        var server = new FakeYellowScribe(_ => throw new HttpRequestException("connexion refusée"));
        var client = new YellowScribeClient(new HttpClient(server) { BaseAddress = new Uri("https://yellowscribe.link/") });

        var error = await Assert.ThrowsAsync<RosterImportException>(() => client.FetchAsync("deadbeef"));

        Assert.Equal(RosterImportError.Unavailable, error.Error);
    }
}
