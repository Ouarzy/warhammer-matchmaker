using System.Text.Json;
using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Data;

public sealed class MatchmakerDbContext(DbContextOptions<MatchmakerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<ArmyList> ArmyLists => Set<ArmyList>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(player =>
        {
            player.ToTable("players");
            player.HasKey(p => p.Id);
            player.Property(p => p.Id)
                .HasColumnName("steam_id")
                .HasMaxLength(20)
                .HasConversion(id => id.SteamId, steamId => new PlayerId(steamId));
            player.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(64);
            player.Property(p => p.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
            player.Property(p => p.Rating)
                .HasColumnName("rating")
                .HasConversion(rating => rating.Value, value => new EloRating(value));
            player.Property(p => p.RankedGamesPlayed).HasColumnName("ranked_games_played");
        });

        modelBuilder.Entity<ArmyList>(list =>
        {
            list.ToTable("army_lists");
            list.HasKey(l => l.Id);
            list.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            list.Property(l => l.Owner)
                .HasColumnName("owner_steam_id")
                .HasMaxLength(20)
                .HasConversion(id => id.SteamId, steamId => new PlayerId(steamId));
            list.HasOne<Player>().WithMany().HasForeignKey(l => l.Owner).OnDelete(DeleteBehavior.Cascade);
            list.HasIndex(l => l.Owner);
            list.Property(l => l.Name).HasColumnName("name").HasMaxLength(ArmyList.NameMaxLength);
            list.ComplexProperty(l => l.Format, format =>
            {
                format.Property(f => f.Game).HasColumnName("game").HasConversion<string>().HasMaxLength(32);
                format.Property(f => f.Name).HasColumnName("format").HasMaxLength(32);
            });
            list.Property(l => l.Roster)
                .HasColumnName("roster")
                .HasColumnType("jsonb")
                .HasConversion(
                    roster => JsonSerializer.Serialize(roster, RosterJson),
                    json => JsonSerializer.Deserialize<Roster>(json, RosterJson)!);
            list.Property(l => l.ImportedAt).HasColumnName("imported_at");
        });
    }

    // Les propriétés calculées (faction, IsMelee) ne sont pas stockées.
    private static readonly JsonSerializerOptions RosterJson = new() { IgnoreReadOnlyProperties = true };
}
