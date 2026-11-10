using System.Net;
using System.Text;
using LolDraft.Application.Data;
using LolDraft.Application.Players;
using LolDraft.Application.Riot;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LolDraft.Tests;

/// <summary>Riot API falsa: contas, ícones e elos em memória.</summary>
public class FakeRiot : IRiotApi
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, RiotAccountDto> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Icons { get; } = new();
    public Dictionary<string, List<RiotLeagueEntryDto>> Leagues { get; } = new();

    public void Add(string riotId, string puuid, int icon, string? tier = null, string rank = "II", int lp = 50)
    {
        var parts = riotId.Split('#');
        Accounts[riotId] = new RiotAccountDto(puuid, parts[0], parts[1]);
        Icons[puuid] = icon;
        Leagues[puuid] = tier == null ? [] : [new RiotLeagueEntryDto("RANKED_SOLO_5x5", tier, rank, lp, 10, 5)];
    }

    public Task<RiotAccountDto?> GetAccountByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default) =>
        Task.FromResult(Accounts.GetValueOrDefault($"{gameName}#{tagLine}"));

    public Task<RiotSummonerDto?> GetSummonerByPuuidAsync(string puuid, CancellationToken ct = default) =>
        Task.FromResult(Icons.TryGetValue(puuid, out var icon) ? new RiotSummonerDto(puuid, icon, 300) : null);

    public Task<IReadOnlyList<RiotLeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RiotLeagueEntryDto>>(Leagues.GetValueOrDefault(puuid) ?? []);
}

