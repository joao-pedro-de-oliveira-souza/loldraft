using LolDraft.Domain.Bracket;
using LolDraft.Domain.Tournaments;
using Xunit;

namespace LolDraft.Tests;

public class BracketTests
{
    private static SeriesFormat Bo3(int round, int total) => SeriesFormat.Bo3;

    [Fact]
    public void Seed_order_keeps_top_seeds_apart()
    {
        Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, SingleElimination.SeedOrder(8));
    }

    [Fact]
    public void Four_teams_make_semis_and_final()
    {
        var matches = SingleElimination.Generate(TestData.Teams(4), Bo3);
        Assert.Equal(3, matches.Count);
        Assert.Equal(2, matches.Count(m => m.RoundName == "Semifinal"));
        Assert.Single(matches, m => m.RoundName == "Final");
    }

    [Fact]
    public void Six_teams_give_byes_to_top_two_seeds()
    {
        var teams = TestData.Teams(6);
        var matches = SingleElimination.Generate(teams, Bo3);

        Assert.Equal(7, matches.Count); // chave de 8
        var byes = matches.Where(m => m.IsBye).ToList();
        Assert.Equal(2, byes.Count);
        Assert.Contains(byes, m => m.Winner == teams[0]);
        Assert.Contains(byes, m => m.Winner == teams[1]);

        // Os seeds 1 e 2 já aparecem na semifinal.
        var semis = matches.Where(m => m.Round == 2).ToList();
        Assert.Contains(semis, m => m.TeamA == teams[0] || m.TeamB == teams[0]);
        Assert.Contains(semis, m => m.TeamA == teams[1] || m.TeamB == teams[1]);
    }

    [Fact]
    public void Bo3_needs_two_wins_and_advances_winner()
    {
        var teams = TestData.Teams(4);
        var matches = SingleElimination.Generate(teams, Bo3);
        var semi = matches.First(m => m.Round == 1);
        var a = semi.TeamA!;

        Assert.True(semi.ReportGame(a.Id).Ok);
        Assert.False(semi.IsFinished);
        Assert.True(semi.ReportGame(a.Id).Ok);
        Assert.True(semi.IsFinished);
        Assert.Equal(a, semi.Winner);

        var final = matches.Single(m => m.Round == 2);
        Assert.Equal(a, final.TeamA);
    }

    [Fact]
    public void Formats_per_round_apply_final_and_semifinal_overrides()
    {
        var t = new Tournament { DefaultFormat = SeriesFormat.Bo1, SemifinalFormat = SeriesFormat.Bo3, FinalFormat = SeriesFormat.Bo5 };
        var matches = SingleElimination.Generate(TestData.Teams(8), t.FormatForRound);
        Assert.All(matches.Where(m => m.Round == 1), m => Assert.Equal(SeriesFormat.Bo1, m.Format));
        Assert.All(matches.Where(m => m.Round == 2), m => Assert.Equal(SeriesFormat.Bo3, m.Format));
        Assert.Equal(SeriesFormat.Bo5, matches.Single(m => m.Round == 3).Format);
    }

    [Fact]
    public void Change_format_mid_series_respects_score()
    {
        var matches = SingleElimination.Generate(TestData.Teams(2), (_, _) => SeriesFormat.Bo5);
        var final = matches.Single();
        final.ReportGame(final.TeamA!.Id);
        final.ReportGame(final.TeamA!.Id);

        Assert.False(final.ChangeFormat(SeriesFormat.Bo1).Ok); // 2 vitórias já fechariam MD1
        Assert.True(final.ChangeFormat(SeriesFormat.Bo5).Ok);
        Assert.False(final.ChangeFormat(SeriesFormat.Bo3).Ok); // 2 vitórias já fechariam MD3
    }

    [Fact]
    public void Full_tournament_finishes_with_champion()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(DateTimeOffset.UtcNow);
        TestData.AssignTeamIds(t);
        while (t.Status == TournamentStatus.Drafting) t.AutoPick(DateTimeOffset.UtcNow);

        Assert.True(t.GenerateBracket().Ok);
        TestData.AssignMatchIds(t);

        var guard = 0;
        while (t.Status == TournamentStatus.Running && guard++ < 50)
        {
            var next = t.Matches.First(m => m.IsReady);
            Assert.True(t.ReportGame(next.Number, next.TeamA!.Id).Ok);
        }

        Assert.Equal(TournamentStatus.Finished, t.Status);
        Assert.NotNull(t.Champion);
    }

    private static Tournament RunningTournament()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(DateTimeOffset.UtcNow);
        TestData.AssignTeamIds(t);
        while (t.Status == TournamentStatus.Drafting) t.AutoPick(DateTimeOffset.UtcNow);
        t.GenerateBracket();
        return t;
    }

    [Fact]
    public void Admin_score_fix_can_finish_series_and_advance()
    {
        var t = RunningTournament();
        var semi = t.ReadyMatches.First();
        Assert.False(t.SetScore(semi.Number, 2, 2).Ok); // os dois com 2 numa MD3 é inválido
        Assert.True(t.SetScore(semi.Number, 1, 0).Ok);
        Assert.False(semi.IsFinished);
        Assert.True(t.SetScore(semi.Number, 1, 2).Ok);
        Assert.True(semi.IsFinished);
        Assert.Equal(semi.TeamB, semi.Winner);
        Assert.Equal(semi.TeamB, t.Matches.Single(m => m.Round == 2).TeamB ?? t.Matches.Single(m => m.Round == 2).TeamA);
    }

    [Fact]
    public void Change_format_of_specific_match()
    {
        var t = RunningTournament();
        var final = t.Matches.Single(m => m.Round == 2);
        Assert.True(t.ChangeMatchFormat(final.Number, SeriesFormat.Bo5).Ok);
        Assert.Equal(SeriesFormat.Bo5, final.Format);
        Assert.False(t.ChangeMatchFormat(99, SeriesFormat.Bo1).Ok);
    }

    [Fact]
    public void Rename_team_validates_names()
    {
        var t = RunningTournament();
        Assert.True(t.RenameTeam(1, "Tigres de Cuiabá").Ok);
        Assert.Equal("Tigres de Cuiabá", t.Teams.Single(x => x.DraftPosition == 1).Name);
        Assert.False(t.RenameTeam(2, "tigres de cuiabá").Ok); // duplicado
        Assert.False(t.RenameTeam(9, "Inexistente").Ok);
        Assert.False(t.RenameTeam(2, "x").Ok);
    }

    [Fact]
    public void Slug_for_channel_names()
    {
        Assert.Equal("tigres-de-cuiaba", LolDraft.Host.Bot.DiscordResources.Slug("Tigres de Cuiabá!"));
        Assert.Equal("time", LolDraft.Host.Bot.DiscordResources.Slug("🔥🔥"));
    }
}
