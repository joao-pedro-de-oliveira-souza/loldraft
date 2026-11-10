using LolDraft.Application.Data;
using LolDraft.Application.Tournaments;
using LolDraft.Domain;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using Microsoft.EntityFrameworkCore;

namespace LolDraft.Application.Dev;

/// <summary>Ferramentas de teste: jogadores falsos (🤖) para simular um campeonato sozinho.</summary>
public class DevService(AppDbContext db, TournamentService tournaments, TournamentLocks locks, ITournamentEvents events)
{
    private static readonly string[] Names =
    [
        "Faker", "Caps", "Chovy", "Ruler", "Keria", "Zeus", "Oner", "Gumayusi", "Canyon", "ShowMaker",
        "Peanut", "Bin", "Knight", "Viper", "Meiko", "Brance", "Robo", "Aegis", "Tinowns", "Route",
        "Titan", "Croc", "Kiari", "Jojo", "Wizer", "Ceos", "Takeshi", "Netuno", "Minerva", "Guigo",
        "Ranger", "Trigo", "Grevthar", "Envy", "Kabbie", "Ayel", "Shini", "Boal", "Tay", "Dynquedo"
    ];

    private static readonly (string Tier, string Div)[] Elos =
    [
        ("SILVER", "II"), ("GOLD", "IV"), ("GOLD", "II"), ("GOLD", "I"), ("PLATINUM", "III"),
        ("PLATINUM", "I"), ("EMERALD", "IV"), ("EMERALD", "II"), ("DIAMOND", "IV"), ("DIAMOND", "II")
    ];

    /// <summary>
    /// Cria jogadores de teste e inscreve no campeonato ativo, já com presença confirmada.
    /// Completa as vagas de capitão que faltarem com 🤖.
    /// </summary>
    public async Task<Result<string>> PopulateAsync(ulong guildId, int? quantity, bool fillCaptains, CancellationToken ct = default)
    {
        var active = await tournaments.GetActiveAsync(guildId, ct);
        if (active == null) return Result<string>.Fail("Crie um campeonato antes (`!camp criar <times> <formato> <nome>`).");
        if (active.Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result<string>.Fail("Só dá para adicionar jogadores antes do draft.");

        var missing = Math.Max(0, active.RequiredPlayers - active.Entries.Count);
        var count = quantity ?? missing;
        if (count <= 0) return Result<string>.Fail("O campeonato já tem jogadores suficientes.");
        if (count > 150) return Result<string>.Fail("Máximo de 150 por vez.");

        // SQLite não traduz comparação de ulong (<); a tabela é pequena, filtra em memória.
        var maxFake = (await db.Players.Select(p => p.DiscordUserId).ToListAsync(ct))
            .Where(id => id < Player.FakeIdLimit).DefaultIfEmpty(0UL).Max();

        // Distribui roles equilibradas considerando quem já está inscrito.
        var roleCounts = LolRoleExtensions.All.ToDictionary(r => r, r => active.Entries.Count(e => e.Player.PrimaryRole == r));
        var rng = new Random();
        var created = new List<Player>();
        for (var i = 0; i < count; i++)
        {
            var id = maxFake + 1 + (ulong)i;
            var primary = roleCounts.OrderBy(kv => kv.Value).ThenBy(_ => rng.Next()).First().Key;
            roleCounts[primary]++;
            var secondary = LolRoleExtensions.All.Where(r => r != primary).OrderBy(_ => rng.Next()).First();
            var (tier, div) = Elos[rng.Next(Elos.Length)];
            var name = $"{Names[(int)(id - 1) % Names.Length]}{((id - 1) / (ulong)Names.Length is var n && n > 0 ? n.ToString() : "")}";

            created.Add(new Player
            {
                DiscordUserId = id,
                DisplayName = name,
                PrimaryRole = primary,
                SecondaryRole = rng.Next(4) == 0 ? null : secondary,
                IsFill = rng.Next(5) == 0,
                Riot = new RiotAccount
                {
                    GameName = name, TagLine = "BOT", Tier = tier, Division = div, LeaguePoints = rng.Next(0, 100)
                },
            });
        }
        db.Players.AddRange(created);
        await db.SaveChangesAsync(ct);

        int captainsAdded;
        Tournament updated;
        using (await locks.AcquireAsync(active.Id, ct))
        {
            var t = (await tournaments.GetAsync(active.Id, ct))!;
            foreach (var p in created)
            {
                var joined = t.Join(p);
                if (joined.Ok) joined.Value!.CheckedIn = true;
            }

            captainsAdded = 0;
            if (fillCaptains)
            {
                var need = t.TeamCount - t.Entries.Count(e => e.IsCaptain);
                foreach (var e in t.Entries.Where(e => e.Player.IsFake && !e.IsCaptain)
                             .OrderByDescending(e => e.Player.RatingScore).Take(Math.Max(0, need)))
                {
                    e.IsCaptain = true;
                    captainsAdded++;
                }
            }
            await db.SaveChangesAsync(ct);
            updated = t;
        }

        await events.TournamentChangedAsync(updated, "dev-populate");
        return Result<string>.Success(
            $"🤖 {created.Count} jogadores de teste inscritos (presença confirmada)" +
            (captainsAdded > 0 ? $", {captainsAdded} viraram capitães" : "") +
            $". Total: {updated.Entries.Count}/{updated.RequiredPlayers}.");
    }

    /// <summary>Apaga todos os jogadores de teste (e as inscrições deles).</summary>
    public async Task<Result<int>> ClearAsync(ulong guildId, CancellationToken ct = default)
    {
        var active = await tournaments.GetActiveAsync(guildId, ct);
        if (active != null && active.Entries.Any(e => e.Player.IsFake)
            && active.Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result<int>.Fail("O campeonato ativo tem 🤖 em times. Cancele-o antes (`!camp cancelar`).");

        var fakes = (await db.Players.ToListAsync(ct)).Where(p => p.IsFake).ToList();
        var ids = fakes.Select(p => p.Id).ToList();

        // Inscrições em campeonatos encerrados/cancelados: remove junto (histórico de teste).
        var entries = await db.Entries.Where(e => ids.Contains(e.PlayerId)).ToListAsync(ct);
        db.Entries.RemoveRange(entries);
        db.Players.RemoveRange(fakes);
        await db.SaveChangesAsync(ct);

        if (active != null)
        {
            var refreshed = await tournaments.GetAsync(active.Id, ct);
            if (refreshed != null) await events.TournamentChangedAsync(refreshed, "dev-clear");
        }
        return Result<int>.Success(fakes.Count);
    }
}
