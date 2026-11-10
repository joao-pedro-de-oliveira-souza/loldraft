using System.Collections.Concurrent;
using LolDraft.Application.Data;
using LolDraft.Application.Players;
using LolDraft.Domain;
using LolDraft.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LolDraft.Application.Riot;

public record LinkStarted(Player Player, string Message);

/// <summary>
/// Vincula a conta da Riot ao cadastro e mantém o elo atualizado.
/// Registrar como singleton; usa escopos para o banco.
/// </summary>
public class RiotLinkService(IRiotApi riot, IServiceScopeFactory scopes)
{
    public bool Enabled => riot.Enabled;

    public async Task<Result<LinkStarted>> StartAsync(ulong discordUserId, string riotIdInput, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var players = scope.ServiceProvider.GetRequiredService<PlayerService>();

        // Sem chave da Riot: só guarda o Riot ID.
        if (!riot.Enabled)
        {
            var simple = await players.LinkRiotAsync(discordUserId, riotIdInput, ct);
            return simple.Ok
                ? Result<LinkStarted>.Success(new LinkStarted(simple.Value!,
                    "Riot ID guardado. O elo automático fica disponível quando um admin configurar a chave da Riot."))
                : Result<LinkStarted>.Fail(simple.Error!);
        }

        var player = await players.FindByDiscordIdAsync(discordUserId, ct);
        if (player == null) return Result<LinkStarted>.Fail("Faça o cadastro primeiro com `!registrar <role>`.");

        var parsed = PlayerService.ParseRiotId(riotIdInput);
        if (parsed == null) return Result<LinkStarted>.Fail("Riot ID inválido. Use o formato `Nome#TAG` (ex.: `Faker#KR1`).");

        RiotAccountDto? account;
        RiotSummonerDto? summoner;
        try
        {
            account = await riot.GetAccountByRiotIdAsync(parsed.Value.GameName, parsed.Value.TagLine, ct);
            if (account == null) return Result<LinkStarted>.Fail($"Não encontrei a conta `{riotIdInput}`. Confira o nome e a tag.");
            summoner = await riot.GetSummonerByPuuidAsync(account.Puuid, ct);
            if (summoner == null) return Result<LinkStarted>.Fail("Essa conta não tem perfil de League of Legends neste servidor.");
        }
        catch (RiotApiException ex) { return Result<LinkStarted>.Fail(ex.Message); }

        var taken = await db.Players.AnyAsync(p => p.Id != player.Id && p.Riot != null && p.Riot.Puuid == account.Puuid, ct);
        if (taken) return Result<LinkStarted>.Fail("Essa conta já está vinculada a outro jogador do servidor.");

        var tracked = await db.Players.FirstAsync(p => p.Id == player.Id, ct);
        tracked.Riot = new RiotAccount
        {
            GameName = account.GameName ?? parsed.Value.GameName,
            TagLine = account.TagLine ?? parsed.Value.TagLine,
            Puuid = account.Puuid,
            ProfileIconId = summoner.ProfileIconId,
        };
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try { await SyncRankAsync(db, tracked, ct); }
        catch (RiotApiException ex)
        {
            return Result<LinkStarted>.Success(new LinkStarted(tracked, $"Conta vinculada, mas não consegui puxar o elo agora: {ex.Message}"));
        }

        var elo = tracked.RatingScore is { } score ? $"Elo: {EloScore.Label(score)}." : "Sem ranqueada nesta temporada.";
        return Result<LinkStarted>.Success(new LinkStarted(tracked, $"Conta `{tracked.Riot.RiotId}` vinculada! {elo}"));
    }

    /// <summary>Atualiza o elo de um jogador.</summary>
    public async Task<Result<Player>> SyncOneAsync(ulong discordUserId, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstOrDefaultAsync(p => p.DiscordUserId == discordUserId, ct);
        if (player?.Riot?.Puuid == null) return Result<Player>.Fail("Você não tem conta Riot vinculada. Use `!vincular Nome#TAG`.");
        try
        {
            await SyncRankAsync(db, player, ct);
            return Result<Player>.Success(player);
        }
        catch (RiotApiException ex) { return Result<Player>.Fail(ex.Message); }
    }

    /// <summary>Atualiza o elo de todas as contas vinculadas (respeitando o limite da chave).</summary>
    public async Task<(int Updated, int Failed, string? Error)> SyncAllAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var players = (await db.Players.ToListAsync(ct)).Where(p => p.Riot is { Puuid: not null }).ToList();

        int updated = 0, failed = 0;
        foreach (var p in players)
        {
            try
            {
                await SyncRankAsync(db, p, ct);
                updated++;
            }
            catch (RiotApiException ex)
            {
                failed++;
                if (ex.Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
                    return (updated, failed + (players.Count - updated - failed), ex.Message);
            }
            await Task.Delay(120, ct); // chave pessoal: 20 req/s e 100 a cada 2 min
        }
        return (updated, failed, null);
    }

    private async Task SyncRankAsync(AppDbContext db, Player player, CancellationToken ct)
    {
        var entries = await riot.GetLeagueEntriesAsync(player.Riot!.Puuid!, ct);
        var best = entries.FirstOrDefault(e => e.QueueType == "RANKED_SOLO_5x5")
                   ?? entries.FirstOrDefault(e => e.QueueType == "RANKED_FLEX_SR");
        player.Riot.Tier = best?.Tier;
        player.Riot.Division = best is null || best.Tier is "MASTER" or "GRANDMASTER" or "CHALLENGER" ? null : best.Rank;
        player.Riot.LeaguePoints = best?.LeaguePoints;
        player.Riot.LastSyncedAt = DateTimeOffset.UtcNow;
        player.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

}
