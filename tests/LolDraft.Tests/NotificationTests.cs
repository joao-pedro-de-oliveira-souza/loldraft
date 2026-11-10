using LolDraft.Domain.Tournaments;
using LolDraft.Host.Bot;
using Xunit;

namespace LolDraft.Tests;

public class NotificationTests
{
    private static Tournament Drafted()
    {
        var t = TestData.TwentyPlayers();
        t.StartDraft(DateTimeOffset.UtcNow);
        TestData.AssignTeamIds(t);
        while (t.Status == TournamentStatus.Drafting) t.AutoPick(DateTimeOffset.UtcNow);
        return t;
    }

    [Fact]
    public void Picked_dm_says_team_captain_and_role()
    {
        var t = Drafted();
        var pick = t.Picks.First();
        var team = t.Teams.Single(x => x.Id == pick.TeamId);
        var entry = t.Entries.Single(e => e.Id == pick.EntryId);
        var captain = t.Entries.Single(e => e.Id == team.CaptainEntryId);

        var embed = DraftNotifier.PickedEmbed(t, team, entry, pick);

        Assert.Contains(team.Name, embed.Title);
        Assert.Contains(t.Name, embed.Description);
        Assert.Equal(captain.Player.DisplayName, embed.Fields.Single(f => f.Name == "Capitão").Value);
        Assert.Contains(LolDraft.Domain.Players.LolRoleExtensions.Label(entry.AssignedRole!.Value),
            embed.Fields.Single(f => f.Name == "Sua role").Value);
        Assert.StartsWith($"#1 de {t.TotalPicks}", embed.Fields.Single(f => f.Name == "Pick").Value);
    }

    [Fact]
    public void Team_dm_lists_the_whole_roster_and_marks_the_player()
    {
        var t = Drafted();
        var team = t.Teams.First();
        var me = team.Members.Last();

        var embed = DraftNotifier.TeamEmbed(t, team, me);

        Assert.Contains(team.Name, embed.Title);
        foreach (var m in team.Members) Assert.Contains(m.Player.DisplayName, embed.Description);
        Assert.Contains($"**{me.Player.DisplayName}**", embed.Description);
        Assert.Single(embed.Description.Split('\n'), l => l.Contains("← você"));
        Assert.Contains("👑", embed.Description);
    }
}
