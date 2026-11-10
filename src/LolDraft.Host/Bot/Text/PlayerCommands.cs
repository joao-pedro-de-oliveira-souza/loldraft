using Discord;
using Discord.Commands;
using LolDraft.Application.Players;
using LolDraft.Application.Riot;
using LolDraft.Domain.Players;

namespace LolDraft.Host.Bot.Text;

/// <summary>!ajuda</summary>
public class HelpCommands(CommandService commands) : TextModule
{
    [Command("ajuda"), Alias("help", "comandos")]
    [Summary("Lista os comandos e para que serve cada um.")]
    [Help(HelpSections.Player, "!ajuda [comando]", "!ajuda camp")]
    public async Task HelpAsync([Remainder] string? comando = null)
    {
        var filter = comando?.Trim().TrimStart('!').ToLowerInvariant();
        var embeds = HelpText.Build(commands, string.IsNullOrEmpty(filter) ? null : filter);
        if (embeds.Count == 0)
        {
            await Fail($"Não conheço `{comando}`. Use `!ajuda` para ver todos.");
            return;
        }

        // Detalhe de um comando específico.
        var exact = commands.Commands.FirstOrDefault(c => c.Aliases.Any(a => a.Equals(filter, StringComparison.OrdinalIgnoreCase)));
        if (exact != null && HelpText.Meta(exact) is { } meta)
        {
            await Say($"**{meta.Usage}**{(HelpText.IsAdminOnly(exact) ? " 🔒" : "")}\n{exact.Summary}" +
                      (meta.Example != null ? $"\nExemplo: `{meta.Example}`" : ""));
            return;
        }

        foreach (var embed in embeds) await Context.Channel.SendMessageAsync(embed: embed);
    }
}

/// <summary>Cadastro e conta Riot.</summary>
public class PlayerCommands(PlayerService players, RiotLinkService riot) : TextModule
{
    [Command("registrar"), Alias("cadastrar")]
    [Summary("Cadastra você (ou atualiza) com role principal, secundária opcional e fill.")]
    [Help(HelpSections.Player, "!registrar <principal> [secundária] [fill]", "!registrar mid top fill")]
    public async Task RegisterAsync(LolRole principal, params string[] resto)
    {
        LolRole? secondary = null;
        var fill = false;
        foreach (var word in resto)
        {
            if (word.Equals("fill", StringComparison.OrdinalIgnoreCase)) { fill = true; continue; }
            if (Parse.Role(word) is { } r && secondary == null) { secondary = r; continue; }
            await Fail($"Não entendi `{word}`. Jeito certo: `!registrar <principal> [secundária] [fill]` (roles: {Parse.RoleHint}).");
            return;
        }

        var (player, created) = await players.UpsertAsync(Context.User.Id, DisplayName,
            principal, secondary, clearSecondary: secondary == null, fill: fill);
        await Say(created ? "🎉 Cadastro criado!" : "✏️ Cadastro atualizado!", embed: Ui.PlayerProfile(player, Context.User));
    }

    [Command("perfil")]
    [Summary("Mostra o seu perfil ou o de outro jogador.")]
    [Help(HelpSections.Player, "!perfil [@jogador]", "!perfil @Fulano")]
    public async Task ProfileAsync(IUser? jogador = null)
    {
        var user = jogador ?? Context.User;
        var player = await players.FindByDiscordIdAsync(user.Id);
        if (player == null)
        {
            await Say(user.Id == Context.User.Id
                ? "Você ainda não tem cadastro. Use `!registrar <role>`."
                : $"{user.Mention} ainda não tem cadastro.");
            return;
        }
        await Say(null, embed: Ui.PlayerProfile(player, user));
    }

    [Command("jogadores")]
    [Summary("Lista os jogadores cadastrados por role.")]
    [Help(HelpSections.Player, "!jogadores")]
    public async Task ListAsync()
    {
        var all = await players.ListAsync();
        if (all.Count == 0) { await Say("Ninguém cadastrado ainda. Use `!registrar <role>`."); return; }

        var eb = new EmbedBuilder().WithColor(Ui.Brand).WithTitle($"👥 Jogadores cadastrados ({all.Count})");
        foreach (var role in LolRoleExtensions.All)
        {
            var list = all.Where(p => p.PrimaryRole == role)
                .Select(p => p.Tag() + (p.SecondaryRole is { } s ? $" · {s.Emoji()}" : "") + (p.IsFill ? " · 🎲" : ""))
                .ToList();
            var text = list.Count == 0 ? "—" : string.Join("\n", list);
            if (text.Length > 1024) text = text[..1000] + "\n…";
            eb.AddField($"{Ui.RoleText(role)} ({list.Count})", text, inline: true);
        }
        eb.WithFooter("emoji = role secundária · 🎲 = fill");
        await Say(null, embed: eb.Build());
    }

    [Command("painel-cadastro")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    [Summary("Posta neste canal um painel com botão para cada um escolher as roles por menu.")]
    [Help(HelpSections.Player, "!painel-cadastro")]
    public async Task PostPanelAsync()
    {
        await Context.Channel.SendMessageAsync(embed: Ui.RegistrationPanelEmbed(), components: Ui.RegistrationPanelButton());
    }

    // ---------------- Riot ----------------

    [Command("vincular")]
    [Summary("Vincula sua conta do LoL (Riot ID) e puxa seu elo.")]
    [Help(HelpSections.Riot, "!vincular <Nome#TAG>", "!vincular Faker#KR1")]
    public async Task LinkAsync([Remainder] string riotId)
    {
        using var typing = Context.Channel.EnterTypingState();
        var r = await riot.StartAsync(Context.User.Id, riotId);
        if (!r.Ok) { await Fail(r.Error!); return; }
        await Say($"🔗 {r.Value!.Message}", embed: Ui.PlayerProfile(r.Value.Player, Context.User));
    }

    [Command("desvincular")]
    [Summary("Remove a conta da Riot do seu cadastro.")]
    [Help(HelpSections.Riot, "!desvincular")]
    public async Task UnlinkAsync()
    {
        var ok = await players.UnlinkRiotAsync(Context.User.Id);
        await Say(ok ? "Conta desvinculada." : "Você não tem conta vinculada.");
    }

    [Command("elo")]
    [Summary("Atualiza o seu elo pela Riot.")]
    [Help(HelpSections.Riot, "!elo")]
    public async Task SyncMineAsync()
    {
        using var typing = Context.Channel.EnterTypingState();
        var r = await riot.SyncOneAsync(Context.User.Id);
        if (!r.Ok) { await Fail(r.Error!); return; }
        await Say("📈 Elo atualizado.", embed: Ui.PlayerProfile(r.Value!, Context.User));
    }

    [Command("sincronizar-elos")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    [Summary("Atualiza o elo de todas as contas vinculadas.")]
    [Help(HelpSections.Riot, "!sincronizar-elos")]
    public async Task SyncAllAsync()
    {
        if (!riot.Enabled) { await Fail("A chave da Riot não está configurada (Riot:ApiKey)."); return; }
        using var typing = Context.Channel.EnterTypingState();
        var (updated, failed, error) = await riot.SyncAllAsync();
        await Say($"📈 {updated} elo(s) atualizado(s)" + (failed > 0 ? $", {failed} falharam" : "") + (error != null ? $"\n⚠️ {error}" : "."));
    }
}
