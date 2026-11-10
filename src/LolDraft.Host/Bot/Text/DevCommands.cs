using Discord;
using Discord.Commands;
using LolDraft.Application.Dev;

namespace LolDraft.Host.Bot.Text;

/// <summary>!dev ...: jogadores de teste. Só carregado com "Dev": { "Enabled": true }.</summary>
[Group("dev")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public class DevCommands(DevService dev) : TextModule
{
    [Command("popular")]
    [Summary("Inscreve jogadores de teste (🤖) no campeonato ativo e completa os capitães. \"sem-capitaes\" não mexe nos capitães.")]
    [Help(HelpSections.Dev, "!dev popular [quantidade] [sem-capitaes]", "!dev popular")]
    public async Task PopulateAsync(int quantidade = 0, string? opcao = null)
    {
        if (opcao != null && !opcao.Equals("sem-capitaes", StringComparison.OrdinalIgnoreCase))
        {
            await Fail("Jeito certo: `!dev popular [quantidade] [sem-capitaes]`.");
            return;
        }
        using var typing = Context.Channel.EnterTypingState();
        var r = await dev.PopulateAsync(Context.Guild.Id, quantidade > 0 ? quantidade : null, fillCaptains: opcao == null);
        await (r.Ok ? Say(r.Value!) : Fail(r.Error!));
    }

    [Command("limpar")]
    [Summary("Apaga todos os jogadores de teste (🤖).")]
    [Help(HelpSections.Dev, "!dev limpar")]
    public async Task ClearAsync()
    {
        var r = await dev.ClearAsync(Context.Guild.Id);
        await (r.Ok ? Say($"🧹 {r.Value} jogadores de teste removidos.") : Fail(r.Error!));
    }
}
