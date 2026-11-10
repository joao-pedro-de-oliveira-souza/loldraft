using LolDraft.Application.Data;
using LolDraft.Application.Players;
using LolDraft.Domain;
using LolDraft.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;

namespace LolDraft.Application.Tournaments;

/// <summary>Eventos que o Discord e o painel (SignalR) escutam para se atualizar.</summary>
public interface ITournamentEvents
{
    Task TournamentChangedAsync(Tournament tournament, string reason);
    Task PickMadeAsync(Tournament tournament, DraftPick pick);
    Task DraftCompletedAsync(Tournament tournament);
    Task MatchUpdatedAsync(Tournament tournament, Match match);
}

/// <summary>Implementação vazia (testes / quando nenhum ouvinte está registrado).</summary>
public sealed class NoopTournamentEvents : ITournamentEvents
{
    public Task TournamentChangedAsync(Tournament t, string reason) => Task.CompletedTask;
    public Task PickMadeAsync(Tournament t, DraftPick pick) => Task.CompletedTask;
    public Task DraftCompletedAsync(Tournament t) => Task.CompletedTask;
    public Task MatchUpdatedAsync(Tournament t, Match m) => Task.CompletedTask;
}

/// <summary>
/// Um lock por campeonato: Discord e painel podem tentar pickar ao mesmo tempo,
/// mas só uma operação altera o mesmo campeonato por vez. Registrar como singleton.
/// </summary>
public sealed class TournamentLocks
{
    private readonly Dictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int tournamentId, CancellationToken ct)
    {
        SemaphoreSlim? sem;
        lock (_locks)
        {
            if (!_locks.TryGetValue(tournamentId, out sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _locks[tournamentId] = sem;
            }
        }
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        public void Dispose() => sem.Release();
    }
}

public class TournamentService(AppDbContext db, PlayerService players, TournamentLocks locks, ITournamentEvents events)
{
    private IQueryable<Tournament> Full => db.Tournaments
        .Include(t => t.Entries).ThenInclude(e => e.Player)
        .Include(t => t.Teams).ThenInclude(t => t.Members)
        .Include(t => t.Picks)
        .Include(t => t.Matches).ThenInclude(m => m.TeamA)
        .Include(t => t.Matches).ThenInclude(m => m.TeamB)
        .Include(t => t.Matches).ThenInclude(m => m.Winner)
        .AsSplitQuery();

