using LolDraft.Domain.Draft;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Xunit;

namespace LolDraft.Tests;

public class DraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snake_order_reverses_every_round()
    {
        var order = DraftOrder.Snake(4, 4);
        Assert.Equal(new[] { 1, 2, 3, 4, 4, 3, 2, 1, 1, 2, 3, 4, 4, 3, 2, 1 }, order.ToArray());
    }

    [Fact]
    public void Role_assignment_prefers_primary_roles()
    {
        var a = TestData.Player("A", LolRole.Mid, LolRole.Top);
        var b = TestData.Player("B", LolRole.Top, LolRole.Mid);
        var solved = RoleAssignment.Solve([a, b])!;
        Assert.Equal(LolRole.Mid, solved[a]);
        Assert.Equal(LolRole.Top, solved[b]);
    }

    [Fact]
    public void Role_assignment_uses_secondary_when_primary_is_taken()
    {
        var a = TestData.Player("A", LolRole.Mid);
        var b = TestData.Player("B", LolRole.Mid, LolRole.Support);
        var solved = RoleAssignment.Solve([a, b])!;
        Assert.Equal(LolRole.Mid, solved[a]);
        Assert.Equal(LolRole.Support, solved[b]);
    }

    [Fact]
    public void Role_assignment_is_impossible_with_two_one_tricks()
    {
        var a = TestData.Player("A", LolRole.Mid);
        var b = TestData.Player("B", LolRole.Mid);
        Assert.Null(RoleAssignment.Solve([a, b]));
    }

    [Fact]
    public void Start_draft_requires_one_captain_per_team()
    {
        var t = TestData.TwentyPlayers();
        t.SetCaptain(t.Entries.First(e => e.IsCaptain).PlayerId, false);
        var result = t.StartDraft(Now);
        Assert.False(result.Ok);
        Assert.Equal(TournamentStatus.Registration, t.Status);
    }

    [Fact]
    public void Start_draft_creates_teams_with_captains()
    {
        var t = TestData.TwentyPlayers();
        Assert.True(t.StartDraft(Now).Ok);
        Assert.Equal(TournamentStatus.Drafting, t.Status);
        Assert.Equal(4, t.Teams.Count);
        Assert.All(t.Teams, team => Assert.Single(team.Members));
        Assert.Equal(16, t.TotalPicks);
        Assert.Equal(16, t.AvailablePool.Count());
    }

    [Fact]
    public void Picking_out_of_turn_fails()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(Now);
        TestData.AssignTeamIds(t);

        var second = t.Teams.Single(x => x.DraftPosition == 2);
        var anyone = t.AvailablePool.First();
        var result = t.Pick(second.Id, anyone.Id, Now);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Cannot_pick_player_for_a_role_the_team_already_has()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(Now);
        TestData.AssignTeamIds(t);

        var team = t.CurrentTeam!;
        var captainRole = team.Members.Single().Player.PrimaryRole;
        // Cria um "one trick" da mesma role do capitão, sem secundária nem fill.
        var oneTrick = TestData.Player("OneTrick", captainRole);
        var entry = new TournamentEntry { Id = 999, Player = oneTrick, PlayerId = oneTrick.Id, CheckedIn = true };
        t.Entries.Add(entry);

        Assert.False(t.CanTeamPick(team, entry));
        Assert.False(t.Pick(team.Id, entry.Id, Now).Ok);
    }

    [Fact]
    public void Full_auto_draft_produces_complete_teams_with_all_roles()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(Now);
        TestData.AssignTeamIds(t);

        var guard = 0;
        while (t.Status == TournamentStatus.Drafting && guard++ < 100)
            Assert.True(t.AutoPick(Now).Ok);

        Assert.Equal(TournamentStatus.Drafted, t.Status);
        Assert.Equal(16, t.Picks.Count);
        foreach (var team in t.Teams)
        {
            Assert.Equal(5, team.Members.Count);
            var roles = team.Members.Select(m => m.AssignedRole).ToList();
            Assert.DoesNotContain(null, roles);
            Assert.Equal(5, roles.Distinct().Count());
        }
    }

    [Fact]
    public void Pick_does_not_steal_the_last_support_needed_by_another_team()
    {
        // 2 times. Capitães: Top e Mid. Pool: 2 supports (um deles flex Jungle), jungle, adc...
        var t = new Tournament { Id = 2, TeamCount = 2, RequireCheckIn = false };
        var id = 1;
        TournamentEntry Add(Player p, bool captain = false)
        {
            var e = t.Join(p).Value!;
            e.Id = id++;
            e.IsCaptain = captain;
            return e;
        }

        Add(TestData.Player("CapTop", LolRole.Top), captain: true);
        Add(TestData.Player("CapMid", LolRole.Mid), captain: true);
        Add(TestData.Player("Top2", LolRole.Top));
        Add(TestData.Player("Mid2", LolRole.Mid));
        Add(TestData.Player("Jg1", LolRole.Jungle));
        Add(TestData.Player("Adc1", LolRole.Adc));
        Add(TestData.Player("Adc2", LolRole.Adc));
        var supOnly = Add(TestData.Player("SupOnly", LolRole.Support));
        var supFlex = Add(TestData.Player("SupFlex", LolRole.Support, LolRole.Jungle));
        Add(TestData.Player("Sup2", LolRole.Support, LolRole.Jungle)); // jungle extra p/ fechar

        Assert.True(t.StartDraft(Now).Ok);
        TestData.AssignTeamIds(t);

        // Os dois jungles possíveis além de Jg1 são flex de suporte. Todos devem continuar elegíveis
        // enquanto der para fechar tudo; o draft automático tem que terminar sem violar roles.
        var guard = 0;
        while (t.Status == TournamentStatus.Drafting && guard++ < 20)
            Assert.True(t.AutoPick(Now).Ok);

        Assert.Equal(TournamentStatus.Drafted, t.Status);
        foreach (var team in t.Teams)
            Assert.Equal(5, team.Members.Select(m => m.AssignedRole).Distinct().Count());
    }

    [Fact]
    public void Pick_timer_expires()
    {
        var t = TestData.TwentyPlayers();
        t.PickTimerSeconds = 30;
        t.StartDraft(Now);
        Assert.False(t.IsPickExpired(Now.AddSeconds(29)));
        Assert.True(t.IsPickExpired(Now.AddSeconds(30)));
    }

    [Fact]
    public void Elo_score_orders_tiers_and_divisions()
    {
        Assert.True(EloScore.From("GOLD", "I", 0) > EloScore.From("GOLD", "II", 99));
        Assert.True(EloScore.From("PLATINUM", "IV", 0) > EloScore.From("GOLD", "I", 99));
        Assert.True(EloScore.From("MASTER", null, 10) > EloScore.From("DIAMOND", "I", 99));
        Assert.Null(EloScore.From(null, null, null));
    }
}
