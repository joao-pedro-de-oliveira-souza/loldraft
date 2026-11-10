using LolDraft.Application.Data;
using LolDraft.Application.Dev;
using LolDraft.Application.Players;
using LolDraft.Application.Tournaments;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LolDraft.Tests;

/// <summary>
/// Fluxo completo com EF Core + SQLite real (em memória), um DbContext novo a cada passo,
/// como acontece com os escopos do bot.
/// </summary>
public sealed class ServiceFlowTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly TournamentLocks _locks = new();
    private const ulong Guild = 42;

    public ServiceFlowTests()
    {
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private async Task<T> WithServices<T>(Func<PlayerService, TournamentService, Task<T>> action)
    {
        await using var db = NewDb();
        var players = new PlayerService(db);
        var tournaments = new TournamentService(db, players, _locks, new NoopTournamentEvents());
        return await action(players, tournaments);
    }

    [Fact]
    public async Task Full_flow_registration_to_champion_persists_correctly()
    {
        var roles = LolRoleExtensions.All;

        // 20 cadastros
        for (ulong i = 0; i < 20; i++)
        {
            var role = roles[(int)(i % 5)];
            await WithServices((p, _) => p.UpsertAsync(1000 + i, $"P{i}", role, roles[(int)((i + 1) % 5)]));
        }

        var t = await WithServices((_, ts) => ts.CreateAsync(Guild, "Copa", 4, SeriesFormat.Bo3));
        Assert.True(t.Ok);
        var id = t.Value!.Id;

        await WithServices((_, ts) => ts.ConfigureAsync(id, null, null, null, SeriesFormat.Bo5, 30, null, true));

        for (ulong i = 0; i < 20; i++)
        {
            var r = await WithServices((_, ts) => ts.JoinAsync(id, 1000 + i));
            Assert.True(r.Ok, r.Error);
        }

        // Duplicado falha
        Assert.False((await WithServices((_, ts) => ts.JoinAsync(id, 1000))).Ok);

        foreach (var cap in new ulong[] { 1000, 1006, 1012, 1018 })
            Assert.True((await WithServices((_, ts) => ts.SetCaptainAsync(id, cap, true))).Ok);

        Assert.True((await WithServices((_, ts) => ts.OpenCheckInAsync(id))).Ok);
        for (ulong i = 0; i < 20; i++)
            await WithServices((_, ts) => ts.CheckInAsync(id, 1000 + i));

        var started = await WithServices((_, ts) => ts.StartDraftAsync(id, shuffleOrder: true));
        Assert.True(started.Ok, started.Error);

        // Picks via serviço (contexto novo a cada pick)
        for (var guard = 0; guard < 30; guard++)
        {
            var state = await WithServices((_, ts) => ts.GetAsync(id));
            if (state!.Status != TournamentStatus.Drafting) break;

            var team = state.CurrentTeam!;
            var choice = state.EligibleFor(team).First();
            var pick = await WithServices((_, ts) => ts.PickAsync(id, team.Id, choice.Id));
            Assert.True(pick.Ok, pick.Error);
        }

        var drafted = await WithServices((_, ts) => ts.GetAsync(id));
        Assert.Equal(TournamentStatus.Drafted, drafted!.Status);
        Assert.Equal(16, drafted.Picks.Count);
        Assert.All(drafted.Teams, team =>
        {
            Assert.Equal(5, team.Members.Count);
            Assert.Equal(5, team.Members.Select(m => m.AssignedRole).Distinct().Count());
        });

        // Chave + resultados
        Assert.True((await WithServices((_, ts) => ts.GenerateBracketAsync(id, shuffleSeeds: false))).Ok);

        for (var guard = 0; guard < 30; guard++)
        {
            var state = await WithServices((_, ts) => ts.GetAsync(id));
            if (state!.Status == TournamentStatus.Finished) break;
            var match = state.Matches.OrderBy(m => m.Number).First(m => m.IsReady);
            var r = await WithServices((_, ts) => ts.ReportGameAsync(id, match.Number, match.TeamAId!.Value));
            Assert.True(r.Ok, r.Error);
        }

        var done = await WithServices((_, ts) => ts.GetAsync(id));
        Assert.Equal(TournamentStatus.Finished, done!.Status);
        var final = done.Matches.Single(m => m.Round == m.TotalRounds);
        Assert.Equal(SeriesFormat.Bo5, final.Format);
        Assert.Equal(3, final.WinsA);
        Assert.NotNull(done.Champion);
    }

    [Fact]
    public async Task Expired_pick_is_auto_picked()
    {
        var roles = LolRoleExtensions.All;
        for (ulong i = 0; i < 10; i++)
            await WithServices((p, _) => p.UpsertAsync(2000 + i, $"Q{i}", roles[(int)(i % 5)], fill: true));

        var id = (await WithServices((_, ts) => ts.CreateAsync(Guild, "Rapidinho", 2, SeriesFormat.Bo1))).Value!.Id;
        await WithServices((_, ts) => ts.ConfigureAsync(id, null, null, null, null, 15, null, false));
        for (ulong i = 0; i < 10; i++) await WithServices((_, ts) => ts.JoinAsync(id, 2000 + i));
        await WithServices((_, ts) => ts.SetCaptainAsync(id, 2000, true));
        await WithServices((_, ts) => ts.SetCaptainAsync(id, 2001, true));
        Assert.True((await WithServices((_, ts) => ts.StartDraftAsync(id, false))).Ok);

        // Força o prazo para o passado.
        await WithServices(async (_, ts) =>
        {
            await ts.UpdateDiscordRefsAsync(id, t => t.CurrentPickDeadline = DateTimeOffset.UtcNow.AddSeconds(-1));
            return true;
        });

        Assert.True(await WithServices((_, ts) => ts.AutoPickIfExpiredAsync(id)));
        var state = await WithServices((_, ts) => ts.GetAsync(id));
        Assert.Single(state!.Picks);
        Assert.True(state.Picks[0].AutoPicked);
    }

    [Fact]
    public async Task Riot_id_parsing_and_linking()
    {
        Assert.Equal(("Faker", "KR1"), PlayerService.ParseRiotId("Faker#KR1"));
        Assert.Null(PlayerService.ParseRiotId("semtag"));

        await WithServices((p, _) => p.UpsertAsync(3000, "Alguem", LolRole.Mid));
        await WithServices((p, _) => p.UpsertAsync(3001, "Outro", LolRole.Top));
        Assert.True((await WithServices((p, _) => p.LinkRiotAsync(3000, "Nome Legal#BR1"))).Ok);
        Assert.False((await WithServices((p, _) => p.LinkRiotAsync(3001, "nome legal#br1"))).Ok); // já usado

        var player = await WithServices((p, _) => p.FindByDiscordIdAsync(3000));
        Assert.Equal("Nome Legal#BR1", player!.Riot!.RiotId);
    }

    [Fact]
    public async Task Dev_populate_lets_one_human_run_a_whole_draft()
    {
        const ulong me = 555_555_555_555_555_555;
        await WithServices((p, _) => p.UpsertAsync(me, "Eu", LolRole.Jungle, LolRole.Mid));
        var id = (await WithServices((_, ts) => ts.CreateAsync(Guild, "Teste solo", 4, SeriesFormat.Bo3))).Value!.Id;
        Assert.True((await WithServices((_, ts) => ts.SetCaptainAsync(id, me, true))).Ok);

        var pop = await WithServices(async (_, ts) =>
        {
            await using var db = NewDb();
            var dev = new DevService(db, new TournamentService(db, new PlayerService(db), _locks, new NoopTournamentEvents()),
                _locks, new NoopTournamentEvents());
            return await dev.PopulateAsync(Guild, null, fillCaptains: true);
        });
        Assert.True(pop.Ok, pop.Error);

        var t = await WithServices((_, ts) => ts.GetAsync(id));
        Assert.Equal(20, t!.Entries.Count);
        Assert.Equal(4, t.Entries.Count(e => e.IsCaptain));
        Assert.Equal(19, t.Entries.Count(e => e.Player.IsFake));

        Assert.True((await WithServices((_, ts) => ts.OpenCheckInAsync(id))).Ok);
        await WithServices((_, ts) => ts.CheckInAsync(id, me));
        Assert.True((await WithServices((_, ts) => ts.StartDraftAsync(id, true))).Ok);

        for (var guard = 0; guard < 40; guard++)
        {
            var state = (await WithServices((_, ts) => ts.GetAsync(id)))!;
            if (state.Status != TournamentStatus.Drafting) break;
            var team = state.CurrentTeam!;
            if (state.IsCaptainOf(team, me))
            {
                Assert.False(state.IsBotTurnReady(DateTimeOffset.UtcNow.AddMinutes(1))); // humano nunca é auto rápido
                var choice = state.EligibleFor(team).First();
                Assert.True((await WithServices((_, ts) => ts.PickAsync(id, team.Id, choice.Id))).Ok);
            }
            else
            {
                Assert.True(state.IsBotTurnReady(DateTimeOffset.UtcNow.AddSeconds(5)));
                // Simula os 3s passando.
                await WithServices(async (_, ts) =>
                {
                    await ts.UpdateDiscordRefsAsync(id, x => x.CurrentPickDeadline = DateTimeOffset.UtcNow.AddSeconds(x.PickTimerSeconds - 5));
                    return true;
                });
                Assert.True(await WithServices((_, ts) => ts.AutoPickIfExpiredAsync(id)));
            }
        }

        var done = (await WithServices((_, ts) => ts.GetAsync(id)))!;
        Assert.Equal(TournamentStatus.Drafted, done.Status);
        var myTeam = done.Teams.Single(x => done.IsCaptainOf(x, me));
        Assert.Equal(5, myTeam.Members.Count);

        // Limpar com 🤖 em times precisa cancelar antes.
        async Task<LolDraft.Domain.Result<int>> Clear() => await WithServices(async (_, ts) =>
        {
            await using var db = NewDb();
            var dev = new DevService(db, new TournamentService(db, new PlayerService(db), _locks, new NoopTournamentEvents()),
                _locks, new NoopTournamentEvents());
            return await dev.ClearAsync(Guild);
        });
        Assert.False((await Clear()).Ok);
        Assert.True((await WithServices((_, ts) => ts.CancelAsync(id))).Ok);
        var cleared = await Clear();
        Assert.True(cleared.Ok, cleared.Error);
        Assert.Equal(19, cleared.Value);
    }

    [Fact]
    public async Task Discord_refs_are_saved_in_same_scope_as_create()
    {
        await using var db = NewDb();
        var players = new PlayerService(db);
        var ts = new TournamentService(db, players, _locks, new NoopTournamentEvents());
        var t = await ts.CreateAsync(Guild, "Refs", 4, SeriesFormat.Bo3);
        var cfg = await ts.ConfigureAsync(t.Value!.Id, null, null, null, null, 60, null, true);
        Assert.True(cfg.Ok);
        await ts.UpdateDiscordRefsAsync(t.Value.Id, x => { x.AnnouncementChannelId = 1234567890123456789; x.RegistrationMessageId = 987654321098765432; });

        var reloaded = await WithServices((_, s) => s.GetAsync(t.Value.Id));
        Assert.Equal(1234567890123456789UL, reloaded!.AnnouncementChannelId);
        Assert.Equal(987654321098765432UL, reloaded.RegistrationMessageId);
    }

    [Fact]
    public async Task Balance_via_service_saves_picks_with_real_team_ids()
    {
        var roles = LolRoleExtensions.All;
        for (ulong i = 0; i < 20; i++)
            await WithServices((p, _) => p.UpsertAsync(4000 + i, $"B{i}", roles[(int)(i % 5)], roles[(int)((i + 2) % 5)]));
        var id = (await WithServices((_, ts) => ts.CreateAsync(Guild, "Balanceado", 4, SeriesFormat.Bo1))).Value!.Id;
        await WithServices((_, ts) => ts.ConfigureAsync(id, null, null, null, null, null, null, false));
        for (ulong i = 0; i < 20; i++) await WithServices((_, ts) => ts.JoinAsync(id, 4000 + i));

        var r = await WithServices((_, ts) => ts.BalanceAsync(id));
        Assert.True(r.Ok, r.Error);

        var t = (await WithServices((_, ts) => ts.GetAsync(id)))!;
        Assert.Equal(TournamentStatus.Drafted, t.Status);
        var teamIds = t.Teams.Select(x => x.Id).ToHashSet();
        Assert.Equal(16, t.Picks.Count);
        Assert.All(t.Picks, p => Assert.Contains(p.TeamId, teamIds));
        Assert.All(t.Teams, team => Assert.Equal(5, team.Members.Count));
    }
}
