using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;

namespace LolDraft.Tests;

/// <summary>Monta campeonatos em memória com Ids preenchidos (como se viessem do banco).</summary>
public static class TestData
{
    private static int _nextId = 1;

    public static Player Player(string name, LolRole primary, LolRole? secondary = null, bool fill = false,
        string? tier = null, string? division = null)
    {
        var p = new Player
        {
            Id = _nextId++,
            DiscordUserId = (ulong)_nextId,
            DisplayName = name,
            PrimaryRole = primary,
            SecondaryRole = secondary,
            IsFill = fill,
        };
        if (tier != null) p.Riot = new RiotAccount { GameName = name, TagLine = "BR1", Tier = tier, Division = division };
        return p;
    }

    /// <summary>
    /// 20 jogadores: 4 por role, 4 capitães (um top, um jg, um mid, um adc).
    /// </summary>
    public static Tournament TwentyPlayers(int teamCount = 4)
    {
        var t = new Tournament { Id = 1, Name = "Copa Teste", TeamCount = teamCount, RequireCheckIn = false };
        var entryId = 100;
        var roles = LolRoleExtensions.All;

        for (var i = 0; i < teamCount * 5; i++)
        {
            var role = roles[i % 5];
            var player = Player($"{role.Label()}{i / 5 + 1}", role, roles[(i + 1) % 5], tier: "GOLD", division: (i % 4) switch { 0 => "I", 1 => "II", 2 => "III", _ => "IV" });
            var entry = t.Join(player).Value!;
            entry.Id = entryId++;
            entry.JoinedAt = DateTimeOffset.UnixEpoch.AddMinutes(i);
        }

        // Capitães: os 4 primeiros jogadores de "rodadas" diferentes e roles diferentes.
        var captains = t.Entries.Where((e, idx) => idx % 6 == 0).Take(teamCount).ToList();
        foreach (var c in captains) t.SetCaptain(c.PlayerId, true);
        return t;
    }

    /// <summary>Simula o save do EF: preenche Ids dos times criados no StartDraft.</summary>
    public static void AssignTeamIds(Tournament t)
    {
        var id = 500;
        foreach (var team in t.Teams) team.Id = id++;
    }

    public static void AssignMatchIds(Tournament t)
    {
        var id = 900;
        foreach (var m in t.Matches) m.Id = id++;
    }

    public static List<Team> Teams(int count)
    {
        return Enumerable.Range(1, count).Select(i => new Team { Id = i, Name = $"Time {i}", DraftPosition = i }).ToList();
    }
}
