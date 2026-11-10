using LolDraft.Application.Tournaments;
using LolDraft.Domain.Tournaments;
using Microsoft.AspNetCore.SignalR;

namespace LolDraft.Host.Web;

/// <summary>Hub do painel: o navegador entra no grupo do campeonato e recebe o estado a cada mudança.</summary>
public class TournamentHub : Hub
{
    public static string Group(int tournamentId) => $"t:{tournamentId}";

    public Task Watch(int tournamentId) => Groups.AddToGroupAsync(Context.ConnectionId, Group(tournamentId));

    public Task Unwatch(int tournamentId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(tournamentId));
}

/// <summary>Empurra o estado completo para os navegadores (o DTO é pequeno: ~20 jogadores).</summary>
public class SignalRTournamentEvents(IHubContext<TournamentHub> hub, DtoMapper mapper, ILogger<SignalRTournamentEvents> logger)
    : ITournamentEvents
{
    private async Task PushAsync(Tournament t, string kind, string? message)
    {
        try
        {
            await hub.Clients.Group(TournamentHub.Group(t.Id)).SendAsync("tournamentUpdated", mapper.Tournament(t), kind, message);
            // Quem está na tela inicial ouve o canal geral (ex.: campeonato novo criado).
            await hub.Clients.All.SendAsync("tournamentsChanged", t.Id, t.Status.ToString());
        }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao enviar atualização em tempo real"); }
    }

    public Task TournamentChangedAsync(Tournament t, string reason) => PushAsync(t, reason, null);

    public Task PickMadeAsync(Tournament t, DraftPick pick)
    {
        var entry = t.Entries.FirstOrDefault(e => e.Id == pick.EntryId);
        var team = t.Teams.FirstOrDefault(x => x.Id == pick.TeamId);
        return PushAsync(t, "pick", entry != null && team != null
            ? $"{team.Name} escolheu {entry.Player.DisplayName}{(pick.AutoPicked ? " (auto)" : "")}" : null);
    }

    public Task DraftCompletedAsync(Tournament t) => PushAsync(t, "draft-completed", "Draft encerrado!");

    public Task MatchUpdatedAsync(Tournament t, Match match) =>
        PushAsync(t, "match", match.IsFinished ? $"{match.Winner?.Name} venceu a partida #{match.Number}" : null);
}

/// <summary>Repassa cada evento para Discord e painel web.</summary>
public class CompositeTournamentEvents(IEnumerable<ITournamentEvents> sinks) : ITournamentEvents
{
    private Task All(Func<ITournamentEvents, Task> f) => Task.WhenAll(sinks.Select(f));

    public Task TournamentChangedAsync(Tournament t, string reason) => All(s => s.TournamentChangedAsync(t, reason));
    public Task PickMadeAsync(Tournament t, DraftPick pick) => All(s => s.PickMadeAsync(t, pick));
    public Task DraftCompletedAsync(Tournament t) => All(s => s.DraftCompletedAsync(t));
    public Task MatchUpdatedAsync(Tournament t, Match match) => All(s => s.MatchUpdatedAsync(t, match));
}
