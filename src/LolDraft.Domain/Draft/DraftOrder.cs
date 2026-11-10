namespace LolDraft.Domain.Draft;

public static class DraftOrder
{
    /// <summary>
    /// Ordem "cobrinha": 1-2-3-4, 4-3-2-1, 1-2-3-4...
    /// Retorna a lista de posições de draft (1-based) para cada pick.
    /// </summary>
    public static IReadOnlyList<int> Snake(int teamCount, int rounds)
    {
        if (teamCount < 2) throw new ArgumentOutOfRangeException(nameof(teamCount), "Mínimo de 2 times.");
        if (rounds < 1) throw new ArgumentOutOfRangeException(nameof(rounds));

        var order = new List<int>(teamCount * rounds);
        for (var round = 0; round < rounds; round++)
        {
            var positions = Enumerable.Range(1, teamCount);
            order.AddRange(round % 2 == 0 ? positions : positions.Reverse());
        }
        return order;
    }
}
