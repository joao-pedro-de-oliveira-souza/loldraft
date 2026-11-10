using Discord;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot;

public static class BracketUi
{
    public const string WinPrefix = "m:win:";   // m:win:{tid}:{match}:{teamId}
    public const string CallPrefix = "m:call:"; // m:call:{tid}:{match}

    private static string TeamName(Team? team) => team?.Name ?? "*a definir*";

    private static string RoleMention(Team? team) =>
        team?.DiscordRoleId is { } id ? $"<@&{id}>" : $"**{TeamName(team)}**";

    public static string MatchLine(Match m)
    {
        if (m.IsBye) return $"`#{m.Number}` **{TeamName(m.Winner)}** avança direto";

        var icon = m.IsFinished ? "✅" : m.IsReady ? "⚔️" : "⏳";
        string Side(Team? team, int wins) =>
            m.Winner != null && team != null && m.Winner.Id == team.Id ? $"**{TeamName(team)}** 🏅" : TeamName(team);

        return $"{icon} `#{m.Number}` {Side(m.TeamA, m.WinsA)} **{m.WinsA} x {m.WinsB}** {Side(m.TeamB, m.WinsB)} · {m.Format.Label()}";
    }

    public static Embed BracketEmbed(Tournament t)
    {
        var eb = new EmbedBuilder()
            .WithColor(t.Status == TournamentStatus.Finished ? Ui.Ok : Ui.Brand)
            .WithTitle($"🗂️ Chave · {t.Name}");

        if (t.Matches.Count == 0)
        {
            eb.WithDescription("A chave ainda não foi gerada. Use `!camp chave`.");
            return eb.Build();
        }

        foreach (var round in t.Matches.GroupBy(m => m.Round).OrderBy(g => g.Key))
        {
            var first = round.First();
            var lines = string.Join("\n", round.OrderBy(m => m.Position).Select(MatchLine));
            eb.AddField($"{first.RoundName} ({round.Count(m => !m.IsBye)} partida(s))",
                lines.Length > 1024 ? lines[..1020] + "…" : lines);
        }

        if (t.Champion is { } champ && t.Status == TournamentStatus.Finished)
            eb.WithDescription($"🏆 **Campeão: {champ.Name}**");
        else
            eb.WithDescription("⚔️ pronta · ⏳ aguardando · ✅ encerrada\nUse `!camp partida <número>` para o cartão com os botões de resultado.");

        return eb.Build();
    }

    public static Embed MatchEmbed(Tournament t, Match m)
    {
        var eb = new EmbedBuilder()
            .WithColor(m.IsFinished ? Ui.Ok : Ui.Brand)
            .WithTitle($"⚔️ Partida #{m.Number} · {m.RoundName} · {m.Format.Label()}")
            .WithDescription($"{RoleMention(m.TeamA)} **{m.WinsA} x {m.WinsB}** {RoleMention(m.TeamB)}");

        if (m.TeamA != null) eb.AddField(m.TeamA.Name, TournamentUi.TeamRoster(t, m.TeamA), inline: true);
        if (m.TeamB != null) eb.AddField(m.TeamB.Name, TournamentUi.TeamRoster(t, m.TeamB), inline: true);

        eb.WithFooter(m.IsFinished
            ? $"🏅 {m.Winner?.Name} venceu a série"
            : $"Primeiro a {m.Format.WinsNeeded()} vitória(s). Capitães ou admins registram cada jogo.");
        return eb.Build();
    }

    public static MessageComponent MatchComponents(Tournament t, Match m)
    {
        var cb = new ComponentBuilder();
        if (!m.IsReady || m.TeamA == null || m.TeamB == null) return cb.Build();

        cb.WithButton($"{Trim(m.TeamA.Name)} venceu o jogo", $"{WinPrefix}{t.Id}:{m.Number}:{m.TeamA.Id}", ButtonStyle.Success, new Emoji("🏆"));
        cb.WithButton($"{Trim(m.TeamB.Name)} venceu o jogo", $"{WinPrefix}{t.Id}:{m.Number}:{m.TeamB.Id}", ButtonStyle.Success, new Emoji("🏆"));
        cb.WithButton("Chamar para as calls", $"{CallPrefix}{t.Id}:{m.Number}", ButtonStyle.Secondary, new Emoji("🔊"), row: 1);
        return cb.Build();
    }

    public static Embed ChampionEmbed(Tournament t)
    {
        var champ = t.Champion;
        var eb = new EmbedBuilder()
            .WithColor(new Color(0xF5, 0xC5, 0x18))
            .WithTitle($"🏆 {champ?.Name} é campeão do {t.Name}!");
        if (champ != null) eb.WithDescription(TournamentUi.TeamRoster(t, champ));
        eb.WithFooter("GG a todos! Use !camp encerrar para apagar cargos e calls do campeonato.");
        return eb.Build();
    }

    private static string Trim(string s) => s.Length <= 30 ? s : s[..30];
}
