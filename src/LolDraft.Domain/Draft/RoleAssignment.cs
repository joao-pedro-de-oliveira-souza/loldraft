using LolDraft.Domain.Players;

namespace LolDraft.Domain.Draft;

/// <summary>
/// Distribui as 5 roles entre os jogadores de um time, uma role por jogador,
/// respeitando o que cada um aceita jogar e preferindo a role principal.
/// Com no máximo 5 jogadores, força bruta com poda é instantânea.
/// </summary>
public static class RoleAssignment
{
    /// <summary>
    /// Retorna a melhor distribuição (menor custo de preferência) ou null se for impossível.
    /// Custo: 0 = role principal, 1 = secundária, 2+ = fill.
    /// </summary>
    /// <param name="players">Jogadores do time.</param>
    /// <param name="locked">Roles já fixadas (ex.: capitão sempre na role principal). Opcional.</param>
    public static Dictionary<Player, LolRole>? Solve(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Player, LolRole>? locked = null)
    {
        if (players.Count > LolRoleExtensions.All.Length) return null;

        var best = (Dictionary<Player, LolRole>?)null;
        var bestCost = int.MaxValue;
        var current = new Dictionary<Player, LolRole>();
        var used = new HashSet<LolRole>();

        void Search(int index, int cost)
        {
            if (cost >= bestCost) return;
            if (index == players.Count)
            {
                bestCost = cost;
                best = new Dictionary<Player, LolRole>(current);
                return;
            }

            var player = players[index];
            IEnumerable<LolRole> candidates = locked != null && locked.TryGetValue(player, out var fixedRole)
                ? [fixedRole]
                : player.PlayableRoles();

            var playable = player.PlayableRoles();
            foreach (var role in candidates)
            {
                if (used.Contains(role)) continue;
                var roleCost = playable.ToList().IndexOf(role);
                if (roleCost < 0) roleCost = 10; // role travada fora da lista: permitido, mas caro

                used.Add(role);
                current[player] = role;
                Search(index + 1, cost + roleCost);
                current.Remove(player);
                used.Remove(role);
            }
        }

        Search(0, 0);
        return best;
    }

    public static bool IsFeasible(IReadOnlyList<Player> players, IReadOnlyDictionary<Player, LolRole>? locked = null)
        => Solve(players, locked) != null;

    /// <summary>Roles que ainda ficam livres depois da melhor distribuição.</summary>
    public static IReadOnlyList<LolRole> MissingRoles(IReadOnlyList<Player> players, IReadOnlyDictionary<Player, LolRole>? locked = null)
    {
        var solved = Solve(players, locked);
        if (solved == null) return LolRoleExtensions.All;
        return LolRoleExtensions.All.Where(r => !solved.ContainsValue(r)).ToList();
    }
}
