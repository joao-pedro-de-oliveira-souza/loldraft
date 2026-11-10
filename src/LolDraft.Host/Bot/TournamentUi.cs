using Discord;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot;

public static class TournamentUi
{
    public const string JoinPrefix = "t:join:";
    public const string LeavePrefix = "t:leave:";
    public const string CheckInPrefix = "t:checkin:";
    public const string PickPrefix = "d:pick:";
    public const string AutoPrefix = "d:auto:";

    private const int FieldLimit = 1024;

    public static string StatusLabel(TournamentStatus s) => s switch
    {
        TournamentStatus.Registration => "📝 Inscrições abertas",
        TournamentStatus.CheckIn => "✋ Check-in aberto: confirme sua presença!",
        TournamentStatus.Drafting => "🎯 Draft em andamento",
        TournamentStatus.Drafted => "✅ Times definidos",
        TournamentStatus.Running => "⚔️ Partidas em andamento",
        TournamentStatus.Finished => "🏁 Encerrado",
        TournamentStatus.Cancelled => "🚫 Cancelado",
        _ => s.ToString(),
    };

    public static string FormatSummary(Tournament t)
    {
        var text = $"{t.TeamCount} times de {t.TeamSize} · {t.DefaultFormat.Label()}";
        if (t.SemifinalFormat is { } sf) text += $" · semi {sf.Label()}";
        if (t.FinalFormat is { } ff) text += $" · final {ff.Label()}";
        return text;
    }

    private static string Clip(string text) =>
        text.Length <= FieldLimit ? text : text[..(FieldLimit - 2)] + "…";

    private static string Mention(Player p) => p.Tag();

    // ------------------------------------------------------------------
    // Inscrição
    // ------------------------------------------------------------------

    public static Embed RegistrationEmbed(Tournament t)
    {
        var checkedIn = t.Entries.Count(e => e.CheckedIn);
        var captains = t.Entries.Count(e => e.IsCaptain);

        var eb = new EmbedBuilder()
            .WithColor(t.Status == TournamentStatus.Cancelled ? Ui.Error : Ui.Brand)
            .WithTitle($"🏆 {t.Name}")
            .WithDescription(StatusLabel(t.Status))
            .AddField("Formato", FormatSummary(t), inline: true)
            .AddField("Inscritos", t.RequireCheckIn && t.Status != TournamentStatus.Registration
                ? $"{t.Entries.Count} ({checkedIn} confirmados) / {t.RequiredPlayers}"
                : $"{t.Entries.Count} / {t.RequiredPlayers}", inline: true)
            .AddField("Capitães", $"{captains} / {t.TeamCount}", inline: true);

        foreach (var role in LolRoleExtensions.All)
        {
            var list = t.Entries.Where(e => e.Player.PrimaryRole == role)
                .OrderByDescending(e => e.IsCaptain).ThenBy(e => e.JoinedAt)
                .Select(e =>
                {
                    var marks = (e.IsCaptain ? "👑 " : "") +
                                (t.RequireCheckIn && t.Status == TournamentStatus.CheckIn ? (e.CheckedIn ? "✅ " : "⏳ ") : "");
                    var extra = (e.Player.SecondaryRole is { } s ? $" · {s.Emoji()}" : "") + (e.Player.IsFill ? " · 🎲" : "");
                    return $"{marks}{Mention(e.Player)}{extra}";
                }).ToList();
            eb.AddField($"{Ui.RoleText(role)} ({list.Count})", list.Count == 0 ? "—" : Clip(string.Join("\n", list)), inline: true);
        }

        eb.WithFooter($"#{t.Id} · 👑 capitão · emoji = secundária · 🎲 fill" +
                      (t.Status == TournamentStatus.CheckIn ? " · ✅ confirmado · ⏳ pendente" : ""));
        return eb.Build();
    }

    public static MessageComponent RegistrationComponents(Tournament t)
    {
        var cb = new ComponentBuilder();
        switch (t.Status)
        {
            case TournamentStatus.Registration:
                cb.WithButton("Inscrever-se", JoinPrefix + t.Id, ButtonStyle.Success, new Emoji("✍️"));
                cb.WithButton("Sair", LeavePrefix + t.Id, ButtonStyle.Secondary);
                break;
            case TournamentStatus.CheckIn:
                cb.WithButton("Confirmar presença", CheckInPrefix + t.Id, ButtonStyle.Primary, new Emoji("✅"));
                cb.WithButton("Inscrever-se", JoinPrefix + t.Id, ButtonStyle.Success, new Emoji("✍️"));
                cb.WithButton("Sair", LeavePrefix + t.Id, ButtonStyle.Secondary);
                break;
        }
        return cb.Build();
    }

    // ------------------------------------------------------------------
    // Draft
    // ------------------------------------------------------------------

    private static string? CaptainMention(Tournament t, Team team)
    {
        var captain = t.Entries.FirstOrDefault(e => e.Id == team.CaptainEntryId);
        return captain == null ? null : Mention(captain.Player);
    }

