namespace LolDraft.Domain.Tournaments;

public enum TournamentStatus
{
    /// <summary>Inscrições abertas.</summary>
    Registration = 0,
    /// <summary>Inscrições fechadas, confirmando presença.</summary>
    CheckIn = 1,
    /// <summary>Draft em andamento.</summary>
    Drafting = 2,
    /// <summary>Times fechados, aguardando chaveamento/calls.</summary>
    Drafted = 3,
    /// <summary>Chave gerada, partidas rolando.</summary>
    Running = 4,
    Finished = 5,
    Cancelled = 6
}

/// <summary>Formato de série. O valor numérico é o número máximo de jogos.</summary>
public enum SeriesFormat
{
    Bo1 = 1,
    Bo3 = 3,
    Bo5 = 5
}

public static class SeriesFormatExtensions
{
    /// <summary>Vitórias necessárias para fechar a série (MD3 = 2, MD5 = 3).</summary>
    public static int WinsNeeded(this SeriesFormat format) => (int)format / 2 + 1;

    public static string Label(this SeriesFormat format) => $"MD{(int)format}";
}

public enum BracketType
{
    SingleElimination = 0
    // DoubleElimination e RoundRobin entram numa fase seguinte.
}
