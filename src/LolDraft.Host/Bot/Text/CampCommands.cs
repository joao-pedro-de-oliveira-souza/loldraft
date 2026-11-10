using Discord;
using Discord.Commands;
using LolDraft.Application.Tournaments;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot.Text;

/// <summary>!camp ...: campeonato, draft, chave e calls.</summary>
[Group("camp")]
public class CampCommands(TournamentService tournaments, DiscordResources resources) : TextModule
{
    private const GuildPermission Admin = GuildPermission.ManageGuild;

    private async Task<Tournament?> Active()
    {
        var t = await tournaments.GetActiveAsync(Context.Guild.Id);
        if (t == null) await Say("Não há campeonato ativo. Um admin cria com `!camp criar <times> <formato> <nome>`.");
        return t;
    }

    private async Task<Tournament?> AfterDraft()
    {
        var t = await Active();
        if (t == null) return null;
        if (t.Status is TournamentStatus.Drafted or TournamentStatus.Running or TournamentStatus.Finished) return t;
        await Fail("Isso só funciona depois que o draft terminar.");
        return null;
    }

    private static string Lines(IEnumerable<string> lines)
    {
        var text = string.Join("\n", lines);
        return text.Length > 1900 ? text[..1900] + "\n…" : text;
    }

    // =====================================================================
    // Campeonato
    // =====================================================================

    [Command("criar"), RequireUserPermission(Admin)]
    [Summary("Cria o campeonato e posta o painel de inscrição neste canal.")]
    [Help(HelpSections.Tournament, "!camp criar <times> <formato> <nome>", "!camp criar 4 md3 Copa da Firma")]
    public async Task CreateAsync(int times, SeriesFormat formato, [Remainder] string nome)
    {
        var created = await tournaments.CreateAsync(Context.Guild.Id, nome, times, formato);
        if (!created.Ok) { await Fail(created.Error!); return; }
        var t = created.Value!;

        var panel = await Context.Channel.SendMessageAsync(embed: TournamentUi.RegistrationEmbed(t),
            components: TournamentUi.RegistrationComponents(t), allowedMentions: AllowedMentions.None);
        await tournaments.UpdateDiscordRefsAsync(t.Id, x =>
        {
            x.AnnouncementChannelId = Context.Channel.Id;
            x.RegistrationMessageId = panel.Id;
        });

        await Say($"🏆 **{t.Name}** criado! Inscrições abertas no painel acima.\n" +
                  "Próximos passos: `!camp capitao @jogador` → `!camp checkin` → `!camp iniciar-draft`.\n" +
                  "Ajustes (final MD5, timer, check-in...): `!camp config`.");
    }

