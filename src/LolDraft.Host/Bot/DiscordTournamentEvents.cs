using Discord;
using Discord.WebSocket;
using LolDraft.Application.Tournaments;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot;

/// <summary>
/// Mantém as mensagens do Discord (painel de inscrição e quadro do draft) sincronizadas
/// com o estado do campeonato, venha a mudança do Discord, do painel web ou do timer.
/// </summary>
public class DiscordTournamentEvents(DiscordSocketClient client, DraftNotifier notifier, ILogger<DiscordTournamentEvents> logger) : ITournamentEvents
{
    /// <summary>Canal pelo cache do gateway; se não estiver em cache, busca via REST.</summary>
    private async Task<IMessageChannel?> ChannelAsync(Tournament t)
    {
        if (t.AnnouncementChannelId is not { } id) return null;
        if (client.GetChannel(id) is IMessageChannel cached) return cached;
        try { return await client.Rest.GetChannelAsync(id) as IMessageChannel; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não consegui acessar o canal {Channel} do campeonato {Id}", id, t.Id);
            return null;
        }
    }

    public async Task TournamentChangedAsync(Tournament t, string reason)
    {
        await RefreshRegistrationAsync(t);
        if (t.Status is TournamentStatus.Drafting or TournamentStatus.Drafted or TournamentStatus.Cancelled)
            await RefreshDraftBoardAsync(t);
    }

    public async Task PickMadeAsync(Tournament t, DraftPick pick)
    {
        await RefreshDraftBoardAsync(t);
        notifier.NotifyPicked(t, pick);

        var channel = await ChannelAsync(t);
        if (channel == null) return;

        var entry = t.Entries.FirstOrDefault(e => e.Id == pick.EntryId);
        var team = t.Teams.FirstOrDefault(x => x.Id == pick.TeamId);
        if (entry == null || team == null) return;

        var role = entry.AssignedRole is { } r ? $" ({r.Emoji()} {r.Label()})" : "";
        var text = $"✅ Pick {pick.PickNumber}: **{team.Name}** escolheu {entry.Player.Tag()}{role}" +
                   (pick.AutoPicked ? " · 🎲 auto-pick" : "");

        // Avisa o próximo capitão.
        var next = t.CurrentTeam;
        if (next != null && t.Status == TournamentStatus.Drafting)
        {
            var captain = t.Entries.FirstOrDefault(e => e.Id == next.CaptainEntryId);
            if (captain != null) text += $"\n⏰ Vez de {captain.Player.Tag()} (**{next.Name}**)";
        }

        await SafeAsync(() => channel.SendMessageAsync(text), "aviso de pick");
    }

    public async Task DraftCompletedAsync(Tournament t)
    {
        await RefreshDraftBoardAsync(t);
        notifier.NotifyTeams(t);
        var channel = await ChannelAsync(t);
        if (channel == null) return;
        await SafeAsync(() => channel.SendMessageAsync("🏁 **Draft encerrado!**",
            embed: TournamentUi.TeamsSummaryEmbed(t), allowedMentions: AllowedMentions.None), "resumo dos times");
    }

    public async Task MatchUpdatedAsync(Tournament t, Match match)
    {
        if (!match.IsFinished) return; // jogo a jogo, o próprio cartão é atualizado
        var channel = await ChannelAsync(t);
        if (channel == null) return;

        var winner = match.Winner;
        var roleMention = winner?.DiscordRoleId is { } rid ? $"<@&{rid}>" : $"**{winner?.Name}**";
        await SafeAsync(() => channel.SendMessageAsync(
            $"🏅 {roleMention} venceu a partida #{match.Number} ({match.RoundName}) por **{Math.Max(match.WinsA, match.WinsB)} x {Math.Min(match.WinsA, match.WinsB)}**!",
            allowedMentions: AllowedMentions.None), "resultado");

        if (t.Status == TournamentStatus.Finished)
        {
            var champRole = t.Champion?.DiscordRoleId;
            await SafeAsync(() => channel.SendMessageAsync(
                champRole is { } c ? $"🎉 <@&{c}>" : "🎉",
                embed: BracketUi.ChampionEmbed(t),
                allowedMentions: champRole is { } r ? new AllowedMentions { RoleIds = [r] } : AllowedMentions.None), "campeão");
            await SafeAsync(() => channel.SendMessageAsync(embed: BracketUi.BracketEmbed(t), allowedMentions: AllowedMentions.None), "chave final");
            return;
        }

        // Próxima partida ficou pronta? Posta o cartão dela.
        var next = t.Matches.FirstOrDefault(m => m.Id == match.NextMatchId || m == match.NextMatch);
        if (next is { IsReady: true })
        {
            var pings = new[] { next.TeamA, next.TeamB }.Where(x => x?.DiscordRoleId != null).Select(x => x!.DiscordRoleId!.Value).ToList();
            await SafeAsync(() => channel.SendMessageAsync(
                pings.Count > 0 ? string.Join(" ", pings.Select(p => $"<@&{p}>")) + " vocês se enfrentam!" : null,
                embed: BracketUi.MatchEmbed(t, next), components: BracketUi.MatchComponents(t, next),
                allowedMentions: new AllowedMentions { RoleIds = pings }), "próxima partida");
        }
    }

    private async Task RefreshRegistrationAsync(Tournament t)
    {
        if (await ChannelAsync(t) is not { } channel || t.RegistrationMessageId is not { } msgId) return;
        await SafeAsync(() => channel.ModifyMessageAsync(msgId, m =>
        {
            m.Embed = TournamentUi.RegistrationEmbed(t);
            m.Components = TournamentUi.RegistrationComponents(t);
            m.AllowedMentions = AllowedMentions.None;
        }), "painel de inscrição");
    }

    private async Task RefreshDraftBoardAsync(Tournament t)
    {
        if (await ChannelAsync(t) is not { } channel || t.DraftMessageId is not { } msgId) return;
        await SafeAsync(() => channel.ModifyMessageAsync(msgId, m =>
        {
            m.Embed = TournamentUi.DraftBoardEmbed(t);
            m.Components = TournamentUi.DraftComponents(t);
            m.AllowedMentions = AllowedMentions.None;
        }), "quadro do draft");
    }

    private async Task SafeAsync(Func<Task> action, string what)
    {
        try { await action(); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao atualizar {What} no Discord", what); }
    }
}
