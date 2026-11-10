using System.Reflection;
using System.Text;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot.Text;

/// <summary>Metadados de ajuda: seção, uso e exemplo de cada comando de texto.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HelpAttribute(string section, string usage, string? example = null) : Attribute
{
    public string Section { get; } = section;
    public string Usage { get; } = usage;
    public string? Example { get; } = example;
}

/// <summary>Seções na ordem em que aparecem no !ajuda.</summary>
public static class HelpSections
{
    public const string Player = "Jogador";
    public const string Tournament = "Campeonato";
    public const string Draft = "Draft";
    public const string After = "Depois do draft";
    public const string Riot = "Conta Riot";
    public const string Dev = "Teste";

    public static readonly string[] Order = [Player, Riot, Tournament, Draft, After, Dev];
}

/// <summary>Converte o texto digitado em valores do domínio, aceitando apelidos comuns.</summary>
public static class Parse
{
    public static LolRole? Role(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "top" or "topo" => LolRole.Top,
        "jg" or "jungle" or "jungler" or "jungla" or "selva" => LolRole.Jungle,
        "mid" or "meio" => LolRole.Mid,
        "adc" or "ad" or "bot" or "atirador" => LolRole.Adc,
        "sup" or "supp" or "suporte" or "support" => LolRole.Support,
        _ => null,
    };

    public static SeriesFormat? Format(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "md1" or "bo1" or "1" => SeriesFormat.Bo1,
        "md3" or "bo3" or "3" => SeriesFormat.Bo3,
        "md5" or "bo5" or "5" => SeriesFormat.Bo5,
        _ => null,
    };

    public static bool? YesNo(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "sim" or "s" or "on" or "ligado" or "true" => true,
        "nao" or "não" or "n" or "off" or "desligado" or "false" => false,
        _ => null,
    };

    public const string RoleHint = "top, jg, mid, adc ou sup";
    public const string FormatHint = "md1, md3 ou md5";
}

public class RoleTypeReader : TypeReader
{
    public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services) =>
        Task.FromResult(Parse.Role(input) is { } role
            ? TypeReaderResult.FromSuccess(role)
            : TypeReaderResult.FromError(CommandError.ParseFailed, $"`{input}` não é uma role. Use {Parse.RoleHint}."));
}

public class FormatTypeReader : TypeReader
{
    public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services) =>
        Task.FromResult(Parse.Format(input) is { } f
            ? TypeReaderResult.FromSuccess(f)
            : TypeReaderResult.FromError(CommandError.ParseFailed, $"`{input}` não é um formato. Use {Parse.FormatHint}."));
}

/// <summary>Base dos módulos de texto: respostas curtas, como resposta à mensagem do usuário.</summary>
public abstract class TextModule : ModuleBase<SocketCommandContext>
{
    protected Task<IUserMessage> Say(string? text, Embed? embed = null, MessageComponent? components = null, bool mentions = false) =>
        Context.Message.ReplyAsync(text, embed: embed, components: components,
            allowedMentions: mentions ? AllowedMentions.All : AllowedMentions.None);

    protected Task<IUserMessage> Fail(string error) => Say($"❌ {error}");

    protected bool IsAdmin => (Context.User as IGuildUser)?.GuildPermissions.ManageGuild ?? false;

    protected string DisplayName =>
        (Context.User as IGuildUser)?.DisplayName ?? Context.User.GlobalName ?? Context.User.Username;
}

/// <summary>Monta os textos do !ajuda e das mensagens de "jeito certo".</summary>
public static class HelpText
{
    public static HelpAttribute? Meta(CommandInfo cmd) => cmd.Attributes.OfType<HelpAttribute>().FirstOrDefault();

    public static bool IsAdminOnly(CommandInfo cmd) =>
        cmd.Preconditions.Concat(cmd.Module.Preconditions).OfType<RequireUserPermissionAttribute>().Any();

    public static string UsageLine(CommandInfo cmd)
    {
        var meta = Meta(cmd);
        var line = $"`{meta?.Usage ?? "!" + cmd.Aliases[0]}`";
        if (meta?.Example is { } ex) line += $"\nExemplo: `{ex}`";
        return line;
    }

    /// <summary>Embeds do !ajuda, filtrando por grupo/nome quando informado.</summary>
    public static List<Embed> Build(CommandService commands, string? filter)
    {
        var all = commands.Commands
            .Where(c => Meta(c) != null)
            .Where(c => filter == null || c.Aliases.Any(a => a.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var embeds = new List<Embed>();
        if (all.Count == 0) return embeds;

        var eb = new EmbedBuilder()
            .WithColor(Ui.Brand)
            .WithTitle(filter == null ? "📖 Comandos do LolDraft" : $"📖 Comandos: {filter}")
            .WithFooter("🔒 = só admins (Gerenciar servidor) · <obrigatório> [opcional] · !ajuda <comando> para detalhes");

        foreach (var section in HelpSections.Order)
        {
            var lines = all.Where(c => Meta(c)!.Section == section)
                .Select(c => $"{(IsAdminOnly(c) ? "🔒 " : "")}`{Meta(c)!.Usage}`\n{c.Summary}")
                .ToList();
            if (lines.Count == 0) continue;

            // Campos do Discord têm no máximo 1024 caracteres: quebra em partes.
            var chunk = new StringBuilder();
            var part = 0;
            foreach (var line in lines)
            {
                if (chunk.Length + line.Length + 2 > 1000)
                {
                    eb.AddField(part++ == 0 ? section : $"{section} (cont.)", chunk.ToString());
                    chunk.Clear();
                }
                chunk.Append(line).Append("\n\n");
            }
            if (chunk.Length > 0) eb.AddField(part == 0 ? section : $"{section} (cont.)", chunk.ToString());

            if (eb.Fields.Count >= 20 || eb.Length > 5000)
            {
                embeds.Add(eb.Build());
                eb = new EmbedBuilder().WithColor(Ui.Brand);
            }
        }
        if (eb.Fields.Count > 0) embeds.Add(eb.Build());
        return embeds;
    }
}

/// <summary>Carga dos comandos de texto, usada pelo bot e pelos testes.</summary>
public static class TextCommandSetup
{
    /// <summary>
    /// Registra leitores e módulos. O Discord.Net instancia cada módulo uma vez ao registrar;
    /// isso precisa acontecer dentro de um escopo, porque os módulos usam serviços com escopo (banco).
    /// </summary>
    public static async Task LoadAsync(CommandService commands, IServiceProvider rootServices, bool devEnabled)
    {
        commands.AddTypeReader<LolDraft.Domain.Players.LolRole>(new RoleTypeReader());
        commands.AddTypeReader<LolDraft.Domain.Tournaments.SeriesFormat>(new FormatTypeReader());

        using var scope = rootServices.CreateScope();
        await commands.AddModulesAsync(typeof(TextCommandSetup).Assembly, scope.ServiceProvider);
        if (!devEnabled) await commands.RemoveModuleAsync<DevCommands>();
    }
}
