using LolDraft.Domain.Players;

namespace LolDraft.Domain.Draft;

/// <summary>
/// Verifica se os jogadores que sobraram ainda conseguem preencher todas as vagas
/// (time, role) em aberto. Evita que um pick "roube" o único suporte que sobraria
/// para outro time. Usa emparelhamento bipartido (caminhos aumentantes).
/// </summary>
public static class PoolFeasibility
{
    /// <param name="openSlots">Uma entrada por vaga: a role que falta (o time não importa para o emparelhamento).</param>
    /// <param name="pool">Jogadores ainda sem time.</param>
    public static bool CanFill(IReadOnlyList<LolRole> openSlots, IReadOnlyList<Player> pool)
        => MaxMatching(openSlots, pool) == openSlots.Count;

    public static int MaxMatching(IReadOnlyList<LolRole> openSlots, IReadOnlyList<Player> pool)
    {
        if (openSlots.Count == 0) return 0;
        if (pool.Count < openSlots.Count) return -1;

        var playable = pool.Select(p => p.PlayableRoles().ToHashSet()).ToArray();
        var slotOwner = new int[openSlots.Count];
        Array.Fill(slotOwner, -1);

        var matched = 0;
        for (var p = 0; p < pool.Count && matched < openSlots.Count; p++)
        {
            var visited = new bool[openSlots.Count];
            if (TryAssign(p, visited)) matched++;
        }
        return matched;

        bool TryAssign(int player, bool[] visited)
        {
            for (var s = 0; s < openSlots.Count; s++)
            {
                if (visited[s] || !playable[player].Contains(openSlots[s])) continue;
                visited[s] = true;
                if (slotOwner[s] == -1 || TryAssign(slotOwner[s], visited))
                {
                    slotOwner[s] = player;
                    return true;
                }
            }
            return false;
        }
    }
}