public sealed class RiotTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly ServiceProvider _sp;
    private readonly FakeRiot _riot = new();
    private readonly RiotLinkService _link;

    public RiotTests()
    {
        _conn.Open();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
        services.AddScoped<PlayerService>();
        _sp = services.BuildServiceProvider();
        using (var scope = _sp.CreateScope()) scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        _link = new RiotLinkService(_riot, _sp.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() { _sp.Dispose(); _conn.Dispose(); }

    private async Task<Player> Register(ulong id, string name)
    {
        using var scope = _sp.CreateScope();
        var (p, _) = await scope.ServiceProvider.GetRequiredService<PlayerService>().UpsertAsync(id, name, LolRole.Mid);
        return p;
    }

    private async Task<Player?> Load(ulong id)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlayerService>().FindByDiscordIdAsync(id);
    }

    [Fact]
    public async Task Link_pulls_elo_right_away_and_sync_updates_it()
    {
        await Register(1000000001, "JP");
        _riot.Add("Travesso#BR1", "puuid-jp", icon: 4567, tier: "PLATINUM", rank: "II", lp: 37);

        var linked = await _link.StartAsync(1000000001, "travesso#br1");
        Assert.True(linked.Ok, linked.Error);

        var p = (await Load(1000000001))!;
        Assert.Equal("Travesso#BR1", p.Riot!.RiotId); // grafia oficial da Riot
        Assert.Equal("PLATINUM", p.Riot.Tier);
        Assert.Equal("II", p.Riot.Division);
        Assert.Equal(37, p.Riot.LeaguePoints);
        Assert.NotNull(p.RatingScore);

        _riot.Leagues["puuid-jp"] = [new RiotLeagueEntryDto("RANKED_SOLO_5x5", "EMERALD", "IV", 10, 30, 20)];
        var (updated, failed, error) = await _link.SyncAllAsync();
        Assert.Equal(1, updated);
        Assert.Equal(0, failed);
        Assert.Null(error);
        Assert.Equal("EMERALD", (await Load(1000000001))!.Riot!.Tier);

        _riot.Leagues["puuid-jp"] = [new RiotLeagueEntryDto("RANKED_SOLO_5x5", "DIAMOND", "IV", 0, 40, 20)];
        Assert.True((await _link.SyncOneAsync(1000000001)).Ok);
        Assert.Equal("DIAMOND", (await Load(1000000001))!.Riot!.Tier);
    }

    [Fact]
    public async Task Unranked_account_links_without_elo()
    {
        await Register(1000000001, "A");
        _riot.Add("SemRank#BR1", "puuid-u", icon: 5);
        var r = await _link.StartAsync(1000000001, "SemRank#BR1");
        Assert.True(r.Ok, r.Error);
        Assert.Null((await Load(1000000001))!.RatingScore);
    }

    [Fact]
    public async Task Same_account_cannot_be_linked_by_two_players()
    {
        await Register(1000000001, "A");
        await Register(1000000002, "B");
        _riot.Add("Conta#BR1", "puuid-x", icon: 10);

        Assert.True((await _link.StartAsync(1000000001, "Conta#BR1")).Ok);
        var second = await _link.StartAsync(1000000002, "Conta#BR1");
        Assert.False(second.Ok);
    }

    [Fact]
    public async Task Unknown_account_fails_and_sync_without_link_fails()
    {
        await Register(1000000001, "A");
        Assert.False((await _link.StartAsync(1000000001, "Ninguem#BR1")).Ok);
        Assert.False((await _link.SyncOneAsync(1000000001)).Ok);
    }

    [Fact]
    public async Task Without_api_key_just_stores_riot_id()
    {
        _riot.Enabled = false;
        await Register(1000000001, "A");
        var r = await _link.StartAsync(1000000001, "Qualquer#BR1");
        Assert.True(r.Ok, r.Error);
        var p = (await Load(1000000001))!;
        Assert.Equal("Qualquer#BR1", p.Riot!.RiotId);
        Assert.Null(p.Riot.Puuid);
    }

    [Fact]
    public async Task Riot_client_sends_key_and_maps_errors()
    {
        var handler = new StubHandler();
        var client = new RiotApiClient(new HttpClient(handler),
            Options.Create(new RiotOptions { ApiKey = "RGAPI-test", Platform = "br1", Region = "americas" }));

        handler.Respond(HttpStatusCode.OK, """{"puuid":"p1","gameName":"Faker","tagLine":"KR1"}""");
        var acc = await client.GetAccountByRiotIdAsync("Faker", "KR1");
        Assert.Equal("p1", acc!.Puuid);
        Assert.Equal("RGAPI-test", handler.LastKey);
        Assert.Equal("https://americas.api.riotgames.com/riot/account/v1/accounts/by-riot-id/Faker/KR1", handler.LastUrl);

        handler.Respond(HttpStatusCode.OK, """[{"queueType":"RANKED_SOLO_5x5","tier":"GOLD","rank":"I","leaguePoints":12,"wins":1,"losses":2}]""");
        var leagues = await client.GetLeagueEntriesAsync("p1");
        Assert.Equal("GOLD", leagues.Single().Tier);
        Assert.StartsWith("https://br1.api.riotgames.com/lol/league/v4/entries/by-puuid/", handler.LastUrl);

        handler.Respond(HttpStatusCode.NotFound, "{}");
        Assert.Null(await client.GetSummonerByPuuidAsync("nope"));

        handler.Respond(HttpStatusCode.Forbidden, "{}");
        await Assert.ThrowsAsync<RiotApiException>(() => client.GetSummonerByPuuidAsync("p1"));
    }

    [Fact]
    public void Auto_balance_builds_teams_with_all_roles_and_close_ratings()
    {
        var t = TestData.TwentyPlayers();
        foreach (var e in t.Entries) e.IsCaptain = false; // sem capitães: o balanceamento escolhe
        var r = t.AutoBalance(DateTimeOffset.UtcNow);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(TournamentStatus.Drafted, t.Status);
        Assert.All(t.Teams, team =>
        {
            Assert.Equal(5, team.Members.Count);
            Assert.Equal(5, team.Members.Select(m => m.AssignedRole).Distinct().Count());
        });
        var averages = t.Teams.Select(x => t.TeamAverageRating(x)!.Value).ToList();
        Assert.True(averages.Max() - averages.Min() <= 150, $"médias muito distantes: {string.Join(", ", averages)}");
    }

    [Fact]
    public void Elo_labels()
    {
        Assert.Equal("Ouro II", EloScore.Label(EloScore.From("GOLD", "II", 40)!.Value));
        Assert.Equal("Mestre", EloScore.Label(EloScore.From("MASTER", null, 200)!.Value));
        Assert.Equal("Ferro IV", EloScore.Label(0));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status;
        private string _body = "";
        public string? LastUrl { get; private set; }
        public string? LastKey { get; private set; }

        public void Respond(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastKey = request.Headers.TryGetValues("X-Riot-Token", out var v) ? v.Single() : null;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
        }
    }
}
