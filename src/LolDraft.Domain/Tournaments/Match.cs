namespace LolDraft.Domain.Tournaments;

public enum MatchSlot { A = 0, B = 1 }

/// <summary>Uma série (MD1/MD3/MD5) entre dois times dentro da chave.</summary>
public class Match
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    /// <summary>Número visível ("Partida 3"), sequencial na chave.</summary>
    public int Number { get; set; }

    /// <summary>1 = primeira rodada.</summary>
    public int Round { get; set; }
    public int TotalRounds { get; set; }

    /// <summary>Posição dentro da rodada (0-based, de cima para baixo).</summary>
    public int Position { get; set; }

    public SeriesFormat Format { get; set; } = SeriesFormat.Bo3;

    public int? TeamAId { get; set; }
    public Team? TeamA { get; set; }
    public int? TeamBId { get; set; }
    public Team? TeamB { get; set; }

    public int WinsA { get; set; }
    public int WinsB { get; set; }

    public int? WinnerTeamId { get; set; }
    public Team? Winner { get; set; }

    /// <summary>Avançou direto por falta de adversário.</summary>
    public bool IsBye { get; set; }

    public int? NextMatchId { get; set; }
    public Match? NextMatch { get; set; }
    public MatchSlot NextSlot { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public bool IsFinished => Winner != null || WinnerTeamId != null;
    public bool IsReady => TeamA != null && TeamB != null && !IsFinished;

    public string RoundName => RoundNames.For(Round, TotalRounds);

    public string ScoreLine =>
        $"{TeamA?.Name ?? "A definir"} {WinsA} x {WinsB} {TeamB?.Name ?? "A definir"}";

    public Result ReportGame(int winnerTeamId)
    {
        if (IsFinished) return Result.Fail("Essa série já terminou.");
        if (TeamA == null || TeamB == null) return Result.Fail("Essa partida ainda não tem os dois times.");

        if (TeamA.Id == winnerTeamId) WinsA++;
        else if (TeamB.Id == winnerTeamId) WinsB++;
        else return Result.Fail("Esse time não está nesta partida.");

        var needed = Format.WinsNeeded();
        if (WinsA >= needed) Finish(TeamA);
        else if (WinsB >= needed) Finish(TeamB);
        return Result.Success();
    }

    /// <summary>Troca o formato da série (ex.: MD3 → MD5). Só vale se o placar ainda permitir.</summary>
    public Result ChangeFormat(SeriesFormat format)
    {
        if (IsFinished) return Result.Fail("Essa série já terminou.");
        if (Math.Max(WinsA, WinsB) >= format.WinsNeeded())
            return Result.Fail($"O placar atual já fecharia uma {format.Label()}.");
        Format = format;
        return Result.Success();
    }

    /// <summary>Correção manual do placar por um admin (só antes do fim da série).</summary>
    public Result SetScore(int winsA, int winsB)
    {
        if (IsFinished) return Result.Fail("Essa série já terminou.");
        var needed = Format.WinsNeeded();
        if (winsA < 0 || winsB < 0 || winsA > needed || winsB > needed || (winsA == needed && winsB == needed))
            return Result.Fail("Placar inválido para o formato.");

        WinsA = winsA;
        WinsB = winsB;
        if (WinsA == needed) Finish(TeamA!);
        else if (WinsB == needed) Finish(TeamB!);
        return Result.Success();
    }

    internal void Finish(Team winner, bool bye = false)
    {
        Winner = winner;
        WinnerTeamId = winner.Id == 0 ? null : winner.Id;
        IsBye = bye;
        FinishedAt = DateTimeOffset.UtcNow;

        if (NextMatch == null) return;
        if (NextSlot == MatchSlot.A)
        {
            NextMatch.TeamA = winner;
            NextMatch.TeamAId = winner.Id == 0 ? null : winner.Id;
        }
        else
        {
            NextMatch.TeamB = winner;
            NextMatch.TeamBId = winner.Id == 0 ? null : winner.Id;
        }
    }
}

public static class RoundNames
{
    public static string For(int round, int totalRounds) => (totalRounds - round) switch
    {
        0 => "Final",
        1 => "Semifinal",
        2 => "Quartas de final",
        3 => "Oitavas de final",
        _ => $"Rodada {round}"
    };
}