    [Command("config"), RequireUserPermission(Admin)]
    [Summary("Mostra ou muda a configuração: times, formato, semifinal, final, timer, roles, checkin.")]
    [Help(HelpSections.Tournament, "!camp config [opção] [valor]", "!camp config final md5")]
    public async Task ConfigAsync(string? opcao = null, [Remainder] string? valor = null)
    {
        var t = await Active();
        if (t == null) return;

        if (opcao == null)
        {
            await Say($"⚙️ **{t.Name}**: {TournamentUi.FormatSummary(t)} · timer {t.PickTimerSeconds}s · " +
                      $"travar roles: {(t.EnforceRoles ? "sim" : "não")} · check-in: {(t.RequireCheckIn ? "sim" : "não")}\n" +
                      "Opções: `times <n>` · `formato <md>` · `semifinal <md>` · `final <md>` · `timer <segundos>` · `roles sim|nao` · `checkin sim|nao`");
            return;
        }

        int? teams = null, timer = null;
        SeriesFormat? fmt = null, semi = null, final = null;
        bool? roles = null, checkin = null;
        string? error = null;

        switch (opcao.ToLowerInvariant())
        {
            case "times": if (int.TryParse(valor, out var n)) teams = n; else error = "Use `!camp config times <número>`."; break;
            case "timer": if (int.TryParse(valor, out var s)) timer = s; else error = "Use `!camp config timer <segundos>`."; break;
            case "formato": fmt = Parse.Format(valor); if (fmt == null) error = $"Use {Parse.FormatHint}."; break;
            case "semifinal": case "semi": semi = Parse.Format(valor); if (semi == null) error = $"Use {Parse.FormatHint}."; break;
            case "final": final = Parse.Format(valor); if (final == null) error = $"Use {Parse.FormatHint}."; break;
            case "roles": roles = Parse.YesNo(valor); if (roles == null) error = "Use `sim` ou `nao`."; break;
            case "checkin": checkin = Parse.YesNo(valor); if (checkin == null) error = "Use `sim` ou `nao`."; break;
            default: error = $"Não conheço a opção `{opcao}`. Use times, formato, semifinal, final, timer, roles ou checkin."; break;
        }
        if (error != null) { await Fail(error); return; }

        var r = await tournaments.ConfigureAsync(t.Id, teams, fmt, semi, final, timer, roles, checkin);
        if (!r.Ok) { await Fail(r.Error!); return; }
        var c = r.Value!;
        await Say($"⚙️ Feito: {TournamentUi.FormatSummary(c)} · timer {c.PickTimerSeconds}s · " +
                  $"travar roles: {(c.EnforceRoles ? "sim" : "não")} · check-in: {(c.RequireCheckIn ? "sim" : "não")}");
    }

