using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Data;

public sealed class MatchmakerDbContext(DbContextOptions<MatchmakerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();

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
    }
}
