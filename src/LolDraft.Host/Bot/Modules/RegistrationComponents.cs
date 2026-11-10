using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LolDraft.Application.Players;
using LolDraft.Domain.Players;

namespace LolDraft.Host.Bot.Modules;

/// <summary>Botões e menus do painel de cadastro (!painel-cadastro).</summary>
public class RegistrationComponents(PlayerService players) : InteractionModuleBase<SocketInteractionContext>
{
    private string CurrentDisplayName =>
        (Context.User as IGuildUser)?.DisplayName ?? Context.User.GlobalName ?? Context.User.Username;

    [ComponentInteraction(Ui.RegOpen)]
    public async Task OpenEditorAsync()
    {
        var player = await players.FindByDiscordIdAsync(Context.User.Id);
        var (content, components) = Ui.RegistrationEditor(player);
        await RespondAsync(content, components: components, ephemeral: true);
    }

    [ComponentInteraction(Ui.RegPrimary)]
    public async Task PrimarySelectedAsync(string[] values)
    {
        var role = Enum.Parse<LolRole>(values[0]);
        var (player, _) = await players.UpsertAsync(Context.User.Id, CurrentDisplayName, primary: role);
        await UpdateEditorAsync(player);
    }

    [ComponentInteraction(Ui.RegSecondary)]
    public async Task SecondarySelectedAsync(string[] values)
    {
        var existing = await players.FindByDiscordIdAsync(Context.User.Id);
        if (existing == null)
        {
            await RespondAsync("Escolha a role principal primeiro.", ephemeral: true);
            return;
        }

        var none = values[0] == "none";
        var (player, _) = await players.UpsertAsync(Context.User.Id, CurrentDisplayName,
            secondary: none ? null : Enum.Parse<LolRole>(values[0]), clearSecondary: none);
        await UpdateEditorAsync(player);
    }

    [ComponentInteraction(Ui.RegFill)]
    public async Task ToggleFillAsync()
    {
        var existing = await players.FindByDiscordIdAsync(Context.User.Id);
        if (existing == null)
        {
            await RespondAsync("Escolha a role principal primeiro.", ephemeral: true);
            return;
        }
        var (player, _) = await players.UpsertAsync(Context.User.Id, CurrentDisplayName, fill: !existing.IsFill);
        await UpdateEditorAsync(player);
    }

    private async Task UpdateEditorAsync(Player player)
    {
        var (content, components) = Ui.RegistrationEditor(player);
        if (Context.Interaction is SocketMessageComponent component)
            await component.UpdateAsync(m =>
            {
                m.Content = content;
                m.Components = components;
            });
        else
            await RespondAsync(content, components: components, ephemeral: true);
    }
}