    public Task<Tournament?> GetAsync(int id, CancellationToken ct = default) =>
        Full.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>Campeonato ativo mais recente do servidor (não finalizado/cancelado).</summary>
    public Task<Tournament?> GetActiveAsync(ulong guildId, CancellationToken ct = default) =>
        Full.Where(t => t.GuildId == guildId && t.Status != TournamentStatus.Finished && t.Status != TournamentStatus.Cancelled)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);

    public Task<List<Tournament>> ListAsync(ulong guildId, CancellationToken ct = default) =>
        db.Tournaments.Where(t => t.GuildId == guildId).OrderByDescending(t => t.Id).ToListAsync(ct);

    public async Task<Result<Tournament>> CreateAsync(ulong guildId, string name, int teamCount, SeriesFormat format, CancellationToken ct = default)
    {
        if (await GetActiveAsync(guildId, ct) is { } active)
            return Result<Tournament>.Fail($"Já existe um campeonato ativo: **{active.Name}**. Encerre ou cancele antes.");

        var t = new Tournament { GuildId = guildId, Name = name.Trim() };
        var cfg = t.Configure(teamCount: teamCount, defaultFormat: format);
        if (!cfg.Ok) return Result<Tournament>.Fail(cfg.Error!);

        db.Tournaments.Add(t);
        await db.SaveChangesAsync(ct);
        await events.TournamentChangedAsync(t, "created");
        return Result<Tournament>.Success(t);
    }

    /// <summary>
    /// Executa uma operação de domínio com lock, salva e publica o evento.
    /// </summary>
    public async Task<Result<Tournament>> MutateAsync(int tournamentId, Func<Tournament, Task<Result>> action,
        string reason, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(tournamentId, ct);
        var t = await GetAsync(tournamentId, ct);
        if (t == null) return Result<Tournament>.Fail("Campeonato não encontrado.");

        var result = await action(t);
        if (!result.Ok) return Result<Tournament>.Fail(result.Error!);

        await db.SaveChangesAsync(ct);
        await events.TournamentChangedAsync(t, reason);
        return Result<Tournament>.Success(t);
    }

    public Task<Result<Tournament>> JoinAsync(int tournamentId, ulong discordUserId, CancellationToken ct = default) =>
        MutateAsync(tournamentId, async t =>
        {
            var player = await players.FindByDiscordIdAsync(discordUserId, ct);
            if (player == null) return Result.Fail("Você precisa se cadastrar primeiro com `!registrar <role>`.");
            var r = t.Join(player);
            return r.Ok ? Result.Success() : Result.Fail(r.Error!);
        }, "joined", ct);

    public Task<Result<Tournament>> LeaveAsync(int tournamentId, ulong discordUserId, CancellationToken ct = default) =>
        MutateAsync(tournamentId, async t =>
        {
            var player = await players.FindByDiscordIdAsync(discordUserId, ct);
            return player == null ? Result.Fail("Você não está inscrito.") : t.Leave(player.Id);
        }, "left", ct);

    public Task<Result<Tournament>> CheckInAsync(int tournamentId, ulong discordUserId, CancellationToken ct = default) =>
        MutateAsync(tournamentId, async t =>
        {
            var player = await players.FindByDiscordIdAsync(discordUserId, ct);
            return player == null ? Result.Fail("Você não está inscrito.") : t.CheckIn(player.Id);
        }, "checkin", ct);

    /// <summary>Define/remove capitão. Se o jogador tiver cadastro mas não estiver inscrito, inscreve automaticamente.</summary>
    public Task<Result<Tournament>> SetCaptainAsync(int tournamentId, ulong discordUserId, bool isCaptain, CancellationToken ct = default) =>
        MutateAsync(tournamentId, async t =>
        {
            var player = await players.FindByDiscordIdAsync(discordUserId, ct);
            if (player == null) return Result.Fail("Esse usuário ainda não tem cadastro (`!registrar <role>`).");

            var entry = t.Entries.FirstOrDefault(e => e.PlayerId == player.Id);
            if (entry == null && isCaptain)
            {
                var joined = t.Join(player);
                if (!joined.Ok) return Result.Fail(joined.Error!);
                joined.Value!.CheckedIn = true;
            }
            else if (entry != null && isCaptain && t.RequireCheckIn)
            {
                entry.CheckedIn = true; // capitão é presença confirmada
            }
            return t.SetCaptain(player.Id, isCaptain);
        }, "captain", ct);

    public Task<Result<Tournament>> OpenCheckInAsync(int tournamentId, CancellationToken ct = default) =>
        MutateAsync(tournamentId, t => Task.FromResult(t.OpenCheckIn()), "checkin-open", ct);

    public Task<Result<Tournament>> ConfigureAsync(int tournamentId, int? teamCount, SeriesFormat? defaultFormat,
        SeriesFormat? semifinalFormat, SeriesFormat? finalFormat, int? pickTimerSeconds, bool? enforceRoles,
        bool? requireCheckIn, CancellationToken ct = default) =>
        MutateAsync(tournamentId, t => Task.FromResult(t.Configure(teamCount, defaultFormat, semifinalFormat,
            finalFormat, pickTimerSeconds, enforceRoles, requireCheckIn)), "config", ct);

    public Task<Result<Tournament>> CancelAsync(int tournamentId, CancellationToken ct = default) =>
        MutateAsync(tournamentId, t => Task.FromResult(t.Cancel()), "cancelled", ct);

    /// <summary>Guarda ids de mensagens/canais/cargos do Discord sem disparar eventos.</summary>
    public async Task UpdateDiscordRefsAsync(int tournamentId, Action<Tournament> update, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(tournamentId, ct);
        var t = await GetAsync(tournamentId, ct);
        if (t == null) return;
        update(t);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Campeonatos com draft rodando (para o timer de auto-pick).</summary>
    public Task<List<int>> ListDraftingIdsAsync(CancellationToken ct = default) =>
        db.Tournaments.Where(t => t.Status == TournamentStatus.Drafting).Select(t => t.Id).ToListAsync(ct);

    public async Task<Result<Tournament>> StartDraftAsync(int tournamentId, bool shuffleOrder, CancellationToken ct = default)
    {
        var r = await MutateAsync(tournamentId,
            t => Task.FromResult(t.StartDraft(DateTimeOffset.UtcNow, shuffleOrder ? Random.Shared : null)),
            "draft-started", ct);
        return r;
    }

    /// <summary>Monta os times automaticamente por elo (sem draft manual).</summary>
    public async Task<Result<Tournament>> BalanceAsync(int tournamentId, CancellationToken ct = default)
    {
        // Dois passos salvos separadamente: os picks precisam do Id real dos times.
        var start = await MutateAsync(tournamentId, t => Task.FromResult(t.AutoBalanceStart(DateTimeOffset.UtcNow)), "balancing", ct);
        if (!start.Ok) return start;
        var r = await MutateAsync(tournamentId, t => Task.FromResult(t.AutoDraftAll(DateTimeOffset.UtcNow)), "balanced", ct);
        if (r.Ok) await events.DraftCompletedAsync(r.Value!);
        return r;
    }

    public async Task<Result<DraftPick>> PickAsync(int tournamentId, int teamId, int entryId, bool auto = false, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(tournamentId, ct);
        var t = await GetAsync(tournamentId, ct);
        if (t == null) return Result<DraftPick>.Fail("Campeonato não encontrado.");

        var r = auto ? t.AutoPick(DateTimeOffset.UtcNow) : t.Pick(teamId, entryId, DateTimeOffset.UtcNow);
        if (!r.Ok) return r;

        await db.SaveChangesAsync(ct);
        await events.PickMadeAsync(t, r.Value!);
        if (t.Status == TournamentStatus.Drafted) await events.DraftCompletedAsync(t);
        return r;
    }

    /// <summary>
    /// Chamado pelo timer em background: auto-pick se o tempo do capitão acabou,
    /// ou se é a vez de um capitão de teste (🤖).
    /// </summary>
    public async Task<bool> AutoPickIfExpiredAsync(int tournamentId, CancellationToken ct = default)
    {
        DraftPick pick;
        Tournament t;
        using (await locks.AcquireAsync(tournamentId, ct))
        {
            var loaded = await GetAsync(tournamentId, ct);
            if (loaded == null) return false;
            t = loaded;
            var now = DateTimeOffset.UtcNow;
            if (!t.IsPickExpired(now) && !t.IsBotTurnReady(now)) return false;

            var r = t.AutoPick(now);
            if (!r.Ok) return false;
            pick = r.Value!;
            await db.SaveChangesAsync(ct);
        }

        await events.PickMadeAsync(t, pick);
        if (t.Status == TournamentStatus.Drafted) await events.DraftCompletedAsync(t);
        return true;
    }

    public Task<Result<Tournament>> GenerateBracketAsync(int tournamentId, bool shuffleSeeds, CancellationToken ct = default) =>
        MutateAsync(tournamentId, t =>
        {
            var seeds = shuffleSeeds
                ? t.Teams.OrderBy(_ => Random.Shared.Next()).ToList()
                : t.Teams.OrderBy(x => x.DraftPosition).ToList();
            return Task.FromResult(t.GenerateBracket(seeds));
        }, "bracket", ct);

    /// <summary>Último campeonato do servidor, em qualquer status (para limpar cargos/canais).</summary>
    public Task<Tournament?> GetLatestAsync(ulong guildId, CancellationToken ct = default) =>
        Full.Where(t => t.GuildId == guildId).OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);

    public Task<Result<Tournament>> RenameTeamAsync(int tournamentId, int draftPosition, string name, CancellationToken ct = default) =>
        MutateAsync(tournamentId, t => Task.FromResult(t.RenameTeam(draftPosition, name)), "team-renamed", ct);

    public Task<Result<Match>> SetScoreAsync(int tournamentId, int matchNumber, int winsA, int winsB, CancellationToken ct = default) =>
        MatchOpAsync(tournamentId, t => t.SetScore(matchNumber, winsA, winsB), notify: true, ct);

    public Task<Result<Match>> ChangeMatchFormatAsync(int tournamentId, int matchNumber, SeriesFormat format, CancellationToken ct = default) =>
        MatchOpAsync(tournamentId, t => t.ChangeMatchFormat(matchNumber, format), notify: false, ct);

    private async Task<Result<Match>> MatchOpAsync(int tournamentId, Func<Tournament, Result<Match>> op, bool notify, CancellationToken ct)
    {
        Tournament t;
        Result<Match> r;
        using (await locks.AcquireAsync(tournamentId, ct))
        {
            var loaded = await GetAsync(tournamentId, ct);
            if (loaded == null) return Result<Match>.Fail("Campeonato não encontrado.");
            t = loaded;
            r = op(t);
            if (!r.Ok) return r;
            await db.SaveChangesAsync(ct);
        }
        if (notify) await events.MatchUpdatedAsync(t, r.Value!);
        return r;
    }

    public Task<Result<Match>> ReportGameAsync(int tournamentId, int matchNumber, int winnerTeamId, CancellationToken ct = default) =>
        MatchOpAsync(tournamentId, t => t.ReportGame(matchNumber, winnerTeamId), notify: true, ct);
}
