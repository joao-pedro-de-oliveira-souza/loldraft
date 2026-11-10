namespace LolDraft.Domain.Players;

/// <summary>
/// Converte tier/divisão/PL em um número único e comparável.
/// Cada divisão vale 100 pontos, então Ouro I 50 PL &gt; Ouro II 99 PL.
/// </summary>
public static class EloScore
{
    private static readonly string[] Tiers =
    [
        "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND",
        "MASTER", "GRANDMASTER", "CHALLENGER"
    ];

    public static int? From(string? tier, string? division, int? leaguePoints)
    {
        if (string.IsNullOrWhiteSpace(tier)) return null;

        var tierIndex = Array.IndexOf(Tiers, tier.Trim().ToUpperInvariant());
        if (tierIndex < 0) return null;

        var lp = Math.Max(0, leaguePoints ?? 0);

        // Master+ não tem divisão: tudo acima de Diamante I, somando PL direto.
        if (tierIndex >= 7) return 7 * 400 + lp;

        var divisionValue = division?.Trim().ToUpperInvariant() switch
        {
            "IV" => 0,
            "III" => 1,
            "II" => 2,
            "I" => 3,
            _ => 0
        };

        return tierIndex * 400 + divisionValue * 100 + Math.Min(lp, 99);
    }

    private static readonly string[] Names =
    [
        "Ferro", "Bronze", "Prata", "Ouro", "Platina", "Esmeralda", "Diamante", "Mestre", "Grão-Mestre", "Desafiante"
    ];

    private static readonly string[] Divisions = ["IV", "III", "II", "I"];

    /// <summary>Número → rótulo aproximado ("Ouro II"), usado para médias de time.</summary>
    public static string Label(int score)
    {
        if (score >= 7 * 400) return Names[7];
        var tier = Math.Clamp(score / 400, 0, 6);
        var division = Math.Clamp(score % 400 / 100, 0, 3);
        return $"{Names[tier]} {Divisions[division]}";
    }
}
