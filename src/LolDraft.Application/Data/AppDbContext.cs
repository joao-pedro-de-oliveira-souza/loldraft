using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LolDraft.Application.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentEntry> Entries => Set<TournamentEntry>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<DraftPick> Picks => Set<DraftPick>();
    public DbSet<Match> Matches => Set<Match>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Player>(e =>
        {
            e.HasIndex(p => p.DiscordUserId).IsUnique();
            e.Property(p => p.DisplayName).HasMaxLength(100);
            e.Ignore(p => p.RatingScore);
            e.Ignore(p => p.IsFake);
            e.OwnsOne(p => p.Riot, r =>
            {
                r.Property(x => x.GameName).HasMaxLength(32);
                r.Property(x => x.TagLine).HasMaxLength(8);
                r.Property(x => x.Puuid).HasMaxLength(100);
                r.Property(x => x.Tier).HasMaxLength(20);
                r.Property(x => x.Division).HasMaxLength(4);
                r.Ignore(x => x.RiotId);
                r.Ignore(x => x.RatingScore);
            });
        });

        b.Entity<Tournament>(e =>
        {
            e.HasIndex(t => new { t.GuildId, t.Status });
            e.Property(t => t.Name).HasMaxLength(100);

            e.HasMany(t => t.Entries).WithOne().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Teams).WithOne().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Picks).WithOne().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Matches).WithOne().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);

            // Propriedades calculadas do agregado.
            e.Ignore(t => t.ActiveEntries);
            e.Ignore(t => t.RequiredPlayers);
            e.Ignore(t => t.TotalPicks);
            e.Ignore(t => t.PickOrder);
            e.Ignore(t => t.IsDraftComplete);
            e.Ignore(t => t.CurrentTeam);
            e.Ignore(t => t.CurrentRound);
            e.Ignore(t => t.AvailablePool);
            e.Ignore(t => t.Champion);
            e.Ignore(t => t.ReadyMatches);
        });

        b.Entity<TournamentEntry>(e =>
        {
            e.HasIndex(x => new { x.TournamentId, x.PlayerId }).IsUnique();
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Team>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.Property(t => t.ColorHex).HasMaxLength(9);
        });

        b.Entity<DraftPick>(e => e.HasIndex(p => new { p.TournamentId, p.PickNumber }).IsUnique());

        b.Entity<Match>(e =>
        {
            e.HasIndex(m => new { m.TournamentId, m.Number }).IsUnique();
            e.HasOne(m => m.TeamA).WithMany().HasForeignKey(m => m.TeamAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.TeamB).WithMany().HasForeignKey(m => m.TeamBId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Winner).WithMany().HasForeignKey(m => m.WinnerTeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.NextMatch).WithMany().HasForeignKey(m => m.NextMatchId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(m => m.IsFinished);
            e.Ignore(m => m.IsReady);
            e.Ignore(m => m.RoundName);
            e.Ignore(m => m.ScoreLine);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // SQLite não ordena DateTimeOffset nativamente; guardar como ticks UTC resolve.
        // (vale também para DateTimeOffset?)
        c.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