    [Command("capitao"), Alias("capitão"), RequireUserPermission(Admin)]
    [Summary("Define um capitão (inscreve automaticamente se precisar).")]
    [Help(HelpSections.Tournament, "!camp capitao <@jogador>", "!camp capitao @Fulano")]
    public async Task CaptainAsync(IUser jogador)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.SetCaptainAsync(t.Id, jogador.Id, true);
        if (!r.Ok) { await Fail(r.Error!); return; }
        await Say($"👑 {jogador.Mention} é capitão! ({r.Value!.Entries.Count(e => e.IsCaptain)}/{r.Value.TeamCount})");
    }

    [Command("tirar-capitao"), Alias("tirar-capitão"), RequireUserPermission(Admin)]
    [Summary("Tira alguém de capitão.")]
    [Help(HelpSections.Tournament, "!camp tirar-capitao <@jogador>")]
    public async Task RemoveCaptainAsync(IUser jogador)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.SetCaptainAsync(t.Id, jogador.Id, false);
        if (!r.Ok) { await Fail(r.Error!); return; }
        await Say($"{jogador.Mention} não é mais capitão. ({r.Value!.Entries.Count(e => e.IsCaptain)}/{r.Value.TeamCount})");
    }

    [Command("inscrever"), RequireUserPermission(Admin)]
    [Summary("Inscreve um jogador cadastrado.")]
    [Help(HelpSections.Tournament, "!camp inscrever <@jogador>")]
    public async Task AddPlayerAsync(IUser jogador)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.JoinAsync(t.Id, jogador.Id);
        await (r.Ok ? Say($"✍️ {jogador.Mention} inscrito.") : Fail(r.Error!));
    }

    [Command("remover"), RequireUserPermission(Admin)]
    [Summary("Tira um jogador da inscrição.")]
    [Help(HelpSections.Tournament, "!camp remover <@jogador>")]
    public async Task RemovePlayerAsync(IUser jogador)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.LeaveAsync(t.Id, jogador.Id);
        await (r.Ok ? Say($"{jogador.Mention} removido.") : Fail(r.Error!));
    }

    [Command("entrar")]
    [Summary("Inscreve você no campeonato (mesmo que o botão do painel).")]
    [Help(HelpSections.Tournament, "!camp entrar")]
    public async Task JoinAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.JoinAsync(t.Id, Context.User.Id);
        await (r.Ok ? Say("✍️ Inscrição feita!") : Fail(r.Error!));
    }

    [Command("sair")]
    [Summary("Tira você do campeonato (antes do draft).")]
    [Help(HelpSections.Tournament, "!camp sair")]
    public async Task LeaveAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.LeaveAsync(t.Id, Context.User.Id);
        await (r.Ok ? Say("Você saiu do campeonato.") : Fail(r.Error!));
    }

    [Command("checkin"), RequireUserPermission(Admin)]
    [Summary("Fecha as inscrições e abre a confirmação de presença.")]
    [Help(HelpSections.Tournament, "!camp checkin")]
    public async Task OpenCheckInAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.OpenCheckInAsync(t.Id);
        if (!r.Ok) { await Fail(r.Error!); return; }
        var pending = r.Value!.Entries.Where(e => !e.CheckedIn && !e.Player.IsFake).Select(e => e.Player.Tag());
        await Context.Channel.SendMessageAsync(
            $"✋ **Check-in aberto!** Clique em **Confirmar presença** ou mande `!camp confirmar`.\n{string.Join(" ", pending)}",
            components: TournamentUi.RegistrationComponents(r.Value));
    }

    [Command("confirmar")]
    [Summary("Confirma sua presença no check-in.")]
    [Help(HelpSections.Tournament, "!camp confirmar")]
    public async Task ConfirmAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.CheckInAsync(t.Id, Context.User.Id);
        await (r.Ok ? Say("✅ Presença confirmada!") : Fail(r.Error!));
    }

    [Command("status")]
    [Summary("Mostra o painel atual do campeonato.")]
    [Help(HelpSections.Tournament, "!camp status")]
    public async Task StatusAsync()
    {
        var t = await Active();
        if (t == null) return;
        await Say(null, embed: t.Status is TournamentStatus.Drafting or TournamentStatus.Drafted
            ? TournamentUi.DraftBoardEmbed(t) : TournamentUi.RegistrationEmbed(t));
    }

    [Command("painel"), RequireUserPermission(Admin)]
    [Summary("Posta de novo o painel (inscrição ou draft) neste canal e passa a atualizar esse.")]
    [Help(HelpSections.Tournament, "!camp painel")]
    public async Task RepostPanelAsync()
    {
        var t = await Active();
        if (t == null) return;
        var isDraft = t.Status is TournamentStatus.Drafting or TournamentStatus.Drafted;
        var msg = isDraft
            ? await Context.Channel.SendMessageAsync(embed: TournamentUi.DraftBoardEmbed(t), components: TournamentUi.DraftComponents(t), allowedMentions: AllowedMentions.None)
            : await Context.Channel.SendMessageAsync(embed: TournamentUi.RegistrationEmbed(t), components: TournamentUi.RegistrationComponents(t), allowedMentions: AllowedMentions.None);
        await tournaments.UpdateDiscordRefsAsync(t.Id, x =>
        {
            x.AnnouncementChannelId = Context.Channel.Id;
            if (isDraft) x.DraftMessageId = msg.Id; else x.RegistrationMessageId = msg.Id;
        });
    }

    [Command("cancelar"), RequireUserPermission(Admin)]
    [Summary("Cancela o campeonato ativo.")]
    [Help(HelpSections.Tournament, "!camp cancelar")]
    public async Task CancelAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.CancelAsync(t.Id);
        await (r.Ok ? Say($"🚫 **{t.Name}** cancelado. Para apagar cargos e calls, use `!camp encerrar`.") : Fail(r.Error!));
    }

    // =====================================================================
    // Draft
    // =====================================================================

    [Command("iniciar-draft"), Alias("draft"), RequireUserPermission(Admin)]
    [Summary("Começa o draft neste canal. A ordem dos capitães é sorteada; use \"fixo\" para manter a de inscrição.")]
    [Help(HelpSections.Draft, "!camp iniciar-draft [fixo]", "!camp iniciar-draft")]
    public async Task StartDraftAsync(string? ordem = null)
    {
        if (ordem != null && !ordem.Equals("fixo", StringComparison.OrdinalIgnoreCase))
        {
            await Fail("Jeito certo: `!camp iniciar-draft` (ordem sorteada) ou `!camp iniciar-draft fixo`.");
            return;
        }
        var t = await Active();
        if (t == null) return;

        var r = await tournaments.StartDraftAsync(t.Id, shuffleOrder: ordem == null);
        if (!r.Ok) { await Fail(r.Error!); return; }
        var started = (await tournaments.GetAsync(t.Id))!;

        var board = await Context.Channel.SendMessageAsync("🎯 **O draft começou!** Capitães, escolham pelo menu quando for a sua vez.",
            embed: TournamentUi.DraftBoardEmbed(started), components: TournamentUi.DraftComponents(started),
            allowedMentions: AllowedMentions.None);
        await tournaments.UpdateDiscordRefsAsync(t.Id, x =>
        {
            x.DraftMessageId = board.Id;
            x.AnnouncementChannelId = Context.Channel.Id;
        });

        var first = started.CurrentTeam;
        var captain = first == null ? null : started.Entries.FirstOrDefault(e => e.Id == first.CaptainEntryId);
        if (captain != null)
            await Context.Channel.SendMessageAsync($"⏰ {captain.Player.Tag()}, é a sua vez! ({started.PickTimerSeconds}s)");
    }

    [Command("autopick"), RequireUserPermission(Admin)]
    [Summary("Faz o pick automático para o time da vez.")]
    [Help(HelpSections.Draft, "!camp autopick")]
    public async Task ForceAutoPickAsync()
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.PickAsync(t.Id, 0, 0, auto: true);
        if (!r.Ok) await Fail(r.Error!);
    }

    [Command("balancear"), RequireUserPermission(Admin)]
    [Summary("Monta os times automaticamente por elo, sem draft manual, respeitando as roles.")]
    [Help(HelpSections.Draft, "!camp balancear")]
    public async Task BalanceAsync()
    {
        var t = await Active();
        if (t == null) return;
        using var typing = Context.Channel.EnterTypingState();
        var r = await tournaments.BalanceAsync(t.Id);
        if (!r.Ok) { await Fail(r.Error!); return; }
        var unrated = r.Value!.Entries.Count(e => e.TeamId != null && e.Player.RatingScore == null);
        await Say("⚖️ **Times montados por elo.**" + (unrated > 0 ? $" {unrated} jogador(es) sem elo entraram por último." : ""));
    }

    [Command("renomear-time"), RequireUserPermission(Admin)]
    [Summary("Muda o nome de um time (número = posição no draft). Renomeia cargo e canais também.")]
    [Help(HelpSections.Draft, "!camp renomear-time <número> <nome>", "!camp renomear-time 1 Tigres")]
    public async Task RenameTeamAsync(int time, [Remainder] string nome)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.RenameTeamAsync(t.Id, time, nome);
        if (!r.Ok) { await Fail(r.Error!); return; }

        var team = r.Value!.Teams.First(x => x.DraftPosition == time);
        if (team.DiscordRoleId is { } roleId && Context.Guild.GetRole(roleId) is { } role) await role.ModifyAsync(x => x.Name = team.Name);
        if (team.DiscordCategoryId is { } c && Context.Guild.GetCategoryChannel(c) is { } cat) await cat.ModifyAsync(x => x.Name = $"🛡️ {team.Name}");
        if (team.DiscordVoiceChannelId is { } v && Context.Guild.GetVoiceChannel(v) is { } voice) await voice.ModifyAsync(x => x.Name = $"🔊 {team.Name}");
        if (team.DiscordTextChannelId is { } tx && Context.Guild.GetTextChannel(tx) is { } text) await text.ModifyAsync(x => x.Name = $"chat-{DiscordResources.Slug(team.Name)}");
        await Say($"✏️ Time {time} agora se chama **{team.Name}**.");
    }

    // =====================================================================
    // Depois do draft
    // =====================================================================

    [Command("montar"), RequireUserPermission(Admin)]
    [Summary("Cria cargo, chat e call privados para cada time, mais uma call de Lobby.")]
    [Help(HelpSections.After, "!camp montar")]
    public async Task SetupAsync()
    {
        var t = await AfterDraft();
        if (t == null) return;
        using var typing = Context.Channel.EnterTypingState();

        var result = await resources.SetupAsync(Context.Guild, t);
        await tournaments.UpdateDiscordRefsAsync(t.Id, x =>
        {
            x.LobbyVoiceChannelId = result.LobbyVoiceId;
            foreach (var ids in result.Teams)
            {
                var team = x.Teams.FirstOrDefault(tm => tm.Id == ids.TeamId);
                if (team == null) continue;
                team.DiscordRoleId = ids.RoleId;
                team.DiscordCategoryId = ids.CategoryId;
                team.DiscordTextChannelId = ids.TextId;
                team.DiscordVoiceChannelId = ids.VoiceId;
            }
        });
        var next = t.Matches.Count == 0 ? "\n\nPróximo: `!camp chave`." : "";
        await Say($"🛠️ **Montagem do {t.Name}**\n{Lines(result.Log)}{next}");
    }

    [Command("chave"), RequireUserPermission(Admin)]
    [Summary("Gera o chaveamento (confrontos sorteados; \"fixo\" usa a ordem do draft) e posta as partidas.")]
    [Help(HelpSections.After, "!camp chave [fixo]")]
    public async Task BracketAsync(string? ordem = null)
    {
        if (ordem != null && !ordem.Equals("fixo", StringComparison.OrdinalIgnoreCase))
        {
            await Fail("Jeito certo: `!camp chave` (sorteada) ou `!camp chave fixo`.");
            return;
        }
        var t = await AfterDraft();
        if (t == null) return;

        if (t.Matches.Count == 0)
        {
            var r = await tournaments.GenerateBracketAsync(t.Id, shuffleSeeds: ordem == null);
            if (!r.Ok) { await Fail(r.Error!); return; }
            t = (await tournaments.GetAsync(t.Id))!;
        }

        await Context.Channel.SendMessageAsync(embed: BracketUi.BracketEmbed(t), allowedMentions: AllowedMentions.None);
        foreach (var m in t.ReadyMatches)
            await Context.Channel.SendMessageAsync(embed: BracketUi.MatchEmbed(t, m), components: BracketUi.MatchComponents(t, m),
                allowedMentions: AllowedMentions.None);
    }

    [Command("ver-chave")]
    [Summary("Mostra a chave atual.")]
    [Help(HelpSections.After, "!camp ver-chave")]
    public async Task ShowBracketAsync()
    {
        var t = await tournaments.GetActiveAsync(Context.Guild.Id) ?? await tournaments.GetLatestAsync(Context.Guild.Id);
        if (t == null) { await Say("Nenhum campeonato encontrado."); return; }
        await Say(null, embed: BracketUi.BracketEmbed(t));
    }

    [Command("partida")]
    [Summary("Mostra o cartão de uma partida, com os botões de resultado e de chamar para as calls.")]
    [Help(HelpSections.After, "!camp partida <número>", "!camp partida 2")]
    public async Task MatchCardAsync(int numero)
    {
        var t = await tournaments.GetActiveAsync(Context.Guild.Id) ?? await tournaments.GetLatestAsync(Context.Guild.Id);
        var m = t?.Matches.FirstOrDefault(x => x.Number == numero);
        if (t == null || m == null) { await Fail($"Não existe a partida #{numero}. Veja com `!camp ver-chave`."); return; }
        await Context.Channel.SendMessageAsync(embed: BracketUi.MatchEmbed(t, m), components: BracketUi.MatchComponents(t, m),
            allowedMentions: AllowedMentions.None);
    }

    [Command("placar"), RequireUserPermission(Admin)]
    [Summary("Corrige o placar de uma série (vitórias do time de cima e do de baixo).")]
    [Help(HelpSections.After, "!camp placar <partida> <vitórias A> <vitórias B>", "!camp placar 1 2 1")]
    public async Task SetScoreAsync(int numero, int vitoriasA, int vitoriasB)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.SetScoreAsync(t.Id, numero, vitoriasA, vitoriasB);
        await (r.Ok ? Say($"📝 Placar ajustado: {BracketUi.MatchLine(r.Value!)}") : Fail(r.Error!));
    }

    [Command("formato"), RequireUserPermission(Admin)]
    [Summary("Muda o formato de uma partida específica.")]
    [Help(HelpSections.After, "!camp formato <partida> <md1|md3|md5>", "!camp formato 3 md5")]
    public async Task ChangeFormatAsync(int numero, SeriesFormat formato)
    {
        var t = await Active();
        if (t == null) return;
        var r = await tournaments.ChangeMatchFormatAsync(t.Id, numero, formato);
        await (r.Ok ? Say($"🔁 Partida #{numero} agora é {r.Value!.Format.Label()}.") : Fail(r.Error!));
    }

    [Command("chamar"), RequireUserPermission(GuildPermission.MoveMembers)]
    [Summary("Move os jogadores para as calls dos times (de uma partida ou de todos).")]
    [Help(HelpSections.After, "!camp chamar [partida]", "!camp chamar 2")]
    public async Task CallTeamsAsync(int partida = 0)
    {
        var t = await AfterDraft();
        if (t == null) return;

        IEnumerable<Team> teams = t.Teams.OrderBy(x => x.DraftPosition);
        if (partida > 0)
        {
            var m = t.Matches.FirstOrDefault(x => x.Number == partida);
            if (m?.TeamA == null || m.TeamB == null) { await Fail($"A partida #{partida} não existe ou ainda não tem os dois times."); return; }
            teams = [m.TeamA, m.TeamB];
        }
        var log = await resources.MoveToTeamVoiceAsync(Context.Guild, t, teams);
        await Say(Lines(log));
    }

    [Command("lobby"), RequireUserPermission(GuildPermission.MoveMembers)]
    [Summary("Junta todos os jogadores do campeonato na call de Lobby.")]
    [Help(HelpSections.After, "!camp lobby")]
    public async Task LobbyAsync()
    {
        var t = await AfterDraft();
        if (t == null) return;
        var log = await resources.MoveAllToLobbyAsync(Context.Guild, t);
        await Say(Lines(log));
    }

    [Command("encerrar"), RequireUserPermission(Admin)]
    [Summary("Apaga cargos, chats e calls do campeonato e o encerra.")]
    [Help(HelpSections.After, "!camp encerrar")]
    public async Task CleanupAsync()
    {
        var t = await tournaments.GetLatestAsync(Context.Guild.Id);
        if (t == null) { await Say("Nenhum campeonato encontrado."); return; }
        using var typing = Context.Channel.EnterTypingState();

        var log = await resources.CleanupAsync(Context.Guild, t);
        await tournaments.UpdateDiscordRefsAsync(t.Id, x =>
        {
            x.LobbyVoiceChannelId = null;
            foreach (var team in x.Teams)
            {
                team.DiscordRoleId = null;
                team.DiscordCategoryId = null;
                team.DiscordTextChannelId = null;
                team.DiscordVoiceChannelId = null;
            }
            if (x.Status is not (TournamentStatus.Finished or TournamentStatus.Cancelled)) x.Cancel();
        });

        var champion = t.Champion is { } c && t.Status == TournamentStatus.Finished ? $"\n🏆 Campeão: **{c.Name}**" : "";
        await Say($"🏁 **{t.Name}** encerrado.{champion}\n{Lines(log)}");
    }
}
