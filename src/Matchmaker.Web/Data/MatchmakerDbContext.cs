using System.Text.Json;
using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Tts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchmaker.Web.Data;

public sealed class MatchmakerDbContext(DbContextOptions<MatchmakerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<ArmyList> ArmyLists => Set<ArmyList>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<ArchivedArmyList> ArchivedArmyLists => Set<ArchivedArmyList>();
    public DbSet<ReportCode> ReportCodes => Set<ReportCode>();
    public DbSet<ScheduledSearch> ScheduledSearches => Set<ScheduledSearch>();

    private static readonly ValueConverter<PlayerId, string> PlayerIdConverter = new(id => id.SteamId, steamId => new PlayerId(steamId));
    private static readonly ValueConverter<EloRating, int> EloRatingConverter = new(rating => rating.Value, value => new EloRating(value));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(player =>
        {
            player.ToTable("players");
            player.HasKey(p => p.Id);
            player.Property(p => p.Id)
                .HasColumnName("steam_id")
                .HasMaxLength(20)
                .HasConversion(PlayerIdConverter);
            player.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(64);
            player.Property(p => p.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
            player.Property(p => p.Rating)
                .HasColumnName("rating")
                .HasConversion(EloRatingConverter);
            player.HasIndex(p => p.Rating);
            player.Property(p => p.RankedGamesPlayed).HasColumnName("ranked_games_played");
            // Jeton de concurrence (colonne système xmin de PostgreSQL) : l'Elo n'est jamais écrasé.
            player.Property<uint>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<ArmyList>(list =>
        {
            list.ToTable("army_lists");
            list.HasKey(l => l.Id);
            list.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            list.Property(l => l.Owner)
                .HasColumnName("owner_steam_id")
                .HasMaxLength(20)
                .HasConversion(PlayerIdConverter);
            list.HasOne<Player>().WithMany().HasForeignKey(l => l.Owner).OnDelete(DeleteBehavior.Cascade);
            list.HasIndex(l => l.Owner);
            list.Property(l => l.Name).HasColumnName("name").HasMaxLength(ArmyList.NameMaxLength);
            list.Property(l => l.Detachment).HasColumnName("detachment").HasMaxLength(ArmyList.DetachmentMaxLength);
            list.Property(l => l.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
            list.Property(l => l.IsFavorite).HasColumnName("is_favorite");
            list.ComplexProperty(l => l.Format, MapFormat);
            MapRoster(list.Property(l => l.Roster));
            list.Property(l => l.ImportedAt).HasColumnName("imported_at");
        });

        modelBuilder.Entity<ReportCode>(code =>
        {
            code.ToTable("match_report_codes");
            code.HasKey(c => c.MatchId);
            code.Property(c => c.MatchId).HasColumnName("match_id").ValueGeneratedNever();
            code.Property(c => c.Code).HasColumnName("code").HasMaxLength(ReportCode.Length);
            code.HasIndex(c => c.Code).IsUnique();
        });

        // Copies figées des listes jouées : indépendantes des listes d'origine (pas de clé étrangère).
        modelBuilder.Entity<ArchivedArmyList>(archive =>
        {
            archive.ToTable("archived_army_lists");
            archive.HasKey(a => a.Id);
            archive.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            archive.Property(a => a.MatchId).HasColumnName("match_id");
            archive.HasIndex(a => a.MatchId);
            archive.Property(a => a.OriginalListId).HasColumnName("original_list_id");
            archive.Property(a => a.Owner).HasColumnName("owner_steam_id").HasMaxLength(20).HasConversion(PlayerIdConverter);
            archive.Property(a => a.Name).HasColumnName("name").HasMaxLength(ArmyList.NameMaxLength);
            archive.Property(a => a.Detachment).HasColumnName("detachment").HasMaxLength(ArmyList.DetachmentMaxLength);
            archive.Property(a => a.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
            archive.ComplexProperty(a => a.Format, MapFormat);
            MapRoster(archive.Property(a => a.Roster));
            archive.Property(a => a.ArchivedAt).HasColumnName("archived_at");
        });

        modelBuilder.Entity<ScheduledSearch>(search =>
        {
            search.ToTable("scheduled_searches");
            search.HasKey(s => s.Id);
            search.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            search.ComplexProperty(s => s.Ticket, ticket => MapTicket(ticket, "player"));
            search.Property(s => s.PlannedFor).HasColumnName("planned_for");
            search.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            search.Property(s => s.MatchId).HasColumnName("match_id");
            search.HasIndex(s => new { s.Status, s.PlannedFor });
            search.HasIndex(s => s.MatchId);
            search.Property<uint>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<Match>(match =>
        {
            match.ToTable("matches");
            match.HasKey(m => m.Id);
            match.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            match.ComplexProperty(m => m.First, ticket => MapTicket(ticket, "first"));
            match.ComplexProperty(m => m.Second, ticket => MapTicket(ticket, "second"));
            match.Property(m => m.ProposedAt).HasColumnName("proposed_at");
            match.Property(m => m.ScheduledFor).HasColumnName("scheduled_for");
            match.Property(m => m.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            match.Property(m => m.Declarer).HasColumnName("declarer_steam_id").HasMaxLength(20).HasConversion(PlayerIdConverter);
            match.Property(m => m.DeclaredAt).HasColumnName("declared_at");
            match.Property(m => m.ConfirmedAutomatically).HasColumnName("confirmed_automatically");
            match.Property(m => m.Contestations).HasColumnName("contestations");
            match.Property(m => m.AbandonedBy).HasColumnName("abandoned_by_steam_id").HasMaxLength(20).HasConversion(PlayerIdConverter);
            foreach (var side in new[] { "first", "second" })
            {
                var prefix = "_" + side;
                match.Property<bool>($"{prefix}Accepted").HasColumnName($"{side}_accepted");
                match.Property<int?>($"{prefix}VictoryPoints").HasColumnName($"{side}_victory_points");
                match.Property<int?>($"{prefix}RatingChange").HasColumnName($"{side}_rating_change");
                match.Property<int?>($"{prefix}RatingBefore").HasColumnName($"{side}_rating_before");
            }
            match.HasIndex(m => m.Status);
            match.HasIndex(m => m.ScheduledFor);
            match.Property<uint>("RowVersion").IsRowVersion();
        });
    }

    private static void MapFormat(ComplexPropertyBuilder<GameFormat> format)
    {
        format.Property(f => f.Game).HasColumnName("game").HasConversion<string>().HasMaxLength(32);
        format.Property(f => f.Name).HasColumnName("format").HasMaxLength(32);
    }

    private static void MapRoster(PropertyBuilder<Roster> roster) =>
        roster
            .HasColumnName("roster")
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, RosterJson),
                json => JsonSerializer.Deserialize<Roster>(json, RosterJson)!);

    private static void MapTicket(ComplexPropertyBuilder<QueueTicket> ticket, string side)
    {
        ticket.Property(t => t.Player).HasColumnName($"{side}_steam_id").HasMaxLength(20).HasConversion(PlayerIdConverter);
        ticket.Property(t => t.ArmyListId).HasColumnName($"{side}_army_list_id");
        ticket.ComplexProperty(t => t.Format, format =>
        {
            format.Property(f => f.Game).HasColumnName($"{side}_game").HasConversion<string>().HasMaxLength(32);
            format.Property(f => f.Name).HasColumnName($"{side}_format").HasMaxLength(32);
        });
        ticket.Property(t => t.Ranked).HasColumnName($"{side}_ranked");
        ticket.Property(t => t.Rating).HasColumnName($"{side}_rating").HasConversion(EloRatingConverter);
        ticket.Property(t => t.EnqueuedAt).HasColumnName($"{side}_enqueued_at");
    }

    // Les propriétés calculées (faction, IsMelee) ne sont pas stockées.
    private static readonly JsonSerializerOptions RosterJson = new() { IgnoreReadOnlyProperties = true };
}
