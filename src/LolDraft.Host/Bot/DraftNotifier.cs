using Discord;
using Discord.WebSocket;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Microsoft.Extensions.Options;

namespace LolDraft.Host.Bot;

/// <summary>
/// Avisa os jogadores por DM: quando são escolhidos no draft e quando os times fecham.
/// Liga/desliga com "Discord": { "DmNotifications": true }.
/// </summary>
public class DraftNotifier(DiscordSocketClient client, IOptions<DiscordOptions> options, ILogger<DraftNotifier> logger)
{
    private bool Enabled => options.Value.DmNotifications;

    /// <summary>Monta a mensagem de "você foi escolhido" (público para os testes).</summary>
    public static Embed PickedEmbed(Tournament t, Team team, TournamentEntry entry, DraftPick pick)
    {
        var captain = t.Entries.FirstOrDefault(e => e.Id == team.CaptainEntryId);
        var role = entry.AssignedRole is { } r ? Ui.RoleText(r) : "a definir";
        return new EmbedBuilder()
            .WithColor(Ui.ParseColor(team.ColorHex))
            .WithTitle($"🎉 Você foi escolhido para o {team.Name}!")
            .WithDescription($"Campeonato **{t.Name}**")
            .AddField("Capitão", captain?.Player.DisplayName ?? "—", inline: true)
            .AddField("Sua role", role, inline: true)
            .AddField("Pick", $"#{pick.PickNumber} de {t.TotalPicks}" + (pick.AutoPicked ? " (automático)" : ""), inline: true)
            .WithFooter("Quando os times fecharem, mando o elenco completo.")
            .Build();
    }

    /// <summary>Monta a mensagem de "times fechados" com o elenco do time do jogador.</summary>
    public static Embed TeamEmbed(Tournament t, Team team, TournamentEntry entry)
    {
        var lines = team.Members
            .OrderBy(m => m.AssignedRole ?? (LolRole)99)
            .Select(m => $"{(m.AssignedRole is { } r ? Ui.RoleText(r) : "❔")}: **{m.Player.DisplayName}**" +
                         (m.Id == team.CaptainEntryId ? " 👑" : "") + (m.Id == entry.Id ? " ← você" : ""));
        var avg = t.TeamAverageRating(team) is { } a ? $"\nMédia de elo: {EloScore.Label(a)}" : "";
        return new EmbedBuilder()
            .WithColor(Ui.ParseColor(team.ColorHex))
            .WithTitle($"🛡️ Times fechados: você joga no {team.Name}")
            .WithDescription($"Campeonato **{t.Name}**\n\n{string.Join("\n", lines)}{avg}")
            .WithFooter("Fique de olho no canal do campeonato para a chave e as calls.")
            .Build();
    }

    public void NotifyPicked(Tournament t, DraftPick pick)
    {
        if (!Enabled) return;
        var entry = t.Entries.FirstOrDefault(e => e.Id == pick.EntryId);
        var team = t.Teams.FirstOrDefault(x => x.Id == pick.TeamId);
        if (entry == null || team == null || entry.Player.IsFake) return;

        var embed = PickedEmbed(t, team, entry, pick);
        var userId = entry.Player.DiscordUserId;
        _ = Task.Run(() => SendAsync(userId, embed));
    }

    public void NotifyTeams(Tournament t)
    {
        if (!Enabled) return;
        var messages = t.Teams
            .SelectMany(team => team.Members.Where(m => !m.Player.IsFake).Select(m => (m.Player.DiscordUserId, Embed: TeamEmbed(t, team, m))))
            .ToList();

        // Em segundo plano e em sequência, para não segurar o draft nem estourar o limite de DMs.
        _ = Task.Run(async () =>
        {
            foreach (var (userId, embed) in messages)
            {
                await SendAsync(userId, embed);
                await Task.Delay(300);
            }
        });
    }

    private async Task SendAsync(ulong userId, Embed embed)
    {
        try
        {
            IUser? user = client.GetUser(userId);
            user ??= await client.Rest.GetUserAsync(userId);
            if (user == null) return;
            await user.SendMessageAsync(embed: embed);
        }
        catch (Discord.Net.HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            logger.LogInformation("Usuário {User} não aceita DMs do servidor; aviso não enviado.", userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar DM para {User}", userId);
        }
    }
}
