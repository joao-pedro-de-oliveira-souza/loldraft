using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LolDraft.Application.Tournaments;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot.Modules;

/// <summary>Botões e menus do campeonato (fora do grupo para os custom ids ficarem simples).</summary>
public class TournamentComponentsModule(TournamentService tournaments) : InteractionModuleBase<SocketInteractionContext>
{
    private bool IsAdmin => (Context.User as SocketGuildUser)?.GuildPermissions.ManageGuild ?? false;

    [ComponentInteraction(TournamentUi.JoinPrefix + "*")]
    public async Task JoinAsync(int tournamentId)
    {
        var r = await tournaments.JoinAsync(tournamentId, Context.User.Id);
        await RespondAsync(r.Ok ? "✍️ Inscrição feita! Boa sorte." : $"❌ {r.Error}", ephemeral: true);
    }

    [ComponentInteraction(TournamentUi.LeavePrefix + "*")]
    public async Task LeaveAsync(int tournamentId)
    {
        var r = await tournaments.LeaveAsync(tournamentId, Context.User.Id);
        await RespondAsync(r.Ok ? "Você saiu do campeonato." : $"❌ {r.Error}", ephemeral: true);
    }

    [ComponentInteraction(TournamentUi.CheckInPrefix + "*")]
    public async Task CheckInAsync(int tournamentId)
    {
        var r = await tournaments.CheckInAsync(tournamentId, Context.User.Id);
        await RespondAsync(r.Ok ? "✅ Presença confirmada!" : $"❌ {r.Error}", ephemeral: true);
    }

    [ComponentInteraction(TournamentUi.PickPrefix + "*:*")]
    public async Task PickAsync(int tournamentId, int pickNumber, string[] values)
    {
        if (!int.TryParse(values.FirstOrDefault(), out var entryId))
        {
            await RespondAsync("Seleção inválida.", ephemeral: true);
            return;
        }
        await DoPickAsync(tournamentId, pickNumber, entryId, auto: false);
    }

    [ComponentInteraction(TournamentUi.AutoPrefix + "*:*")]
    public Task AutoAsync(int tournamentId, int pickNumber) => DoPickAsync(tournamentId, pickNumber, 0, auto: true);

    private async Task DoPickAsync(int tournamentId, int pickNumber, int entryId, bool auto)
    {
        var t = await tournaments.GetAsync(tournamentId);
        var team = t?.CurrentTeam;
        if (t == null || team == null)
        {
            await RespondAsync("O draft não está em andamento.", ephemeral: true);
            return;
        }
        if (t.Picks.Count + 1 != pickNumber)
        {
            await RespondAsync("Esse menu está desatualizado: o pick já foi feito. Use o quadro mais recente.", ephemeral: true);
            return;
        }
        if (!t.IsCaptainOf(team, Context.User.Id) && !IsAdmin)
        {
            await RespondAsync($"Só o capitão do **{team.Name}** (ou um admin) pode escolher agora.", ephemeral: true);
            return;
        }

        // Confirma a interação sem mensagem; o quadro é atualizado pelo evento de pick.
        await DeferAsync();
        var r = await tournaments.PickAsync(tournamentId, team.Id, entryId, auto);
        if (!r.Ok) await FollowupAsync($"❌ {r.Error}", ephemeral: true);
    }
}

/// <summary>Botões dos cartões de partida.</summary>
public class MatchComponentsModule(LolDraft.Application.Tournaments.TournamentService tournaments, DiscordResources resources)
    : InteractionModuleBase<SocketInteractionContext>
{
    private bool IsAdmin => (Context.User as SocketGuildUser)?.GuildPermissions.ManageGuild ?? false;

    [ComponentInteraction(BracketUi.WinPrefix + "*:*:*")]
    public async Task ReportWinAsync(int tournamentId, int matchNumber, int winnerTeamId)
    {
        var t = await tournaments.GetAsync(tournamentId);
        var m = t?.Matches.FirstOrDefault(x => x.Number == matchNumber);
        if (t == null || m == null) { await RespondAsync("Partida não encontrada.", ephemeral: true); return; }

        var isCaptain = (m.TeamAId is { } a && t.IsCaptainOfTeamId(a, Context.User.Id))
                        || (m.TeamBId is { } b && t.IsCaptainOfTeamId(b, Context.User.Id));
        if (!isCaptain && !IsAdmin)
        {
            await RespondAsync("Só os capitães dessa partida (ou um admin) registram resultado.", ephemeral: true);
            return;
        }

        await DeferAsync();
        var r = await tournaments.ReportGameAsync(tournamentId, matchNumber, winnerTeamId);
        if (!r.Ok) { await FollowupAsync($"❌ {r.Error}", ephemeral: true); return; }

        var updated = (await tournaments.GetAsync(tournamentId))!;
        var match = updated.Matches.First(x => x.Number == matchNumber);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = BracketUi.MatchEmbed(updated, match);
            x.Components = BracketUi.MatchComponents(updated, match);
            x.AllowedMentions = AllowedMentions.None;
        });

        if (!match.IsFinished)
        {
            var winner = updated.Teams.First(x => x.Id == winnerTeamId);
            await FollowupAsync($"🎮 Jogo {match.WinsA + match.WinsB}: **{winner.Name}** venceu · {BracketUi.MatchLine(match)} (registrado por {Context.User.Mention})",
                allowedMentions: AllowedMentions.None);
        }
    }

    [ComponentInteraction(BracketUi.CallPrefix + "*:*")]
    public async Task CallAsync(int tournamentId, int matchNumber)
    {
        var t = await tournaments.GetAsync(tournamentId);
        var m = t?.Matches.FirstOrDefault(x => x.Number == matchNumber);
        if (t == null || m?.TeamA == null || m.TeamB == null) { await RespondAsync("Partida não encontrada.", ephemeral: true); return; }

        var canMove = (Context.User as SocketGuildUser)?.GuildPermissions.MoveMembers ?? false;
        var isCaptain = t.IsCaptainOfTeamId(m.TeamA.Id, Context.User.Id) || t.IsCaptainOfTeamId(m.TeamB.Id, Context.User.Id);
        if (!canMove && !isCaptain) { await RespondAsync("Só capitães da partida ou quem pode mover membros.", ephemeral: true); return; }

        await DeferAsync(ephemeral: true);
        var log = await resources.MoveToTeamVoiceAsync(Context.Guild, t, [m.TeamA, m.TeamB]);
        await FollowupAsync(string.Join("\n", log), ephemeral: true);
    }
}
