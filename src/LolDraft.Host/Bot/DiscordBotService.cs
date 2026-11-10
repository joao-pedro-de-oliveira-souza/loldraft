using System.Reflection;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using LolDraft.Host.Bot.Text;
using Microsoft.Extensions.Options;

namespace LolDraft.Host.Bot;

public class DiscordOptions
{
    public string Token { get; set; } = "";

    /// <summary>Servidor do campeonato (usado pelo painel e para limpar comandos antigos).</summary>
    public ulong GuildId { get; set; }

    /// <summary>Prefixo dos comandos de texto.</summary>
    public string Prefix { get; set; } = "!";

    /// <summary>Avisar por DM quem foi escolhido no draft e o elenco final.</summary>
    public bool DmNotifications { get; set; } = true;
}

/// <summary>
/// Conecta o bot, lê os comandos de texto (!comando) e despacha botões/menus.
/// </summary>
public class DiscordBotService(
    DiscordSocketClient client,
    CommandService commands,
    InteractionService interactions,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    IConfiguration configuration,
    ILogger<DiscordBotService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Token))
        {
            logger.LogError("Discord:Token não configurado. Veja o README.");
            return;
        }

        client.Log += LogAsync;
        commands.Log += LogAsync;
        interactions.Log += LogAsync;

        client.Ready += async () =>
        {
            // Os comandos agora são de texto: remove os slash commands registrados por versões anteriores.
            try
            {
                if (opts.GuildId != 0) await interactions.RegisterCommandsToGuildAsync(opts.GuildId, deleteMissing: true);
                await interactions.RegisterCommandsGloballyAsync(deleteMissing: true);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Não consegui limpar os slash commands antigos"); }
            logger.LogInformation("Bot conectado como {User}. Prefixo dos comandos: {Prefix}", client.CurrentUser, opts.Prefix);
        };

        // ---------- Comandos de texto ----------
        // Ferramentas de teste (!dev ...) só existem com "Dev": { "Enabled": true }.
        var devEnabled = configuration.GetValue("Dev:Enabled", false);
        await TextCommandSetup.LoadAsync(commands, services, devEnabled);
        if (!devEnabled) logger.LogInformation("Comandos !dev desativados (Dev:Enabled = false).");

        client.MessageReceived += HandleMessageAsync;
        commands.CommandExecuted += OnCommandExecutedAsync;

        // ---------- Botões e menus ----------
        client.InteractionCreated += async interaction =>
        {
            var ctx = new SocketInteractionContext(client, interaction);
            await interactions.ExecuteCommandAsync(ctx, services);
        };
        interactions.InteractionExecuted += async (cmd, ctx, result) =>
        {
            if (result.IsSuccess) return;
            logger.LogWarning("Interação {Cmd} falhou: {Error} {Reason}", cmd?.Name, result.Error, result.ErrorReason);
            try
            {
                const string message = "❌ Algo deu errado. Tente de novo ou chame um admin.";
                if (ctx.Interaction.HasResponded) await ctx.Interaction.FollowupAsync(message, ephemeral: true);
                else await ctx.Interaction.RespondAsync(message, ephemeral: true);
            }
            catch { /* interação expirada */ }
        };
        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services);

        await client.LoginAsync(TokenType.Bot, opts.Token);
        await client.StartAsync();
    }

    private Task HandleMessageAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage message || message.Author.IsBot || message.Channel is not SocketGuildChannel) return Task.CompletedTask;

        var prefix = options.Value.Prefix;
        var argPos = 0;
        if (!message.HasStringPrefix(prefix, ref argPos)) return Task.CompletedTask;

        // Ignora "!" sozinho, "!!!", "!?" etc.: só trata quando vem uma palavra logo depois do prefixo.
        if (argPos >= message.Content.Length || !char.IsLetter(message.Content[argPos])) return Task.CompletedTask;

        // Cada comando roda no próprio escopo (banco/serviços), fora da thread do gateway.
        var context = new SocketCommandContext(client, message);
        _ = Task.Run(async () =>
        {
            using var scope = services.CreateScope();
            try { await commands.ExecuteAsync(context, argPos, scope.ServiceProvider); }
            catch (Exception ex) { logger.LogError(ex, "Erro ao executar comando"); }
        });
        return Task.CompletedTask;
    }

    /// <summary>Responde com o jeito certo de usar quando o comando não existe ou foi escrito errado.</summary>
    private async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, Discord.Commands.IResult result)
    {
        if (result.IsSuccess) return;
        var prefix = options.Value.Prefix;
        var typed = context.Message.Content.Split(' ', 2)[0];

        string reply;
        switch (result.Error)
        {
            case CommandError.UnknownCommand:
                var group = typed.TrimStart(prefix.ToCharArray()).ToLowerInvariant();
                var hasGroup = commands.Commands.Any(c => c.Module.Group?.Equals(group, StringComparison.OrdinalIgnoreCase) == true);
                reply = hasGroup
                    ? $"❌ Faltou dizer o que fazer. Veja os comandos com `{prefix}ajuda {group}`."
                    : $"❌ Não conheço o comando `{typed}`. Veja a lista com `{prefix}ajuda`.";
                break;

            case CommandError.ParseFailed:
            case CommandError.BadArgCount:
            case CommandError.ObjectNotFound:
            case CommandError.MultipleMatches:
                var detail = result.Error == CommandError.ParseFailed && !string.IsNullOrEmpty(result.ErrorReason) && result.ErrorReason.Contains('`')
                    ? result.ErrorReason + "\n"
                    : result.Error == CommandError.ObjectNotFound ? "Não encontrei esse jogador. Mencione com @.\n" : "";
                reply = command.IsSpecified
                    ? $"❌ {detail}Jeito certo: {HelpText.UsageLine(command.Value)}"
                    : $"❌ Comando incompleto. Veja `{prefix}ajuda`.";
                break;

            case CommandError.UnmetPrecondition:
                reply = "⛔ Só admins (Gerenciar servidor) podem usar esse comando.";
                break;

            default:
                logger.LogError("Comando {Cmd} falhou: {Error} {Reason}", command.IsSpecified ? command.Value.Name : typed, result.Error, result.ErrorReason);
                reply = "❌ Algo deu errado ao executar o comando. Tente de novo ou chame um admin.";
                break;
        }

        try { await context.Message.ReplyAsync(reply, allowedMentions: AllowedMentions.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Não consegui responder no canal"); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private Task LogAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        logger.Log(level, msg.Exception, "[{Source}] {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }
}
