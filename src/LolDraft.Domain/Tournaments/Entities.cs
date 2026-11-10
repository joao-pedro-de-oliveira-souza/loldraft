using LolDraft.Domain.Players;

namespace LolDraft.Domain.Tournaments;

/// <summary>Inscrição de um jogador num campeonato.</summary>
public class TournamentEntry
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public bool CheckedIn { get; set; }
    public bool IsCaptain { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Role que o jogador vai fazer no time (calculada após cada pick).</summary>
    public LolRole? AssignedRole { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Team
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Posição na ordem do draft (1 = escolhe primeiro).</summary>
    public int DraftPosition { get; set; }

    public int CaptainEntryId { get; set; }

    /// <summary>Cor do cargo no Discord, ex.: "#E84057".</summary>
    public string ColorHex { get; set; } = "#5865F2";

    // Recursos criados no Discord depois do draft (para limpar no fim).
    public ulong? DiscordRoleId { get; set; }
    public ulong? DiscordCategoryId { get; set; }
    public ulong? DiscordTextChannelId { get; set; }
    public ulong? DiscordVoiceChannelId { get; set; }

    public List<TournamentEntry> Members { get; set; } = [];
}

public class DraftPick
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    /// <summary>1-based, sequencial no draft todo.</summary>
    public int PickNumber { get; set; }
    public int Round { get; set; }

    public int TeamId { get; set; }
    public int EntryId { get; set; }

    /// <summary>True quando o timer estourou e o sistema escolheu.</summary>
    public bool AutoPicked { get; set; }

    public DateTimeOffset PickedAt { get; set; }
}
