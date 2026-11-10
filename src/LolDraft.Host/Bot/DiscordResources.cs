using System.Globalization;
using System.Text;
using Discord;
using Discord.WebSocket;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Host.Bot;

/// <summary>
/// Cria e remove os recursos do Discord de um campeonato: cargo por time, categoria com
/// texto + call privados, call de lobby; e move os jogadores entre as calls.
/// </summary>
public class DiscordResources(DiscordSocketClient client, ILogger<DiscordResources> logger)
{
    public record TeamIds(int TeamId, ulong? RoleId, ulong? CategoryId, ulong? TextId, ulong? VoiceId);

    public record SetupResult(List<TeamIds> Teams, ulong? LobbyVoiceId, List<string> Log);

    private static readonly GuildPermissions NoPerms = GuildPermissions.None;

    /// <summary>Cria o que ainda não existe (pode rodar de novo sem duplicar).</summary>
    public async Task<SetupResult> SetupAsync(SocketGuild guild, Tournament t)
    {
        var log = new List<string>();
        var result = new List<TeamIds>();
        var botId = client.CurrentUser.Id;

        // ---------- Lobby (categoria pública do campeonato) ----------
        ulong? lobbyId = t.LobbyVoiceChannelId is { } existingLobby && guild.GetVoiceChannel(existingLobby) != null
            ? existingLobby : null;
        if (lobbyId == null)
        {
            try
            {
                var cat = await guild.CreateCategoryChannelAsync($"🏆 {t.Name}");
                var lobby = await guild.CreateVoiceChannelAsync("🎙️ Lobby", p => p.CategoryId = cat.Id);
                lobbyId = lobby.Id;
                log.Add("🎙️ Call **Lobby** criada");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao criar lobby");
                log.Add($"⚠️ Não consegui criar o lobby: {Reason(ex)}");
            }
        }

        // ---------- Times ----------
        foreach (var team in t.Teams.OrderBy(x => x.DraftPosition))
        {
            ulong? roleId = team.DiscordRoleId is { } rid && guild.GetRole(rid) != null ? rid : null;
            ulong? catId = team.DiscordCategoryId is { } cid && guild.GetCategoryChannel(cid) != null ? cid : null;
            ulong? textId = team.DiscordTextChannelId is { } tid && guild.GetTextChannel(tid) != null ? tid : null;
            ulong? voiceId = team.DiscordVoiceChannelId is { } vid && guild.GetVoiceChannel(vid) != null ? vid : null;

            try
            {
                if (roleId == null)
                {
                    var role = await guild.CreateRoleAsync(team.Name, NoPerms, Ui.ParseColor(team.ColorHex),
                        isHoisted: true, isMentionable: true);
                    roleId = role.Id;
                }

                var overwrites = new List<Overwrite>
                {
                    new(guild.EveryoneRole.Id, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Deny, connect: PermValue.Deny)),
                    new(roleId.Value, PermissionTarget.Role,
                        // Só permissões que o bot também tem (regra do Discord para overwrites);
                        // falar/transmitir já vêm liberados pelo @everyone por padrão.
                        new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow,
                            sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow, embedLinks: PermValue.Allow)),
                    new(botId, PermissionTarget.User,
                        new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow, sendMessages: PermValue.Allow,
                            manageChannel: PermValue.Allow, moveMembers: PermValue.Allow, embedLinks: PermValue.Allow)),
                };

                if (catId == null)
                {
                    var cat = await guild.CreateCategoryChannelAsync($"🛡️ {team.Name}", p => p.PermissionOverwrites = overwrites);
                    catId = cat.Id;
                }
                if (textId == null)
                {
                    var text = await guild.CreateTextChannelAsync($"chat-{Slug(team.Name)}", p =>
                    {
                        p.CategoryId = catId;
                        p.PermissionOverwrites = overwrites;
                    });
                    textId = text.Id;
                }
                if (voiceId == null)
                {
                    var voice = await guild.CreateVoiceChannelAsync($"🔊 {team.Name}", p =>
                    {
                        p.CategoryId = catId;
                        p.PermissionOverwrites = overwrites;
                    });
                    voiceId = voice.Id;
                }

                // Cargo para os membros reais (🤖 de teste não existem no Discord).
                var given = 0;
                foreach (var m in team.Members.Where(m => !m.Player.IsFake))
                {
                    var user = await GetUserAsync(guild, m.Player.DiscordUserId);
                    if (user == null) { log.Add($"⚠️ {m.Player.DisplayName} não está mais no servidor"); continue; }
                    if (!user.RoleIds.Contains(roleId.Value)) await user.AddRoleAsync(roleId.Value);
                    given++;
                }
                log.Add($"🛡️ **{team.Name}**: cargo, chat e call prontos ({given} jogador(es) com cargo)");

                if (textId is { } chatId && guild.GetTextChannel(chatId) is { } chat)
                {
                    var lines = string.Join("\n", team.Members.OrderBy(m => m.AssignedRole)
                        .Select(m => $"{(m.AssignedRole is { } r ? Ui.RoleText(r) : "❔")}: {m.Player.Tag()}"));
                    await chat.SendMessageAsync($"Bem-vindos, <@&{roleId}>! 🎮\n{lines}",
                        allowedMentions: new AllowedMentions { RoleIds = [roleId.Value] });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao montar recursos do time {Team}", team.Name);
                log.Add($"⚠️ **{team.Name}**: {Reason(ex)}");
            }