    public static string TeamRoster(Tournament t, Team team)
    {
        var lines = new List<string>();
        foreach (var role in LolRoleExtensions.All)
        {
            var member = team.Members.FirstOrDefault(m => m.AssignedRole == role);
            lines.Add(member == null
                ? $"▫️ *{role.Label()}*"
                : $"{role.Emoji()} {Mention(member.Player)}{(member.Id == team.CaptainEntryId ? " 👑" : "")}");
        }
        // Quem ficou sem role atribuída (só acontece com travas desligadas).
        foreach (var m in team.Members.Where(m => m.AssignedRole == null))
            lines.Add($"❔ {Mention(m.Player)}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Um campo por time; com muitos times (limite de 25 campos do Discord) agrupa vários por campo
    /// e usa a lista compacta.
    /// </summary>
    private static void AddTeamFields(EmbedBuilder eb, Tournament t, int? currentTeamId)
    {
        var teams = t.Teams.OrderBy(x => x.DraftPosition).ToList();
        string Title(Team team) =>
            (currentTeamId == team.Id && t.Status == TournamentStatus.Drafting ? "▶️ " : "") + $"{team.DraftPosition}. {team.Name}";

        if (teams.Count <= 20)
        {
            foreach (var team in teams)
            {
                var avg = t.TeamAverageRating(team) is { } a ? $"\n*média: {EloScore.Label(a)}*" : "";
                eb.AddField(Title(team), TeamRoster(t, team) + avg, inline: true);
            }
            return;
        }

        var perField = (int)Math.Ceiling(teams.Count / 20.0);
        foreach (var chunk in teams.Chunk(perField))
        {
            var text = string.Join("\n", chunk.Select(team => $"**{Title(team)}**: " + string.Join(" ",
                team.Members.OrderBy(m => m.AssignedRole).Select(m => $"{m.AssignedRole?.Emoji() ?? "❔"}{m.Player.Tag()}"))));
            eb.AddField("\u200b", Clip(text), inline: false);
        }
    }

    public static Embed DraftBoardEmbed(Tournament t)
    {
        var eb = new EmbedBuilder()
            .WithColor(Ui.Brand)
            .WithTitle($"🎯 Draft · {t.Name}");

        var current = t.CurrentTeam;
        if (t.Status == TournamentStatus.Drafting && current != null)
        {
            var deadline = t.CurrentPickDeadline is { } d ? $" · acaba <t:{d.ToUnixTimeSeconds()}:R>" : "";
            var missing = t.MissingRoles(current);
            eb.WithDescription(
                $"**Vez de {current.Name}** ({CaptainMention(t, current)})\n" +
                $"Pick {t.Picks.Count + 1}/{t.TotalPicks} · rodada {t.CurrentRound}{deadline}\n" +
                $"Faltam: {string.Join(" ", missing.Select(r => r.Emoji() + " " + r.Label()))}");
        }
        else if (t.Status == TournamentStatus.Cancelled)
        {
            eb.WithDescription("🚫 Campeonato cancelado.");
        }
        else
        {
            eb.WithColor(Ui.Ok).WithDescription("✅ **Draft concluído!** Times definidos.");
        }

        AddTeamFields(eb, t, current?.Id);

        var pool = t.AvailablePool.ToList();
        if (pool.Count > 0 && t.Status == TournamentStatus.Drafting)
        {
            var lines = LolRoleExtensions.All
                .Select(r => (r, list: pool.Where(e => e.Player.PrimaryRole == r).ToList()))
                .Where(x => x.list.Count > 0)
                .Select(x => $"{x.r.Emoji()} " + string.Join(", ", x.list.Select(e => Mention(e.Player))));
            eb.AddField($"Disponíveis ({pool.Count})", Clip(string.Join("\n", lines)));
        }

        var last = t.Picks.OrderByDescending(p => p.PickNumber).FirstOrDefault();
        if (last != null)
        {
            var entry = t.Entries.FirstOrDefault(e => e.Id == last.EntryId);
            var team = t.Teams.FirstOrDefault(x => x.Id == last.TeamId);
            if (entry != null && team != null)
                eb.WithFooter($"Último pick: {team.Name} → {entry.Player.DisplayName}{(last.AutoPicked ? " (auto)" : "")}");
        }

        return eb.Build();
    }

    public static MessageComponent DraftComponents(Tournament t)
    {
        var cb = new ComponentBuilder();
        var current = t.CurrentTeam;
        if (t.Status != TournamentStatus.Drafting || current == null) return cb.Build();

        var pickNo = t.Picks.Count + 1;
        var options = t.EligibleFor(current).Take(25).ToList();
        if (options.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{PickPrefix}{t.Id}:{pickNo}")
                .WithPlaceholder($"{current.Name}: escolha um jogador")
                .WithMinValues(1).WithMaxValues(1);

            foreach (var e in options)
            {
                var p = e.Player;
                var roles = p.PrimaryRole.Label() + (p.SecondaryRole is { } s ? "/" + s.Label() : "") + (p.IsFill ? " · fill" : "");
                var elo = p.Riot?.Tier != null ? $" · {p.Riot.Tier} {p.Riot.Division}" : "";
                var label = p.DisplayName.Length > 100 ? p.DisplayName[..100] : p.DisplayName;
                menu.AddOption(label, e.Id.ToString(), description: (roles + elo).Length > 100 ? (roles + elo)[..100] : roles + elo,
                    emote: Ui.RoleEmote(p.PrimaryRole));
            }
            cb.WithSelectMenu(menu, row: 0);
        }

        cb.WithButton("Auto-pick", $"{AutoPrefix}{t.Id}:{pickNo}", ButtonStyle.Secondary, new Emoji("🎲"), row: 1);
        return cb.Build();
    }

    public static Embed TeamsSummaryEmbed(Tournament t)
    {
        var eb = new EmbedBuilder()
            .WithColor(Ui.Ok)
            .WithTitle($"🛡️ Times do {t.Name}");
        AddTeamFields(eb, t, null);
        eb.WithFooter("Próximo passo: o admin cria as calls/cargos e gera a chave.");
        return eb.Build();
    }
}
