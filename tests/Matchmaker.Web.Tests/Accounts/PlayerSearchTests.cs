using Matchmaker.Domain.Players;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public class PlayerSearchTests(PostgresFixture postgres)
{
    private async Task<(PlayerId Id, string Name)> NewPlayer(string name)
    {
        var id = MatchmakerWebFactory.NewPlayerId();
        await using var db = postgres.CreateDbContext();
        await new PlayerAccounts(db).SignInAsync(id, name, null);
        return (id, name);
    }

    private async Task<IReadOnlyList<Player>> Search(string query)
    {
        await using var db = postgres.CreateDbContext();
        return await new PlayerAccounts(db).SearchAsync(query);
    }

    [Fact]
    public async Task Players_are_found_by_part_of_their_name_whatever_the_case()
    {
        var suffix = Random.Shared.Next(100000);
        var ultramarine = await NewPlayer($"Ultramarine{suffix}");
        await NewPlayer($"Necron{suffix}");

        var found = await Search($"MARINE{suffix}");

        Assert.Equal([ultramarine.Id], found.Select(p => p.Id));
    }

    [Fact]
    public async Task Wildcard_characters_are_searched_literally()
    {
        var suffix = Random.Shared.Next(100000);
        var percent = await NewPlayer($"100%Custodes{suffix}");
        await NewPlayer($"100xCustodes{suffix}");

        var found = await Search($"100%Custodes{suffix}");

        Assert.Equal([percent.Id], found.Select(p => p.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task A_query_needs_at_least_2_characters(string query)
    {
        Assert.Empty(await Search(query));
    }
}
