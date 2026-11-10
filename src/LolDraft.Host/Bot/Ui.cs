using Discord;
using LolDraft.Domain.Players;

namespace LolDraft.Host.Bot;

public static class Ui
{
    public static readonly Color Brand = new(0xC8, 0x9B, 0x3C); // dourado LoL
    public static readonly Color Ok = new(0x22, 0xC5, 0x5E);
    public static readonly Color Error = new(0xE8, 0x40, 0x57);

    public static string RoleText(LolRole role) => $"{role.Emoji()} {role.Label()}";

    /// <summary>"#E84057" → cor do Discord (azul se inválida).</summary>
    public static Color ParseColor(string hex) =>
        uint.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? new Color(v) : Color.Blue;

    /// <summary>Menção do Discord; jogadores de teste aparecem pelo nome com 🤖.</summary>
    public static string Tag(this Player p) => p.IsFake ? $"🤖 **{p.DisplayName}**" : $"<@{p.DiscordUserId}>";

    public static Embed PlayerProfile(Player player, IUser? user = null)
    {
        var eb = new EmbedBuilder()
            .WithColor(Brand)
            .WithTitle($"Perfil de {player.DisplayName}")
            .AddField("Role principal", RoleText(player.PrimaryRole), inline: true)
            .AddField("Secundária", player.SecondaryRole is { } s ? RoleText(s) : "—", inline: true)
            .AddField("Fill", player.IsFill ? "✅ Aceita" : "❌ Não", inline: true);

        if (player.Riot is { } riot)
        {
            var elo = player.RatingScore is { } score
                ? $"{EloScore.Label(score)} · {riot.LeaguePoints} PL"
                : riot.LastSyncedAt != null ? "sem ranqueada nesta temporada" : "elo ainda não sincronizado";
            eb.AddField("Conta Riot", $"`{riot.RiotId}`\n{elo}");
        }
        else
        {
            eb.AddField("Conta Riot", "Não vinculada. Use `!vincular`.");
        }

        if (user != null) eb.WithThumbnailUrl(user.GetDisplayAvatarUrl());
        eb.WithFooter($"Cadastrado em {player.CreatedAt.ToLocalTime():dd/MM/yyyy}");
        return eb.Build();
    }

    // ---------- Painel de cadastro com componentes ----------

    public const string RegOpen = "reg:open";
    public const string RegPrimary = "reg:primary";
    public const string RegSecondary = "reg:secondary";
    public const string RegFill = "reg:fill";

    public static MessageComponent RegistrationPanelButton() => new ComponentBuilder()
        .WithButton("Cadastrar / editar roles", RegOpen, ButtonStyle.Success, new Emoji("📝"))
        .Build();

    public static Embed RegistrationPanelEmbed() => new EmbedBuilder()
        .WithColor(Brand)
        .WithTitle("📋 Cadastro de jogadores")
        .WithDescription(
            "Clique no botão abaixo para se cadastrar ou mudar suas roles.\n\n" +
            "• Escolha sua **role principal** e, se quiser, uma **secundária**\n" +
            "• Marque **Fill** se topa jogar em qualquer posição\n" +
            "• Opcional: vincule sua conta com `!vincular Nome#TAG`")
        .Build();

    /// <summary>Editor pessoal (mensagem efêmera) com o estado atual do jogador.</summary>
    public static (string Content, MessageComponent Components) RegistrationEditor(Player? player)
    {
        var primary = new SelectMenuBuilder()
            .WithCustomId(RegPrimary)
            .WithPlaceholder("Role principal")
            .WithMinValues(1).WithMaxValues(1);
        foreach (var r in LolRoleExtensions.All)
            primary.AddOption(r.Label(), r.ToString(), emote: new Emoji(r.Emoji()), isDefault: player?.PrimaryRole == r);

        var secondary = new SelectMenuBuilder()
            .WithCustomId(RegSecondary)
            .WithPlaceholder("Role secundária (opcional)")
            .WithMinValues(1).WithMaxValues(1)
            .AddOption("Nenhuma", "none", isDefault: player != null && player.SecondaryRole == null);
        foreach (var r in LolRoleExtensions.All)
            secondary.AddOption(r.Label(), r.ToString(), emote: new Emoji(r.Emoji()), isDefault: player?.SecondaryRole == r);

        var fillOn = player?.IsFill ?? false;
        var components = new ComponentBuilder()
            .WithSelectMenu(primary, row: 0)
            .WithSelectMenu(secondary, row: 1)
            .WithButton(fillOn ? "Fill: ligado" : "Fill: desligado", RegFill,
                fillOn ? ButtonStyle.Success : ButtonStyle.Secondary, new Emoji("🎲"), row: 2)
            .Build();

        var content = player == null
            ? "Escolha sua **role principal** para criar o cadastro."
            : $"✅ Cadastro salvo: **{RoleText(player.PrimaryRole)}**" +
              (player.SecondaryRole is { } s ? $" / {RoleText(s)}" : "") +
              (player.IsFill ? " / 🎲 Fill" : "") +
              "\nAs mudanças são salvas na hora.";

        return (content, components);
    }
}
