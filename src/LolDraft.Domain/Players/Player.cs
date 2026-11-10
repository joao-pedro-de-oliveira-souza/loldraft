namespace LolDraft.Domain.Players;

/// <summary>
/// Jogador cadastrado no servidor. A conta da Riot é opcional.
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>Snowflake do usuário no Discord.</summary>
    public ulong DiscordUserId { get; set; }

    public string DisplayName { get; set; } = "";

    /// <summary>Ids abaixo disso são jogadores de teste (snowflakes reais são muito maiores).</summary>
    public const ulong FakeIdLimit = 1_000_000;

    /// <summary>Jogador falso criado pelo !dev popular.</summary>
    public bool IsFake => DiscordUserId < FakeIdLimit;

    public LolRole PrimaryRole { get; set; }

    /// <summary>Role secundária (opcional).</summary>
    public LolRole? SecondaryRole { get; set; }

    /// <summary>Aceita jogar em qualquer posição se precisar.</summary>
    public bool IsFill { get; set; }

    public RiotAccount? Riot { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Roles que o jogador aceita jogar, em ordem de preferência.</summary>
    public IReadOnlyList<LolRole> PlayableRoles()
    {
        var roles = new List<LolRole> { PrimaryRole };
        if (SecondaryRole is { } s && s != PrimaryRole) roles.Add(s);
        if (IsFill) roles.AddRange(LolRoleExtensions.All.Where(r => !roles.Contains(r)));
        return roles;
    }

    public bool CanPlay(LolRole role) => PlayableRoles().Contains(role);

    /// <summary>
    /// Pontuação numérica do elo, usada para auto-pick e balanceamento.
    /// Sem conta vinculada, retorna null.
    /// </summary>
    public int? RatingScore => Riot?.RatingScore;
}

/// <summary>Conta da Riot vinculada (owned entity).</summary>
public class RiotAccount
{
    public string GameName { get; set; } = "";
    public string TagLine { get; set; } = "";
    public string? Puuid { get; set; }

    /// <summary>True depois que o jogador provou que é dono da conta (troca de ícone).</summary>
    public bool Verified { get; set; }

    /// <summary>Ex.: GOLD, PLATINUM, EMERALD.</summary>
    public string? Tier { get; set; }

    /// <summary>Ex.: I, II, III, IV.</summary>
    public string? Division { get; set; }

    public int? LeaguePoints { get; set; }
    public int? ProfileIconId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public string RiotId => $"{GameName}#{TagLine}";

    public int? RatingScore => EloScore.From(Tier, Division, LeaguePoints);
}
