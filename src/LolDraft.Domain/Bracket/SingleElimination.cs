using LolDraft.Domain.Tournaments;

namespace LolDraft.Domain.Bracket;

/// <summary>
/// Chave de eliminação simples para qualquer número de times (byes automáticos
/// para os melhores seeds quando não é potência de 2).
/// </summary>
public static class SingleElimination
{
    /// <param name="seeds">Times em ordem de seed (índice 0 = seed 1).</param>
    /// <param name="formatForRound">(rodada, totalRodadas) → formato da série.</param>
    public static List<Match> Generate(IReadOnlyList<Team> seeds, Func<int, int, SeriesFormat> formatForRound)
    {
        if (seeds.Count < 2) throw new ArgumentException("A chave precisa de pelo menos 2 times.", nameof(seeds));

        var size = 1;
        while (size < seeds.Count) size *= 2;
        var totalRounds = (int)Math.Log2(size);

        // Monta todas as rodadas, de trás pra frente não precisa: cria e liga.
        var rounds = new List<List<Match>>();
        var number = 1;
        for (var round = 1; round <= totalRounds; round++)
        {
            var count = size >> round;
            var list = new List<Match>(count);
            for (var pos = 0; pos < count; pos++)
            {
                list.Add(new Match
                {
                    Number = number++,
                    Round = round,
                    TotalRounds = totalRounds,
                    Position = pos,
                    Format = formatForRound(round, totalRounds),
                });
            }
            rounds.Add(list);
        }

        for (var r = 0; r < rounds.Count - 1; r++)
        {
            foreach (var match in rounds[r])
            {
                match.NextMatch = rounds[r + 1][match.Position / 2];
                match.NextSlot = match.Position % 2 == 0 ? MatchSlot.A : MatchSlot.B;
            }
        }

        // Distribui os seeds na primeira rodada (1 x 8, 4 x 5, 2 x 7, 3 x 6...).
        var order = SeedOrder(size);
        var first = rounds[0];
        for (var i = 0; i < first.Count; i++)
        {
            var seedA = order[i * 2];
            var seedB = order[i * 2 + 1];
            first[i].TeamA = seedA <= seeds.Count ? seeds[seedA - 1] : null;
            first[i].TeamB = seedB <= seeds.Count ? seeds[seedB - 1] : null;
            first[i].TeamAId = first[i].TeamA?.Id is > 0 ? first[i].TeamA!.Id : null;
            first[i].TeamBId = first[i].TeamB?.Id is > 0 ? first[i].TeamB!.Id : null;
        }

        // Resolve byes: quem não tem adversário avança.
        foreach (var match in first)
        {
            if (match.TeamA != null && match.TeamB == null) match.Finish(match.TeamA, bye: true);
            else if (match.TeamB != null && match.TeamA == null) match.Finish(match.TeamB, bye: true);
        }

        return rounds.SelectMany(r => r).ToList();
    }

    /// <summary>
    /// Ordem padrão de seeds para uma chave de tamanho potência de 2.
    /// size=8 → [1,8,4,5,2,7,3,6]: os seeds 1 e 2 só se encontram na final.
    /// </summary>
    public static int[] SeedOrder(int size)
    {
        var order = new List<int> { 1 };
        while (order.Count < size)
        {
            var n = order.Count * 2;
            order = order.SelectMany(s => new[] { s, n + 1 - s }).ToList();
        }
        return order.ToArray();
    }
}
