using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Matchmaker.Web.Tests.Infrastructure;

/// <summary>Un PostgreSQL jetable (Docker) partagé par les tests web.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public MatchmakerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<MatchmakerDbContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
