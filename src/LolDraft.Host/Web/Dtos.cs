using Discord.WebSocket;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Web;

// Ids do Discord vão como string: JavaScript perde precisão acima de 2^53.

public record PlayerDto(
    int Id, string DiscordId, string Name, string? AvatarUrl,
    string PrimaryRole, string? SecondaryRole, bool Fill,
    string? RiotId, bool RiotVerified, string? Elo, int? Rating, bool IsFake);

public record EntryDto(
    int Id, PlayerDto Player, bool IsCaptain, bool CheckedIn, int? TeamId, string? AssignedRole);

public record TeamDto(
    int Id, string Name, int DraftPosition, string Color, int CaptainEntryId,
    int[] MemberEntryIds, string[] MissingRoles, bool HasDiscordRole, string? AvgElo);

public record PickDto(int PickNumber, int Round, int TeamId, int EntryId, bool Auto, DateTimeOffset At);

public record MatchDto(
    int Number, int Round, int TotalRounds, string RoundName, int Position, string Format, int WinsNeeded,
    int? TeamAId, int? TeamBId, int WinsA, int WinsB, int? WinnerTeamId, bool IsBye, bool IsReady, bool IsFinished);

public record TournamentDto(
    int Id, string Name, string Status,
    int TeamCount, int TeamSize, string DefaultFormat, string? SemifinalFormat, string? FinalFormat,
    int PickTimerSeconds, bool EnforceRoles, bool RequireCheckIn,
    DateTimeOffset? CurrentPickDeadline, DateTimeOffset ServerNow,
    int PickNumber, int TotalPicks, int CurrentRound, int? CurrentTeamId,
    EntryDto[] Entries, TeamDto[] Teams, PickDto[] Picks, MatchDto[] Matches,
    int[] EligibleEntryIds, int? ChampionTeamId);

public record TournamentSummaryDto(int Id, string Name, string Status, int Entries, int TeamCount, DateTimeOffset CreatedAt);

public record MeDto(
    bool LoginEnabled, bool Authenticated, string? DiscordId, string? Name, string? AvatarUrl,
    bool IsAdmin, PlayerDto? Player);

/// <summary>Converte o domínio para DTOs, usando o cache do bot para avatares.</summary>
public class DtoMapper(DiscordSocketClient client, Microsoft.Extensions.Options.IOptions<Bot.DiscordOptions> options)
{
    private SocketGuild? Guild => client.GetGuild(options.Value.GuildId);

    public string? AvatarFor(ulong discordId) =>
        discordId < LolDraft.Domain.Players.Player.FakeIdLimit ? null : Guild?.GetUser(discordId)?.GetDisplayAvatarUrl(size: 128);

    public PlayerDto Player(Player p) => new(
        p.Id, p.DiscordUserId.ToString(), p.DisplayName, AvatarFor(p.DiscordUserId),
        p.PrimaryRole.ToString(), p.SecondaryRole?.ToString(), p.IsFill,
        p.Riot?.RiotId, p.Riot?.Verified ?? false,
        p.Riot?.Tier is { } tier ? $"{char.ToUpperInvariant(tier[0])}{tier[1..].ToLowerInvariant()} {p.Riot.Division}".Trim() : null,
        p.RatingScore, p.IsFake);

    public TournamentDto Tournament(Tournament t)
    {
        var current = t.CurrentTeam;
        return new TournamentDto(
            t.Id, t.Name, t.Status.ToString(),
            t.TeamCount, t.TeamSize, t.DefaultFormat.Label(), t.SemifinalFormat?.Label(), t.FinalFormat?.Label(),
            t.PickTimerSeconds, t.EnforceRoles, t.RequireCheckIn,
            t.Status == TournamentStatus.Drafting ? t.CurrentPickDeadline : null, DateTimeOffset.UtcNow,
            Math.Min(t.Picks.Count + 1, t.TotalPicks), t.TotalPicks, t.CurrentRound, current?.Id,
            t.Entries.OrderBy(e => e.JoinedAt).Select(e => new EntryDto(
                e.Id, Player(e.Player), e.IsCaptain, e.CheckedIn, e.TeamId ?? e.Team?.Id, e.AssignedRole?.ToString())).ToArray(),
            t.Teams.OrderBy(x => x.DraftPosition).Select(x => new TeamDto(
                x.Id, x.Name, x.DraftPosition, x.ColorHex, x.CaptainEntryId,
                x.Members.Select(m => m.Id).ToArray(),
                (t.Status is TournamentStatus.Drafting ? t.MissingRoles(x) : LolRoleExtensions.All
                    .Where(r => x.Members.All(m => m.AssignedRole != r)).ToList()).Select(r => r.ToString()).ToArray(),
                x.DiscordRoleId != null,
                t.TeamAverageRating(x) is { } avg ? EloScore.Label(avg) : null)).ToArray(),
            t.Picks.OrderBy(p => p.PickNumber).Select(p => new PickDto(p.PickNumber, p.Round, p.TeamId, p.EntryId, p.AutoPicked, p.PickedAt)).ToArray(),
            t.Matches.OrderBy(m => m.Number).Select(m => new MatchDto(
                m.Number, m.Round, m.TotalRounds, m.RoundName, m.Position, m.Format.Label(), m.Format.WinsNeeded(),
                m.TeamAId ?? m.TeamA?.Id, m.TeamBId ?? m.TeamB?.Id, m.WinsA, m.WinsB, m.WinnerTeamId ?? m.Winner?.Id,
                m.IsBye, m.IsReady, m.IsFinished)).ToArray(),
            current != null ? t.EligibleFor(current).Select(e => e.Id).ToArray() : [],
            t.Status == TournamentStatus.Finished ? t.Champion?.Id : null);
    }

    public TournamentSummaryDto Summary(Tournament t) =>
        new(t.Id, t.Name, t.Status.ToString(), t.Entries.Count, t.TeamCount, t.CreatedAt);
}
