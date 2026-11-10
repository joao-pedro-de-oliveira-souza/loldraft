using System.Text.RegularExpressions;
using LolDraft.Application.Data;
using LolDraft.Domain;
using LolDraft.Domain.Players;
using Microsoft.EntityFrameworkCore;

namespace LolDraft.Application.Players;

public partial class PlayerService(AppDbContext db)
{
    public Task<Player?> FindByDiscordIdAsync(ulong discordUserId, CancellationToken ct = default) =>
        db.Players.FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId, ct);

    public Task<List<Player>> ListAsync(CancellationToken ct = default) =>
        db.Players.OrderBy(p => p.PrimaryRole).ThenBy(p => p.DisplayName).ToListAsync(ct);

    /// <summary>Cria ou atualiza o cadastro. Campos null não são alterados num cadastro existente.</summary>
    public async Task<(Player Player, bool Created)> UpsertAsync(
        ulong discordUserId, string displayName,
        LolRole? primary = null, LolRole? secondary = null, bool clearSecondary = false, bool? fill = null,
        CancellationToken ct = default)
    {
        var player = await FindByDiscordIdAsync(discordUserId, ct);
        var created = player == null;
        if (player == null)
        {
            player = new Player { DiscordUserId = discordUserId, PrimaryRole = primary ?? LolRole.Mid };
            db.Players.Add(player);
        }

        player.DisplayName = displayName;
        if (primary is { } p) player.PrimaryRole = p;
        if (clearSecondary) player.SecondaryRole = null;
        else if (secondary is { } s) player.SecondaryRole = s;
        if (player.SecondaryRole == player.PrimaryRole) player.SecondaryRole = null;
        if (fill is { } f) player.IsFill = f;
        player.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return (player, created);
    }

    /// <summary>
    /// Guarda o Riot ID informado (ainda não verificado). A verificação pela Riot API
    /// e a busca de elo entram na fase 5.
    /// </summary>
    public async Task<Result<Player>> LinkRiotAsync(ulong discordUserId, string riotId, CancellationToken ct = default)
    {
        var player = await FindByDiscordIdAsync(discordUserId, ct);
        if (player == null) return Result<Player>.Fail("Faça o cadastro primeiro com `!registrar <role>`.");

        var parsed = ParseRiotId(riotId);
        if (parsed == null)
            return Result<Player>.Fail("Riot ID inválido. Use o formato `Nome#TAG` (ex.: `Faker#KR1`).");

        var (gameName, tagLine) = parsed.Value;
        var taken = await db.Players.AnyAsync(p => p.Id != player.Id && p.Riot != null
            && p.Riot.GameName.ToLower() == gameName.ToLower() && p.Riot.TagLine.ToLower() == tagLine.ToLower(), ct);
        if (taken) return Result<Player>.Fail("Esse Riot ID já está vinculado a outro jogador do servidor.");

        player.Riot = new RiotAccount { GameName = gameName, TagLine = tagLine };
        player.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<Player>.Success(player);
    }

    public async Task<bool> UnlinkRiotAsync(ulong discordUserId, CancellationToken ct = default)
    {
        var player = await FindByDiscordIdAsync(discordUserId, ct);
        if (player?.Riot == null) return false;
        player.Riot = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>"Nome#TAG" → (Nome, TAG). Nome: 3–16 chars, tag: 3–5 chars.</summary>
    public static (string GameName, string TagLine)? ParseRiotId(string input)
    {
        var m = RiotIdRegex().Match(input.Trim());
        if (!m.Success) return null;
        return (m.Groups["name"].Value.Trim(), m.Groups["tag"].Value.Trim());
    }

    [GeneratedRegex(@"^(?<name>[^#]{3,16})#(?<tag>[A-Za-z0-9]{3,5})$")]
    private static partial Regex RiotIdRegex();
}