            result.Add(new TeamIds(team.Id, roleId, catId, textId, voiceId));
        }

        return new SetupResult(result, lobbyId, log);
    }

    /// <summary>Move os jogadores dos times informados para a call do próprio time.</summary>
    public async Task<List<string>> MoveToTeamVoiceAsync(SocketGuild guild, Tournament t, IEnumerable<Team> teams)
    {
        var log = new List<string>();
        foreach (var team in teams)
        {
            if (team.DiscordVoiceChannelId is not { } voiceId || guild.GetVoiceChannel(voiceId) == null)
            {
                log.Add($"⚠️ **{team.Name}** ainda não tem call. Rode `!camp montar`.");
                continue;
            }
            var (moved, outside) = await MoveMembersAsync(guild, team.Members.Select(m => m.Player), voiceId);
            log.Add($"🔊 **{team.Name}**: {moved} movido(s)" + (outside.Count > 0 ? $" · fora de call: {string.Join(", ", outside)}" : ""));
        }
        return log;
    }

    public async Task<List<string>> MoveAllToLobbyAsync(SocketGuild guild, Tournament t)
    {
        if (t.LobbyVoiceChannelId is not { } lobby || guild.GetVoiceChannel(lobby) == null)
            return ["⚠️ Não há call de lobby. Rode `!camp montar`."];
        var players = t.Teams.SelectMany(x => x.Members).Select(m => m.Player);
        var (moved, outside) = await MoveMembersAsync(guild, players, lobby);
        return [$"🎙️ {moved} jogador(es) no lobby" + (outside.Count > 0 ? $" · fora de call: {string.Join(", ", outside)}" : "")];
    }

    private async Task<(int Moved, List<string> Outside)> MoveMembersAsync(SocketGuild guild,
        IEnumerable<LolDraft.Domain.Players.Player> players, ulong voiceId)
    {
        var moved = 0;
        var outside = new List<string>();
        foreach (var p in players.Where(p => !p.IsFake))
        {
            var user = guild.GetUser(p.DiscordUserId);
            // O Discord só deixa mover quem já está conectado em alguma call.
            if (user?.VoiceChannel == null) { outside.Add(p.DisplayName); continue; }
            if (user.VoiceChannel.Id == voiceId) { moved++; continue; }
            try
            {
                await user.ModifyAsync(x => x.ChannelId = voiceId);
                moved++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao mover {User}", p.DisplayName);
                outside.Add($"{p.DisplayName} (erro: {Reason(ex)})");
            }
        }
        return (moved, outside);
    }

    /// <summary>Apaga cargos, categorias e calls criadas pelo campeonato.</summary>
    public async Task<List<string>> CleanupAsync(SocketGuild guild, Tournament t)
    {
        var log = new List<string>();
        var deleted = 0;

        async Task Delete(ulong? id, Func<ulong, IDeletable?> find, string what)
        {
            if (id is not { } value) return;
            var item = find(value);
            if (item == null) return;
            try { await item.DeleteAsync(); deleted++; }
            catch (Exception ex) { log.Add($"⚠️ Não consegui apagar {what}: {Reason(ex)}"); }
        }

        foreach (var team in t.Teams)
        {
            await Delete(team.DiscordTextChannelId, id => guild.GetChannel(id), $"o chat do {team.Name}");
            await Delete(team.DiscordVoiceChannelId, id => guild.GetChannel(id), $"a call do {team.Name}");
            await Delete(team.DiscordCategoryId, id => guild.GetChannel(id), $"a categoria do {team.Name}");
            await Delete(team.DiscordRoleId, id => guild.GetRole(id), $"o cargo {team.Name}");
        }

        if (t.LobbyVoiceChannelId is { } lobbyId && guild.GetVoiceChannel(lobbyId) is { } lobby)
        {
            var categoryId = lobby.CategoryId;
            await Delete(lobbyId, id => guild.GetChannel(id), "o lobby");
            if (categoryId is { } c && guild.GetCategoryChannel(c) is { } cat && !cat.Channels.Any(ch => ch.Id != lobbyId))
                await Delete(c, id => guild.GetChannel(id), "a categoria do campeonato");
        }

        log.Insert(0, $"🧹 {deleted} cargo(s)/canal(is) removido(s).");
        return log;
    }

    private async Task<IGuildUser?> GetUserAsync(SocketGuild guild, ulong userId)
    {
        if (guild.GetUser(userId) is { } cached) return cached;
        try { return await client.Rest.GetGuildUserAsync(guild.Id, userId); }
        catch { return null; }
    }

    /// <summary>"Time Faker!" → "time-faker" (nome de canal de texto).</summary>
    public static string Slug(string name)
    {
        var normalized = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "time" : slug[..Math.Min(slug.Length, 80)];
    }

    private static string Reason(Exception ex) => ex switch
    {
        Discord.Net.HttpException { HttpCode: System.Net.HttpStatusCode.Forbidden } =>
            "sem permissão (confira se o cargo do bot está acima dos cargos dos times e se ele tem Gerenciar cargos/canais)",
        _ => ex.Message,
    };
}
