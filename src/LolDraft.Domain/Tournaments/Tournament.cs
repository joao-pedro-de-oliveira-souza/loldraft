using LolDraft.Domain.Draft;
using LolDraft.Domain.Players;

namespace LolDraft.Domain.Tournaments;

/// <summary>
/// Agregado principal: inscrições, capitães, draft e chave.
/// Todas as regras ficam aqui para valerem igual no Discord e no painel.
/// </summary>
public class Tournament
{
    public const int MinTeams = 2;
    public const int MaxTeams = 32;

    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public string Name { get; set; } = "";
    public TournamentStatus Status { get; set; } = TournamentStatus.Registration;

    // ---- Configuração ----
    public int TeamCount { get; set; } = 4;
    public int TeamSize { get; set; } = 5;
    public bool RequireCheckIn { get; set; } = true;
    /// <summary>Se true, o capitão só pode escolher quem fecha uma role que falta.</summary>
    public bool EnforceRoles { get; set; } = true;
    public int PickTimerSeconds { get; set; } = 60;

    public BracketType BracketType { get; set; } = BracketType.SingleElimination;
    public SeriesFormat DefaultFormat { get; set; } = SeriesFormat.Bo3;
    /// <summary>Formato das semifinais (null = usa o padrão).</summary>
    public SeriesFormat? SemifinalFormat { get; set; }
    /// <summary>Formato da final (null = usa o padrão).</summary>
    public SeriesFormat? FinalFormat { get; set; }

    // ---- Discord ----
    public ulong? AnnouncementChannelId { get; set; }
    public ulong? RegistrationMessageId { get; set; }
    public ulong? DraftMessageId { get; set; }
    public ulong? LobbyVoiceChannelId { get; set; }

    // ---- Estado ----
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CurrentPickDeadline { get; set; }

    public List<TournamentEntry> Entries { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<DraftPick> Picks { get; set; } = [];
    public List<Match> Matches { get; set; } = [];

    // =====================================================================
    // Inscrição
    // =====================================================================

    public Result<TournamentEntry> Join(Player player)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result<TournamentEntry>.Fail("As inscrições deste campeonato já fecharam.");

        if (Entries.Any(e => e.PlayerId == player.Id || e.Player == player))
            return Result<TournamentEntry>.Fail("Você já está inscrito.");

        var entry = new TournamentEntry { Player = player, PlayerId = player.Id, TournamentId = Id };
        // Quem entra durante o check-in já está presente.
        if (Status == TournamentStatus.CheckIn) entry.CheckedIn = true;
        Entries.Add(entry);
        return Result<TournamentEntry>.Success(entry);
    }

    public Result Leave(int playerId)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result.Fail("Não dá para sair depois que o draft começou. Fale com um admin.");

        var entry = Entries.FirstOrDefault(e => e.PlayerId == playerId);
        if (entry == null) return Result.Fail("Você não está inscrito.");
        Entries.Remove(entry);
        return Result.Success();
    }

    public Result OpenCheckIn()
    {
        if (Status != TournamentStatus.Registration)
            return Result.Fail("O check-in só pode abrir durante as inscrições.");
        Status = TournamentStatus.CheckIn;
        return Result.Success();
    }

    public Result CheckIn(int playerId)
    {
        if (Status != TournamentStatus.CheckIn) return Result.Fail("O check-in não está aberto.");
        var entry = Entries.FirstOrDefault(e => e.PlayerId == playerId);
        if (entry == null) return Result.Fail("Você não está inscrito neste campeonato.");
        entry.CheckedIn = true;
        return Result.Success();
    }

    /// <summary>Jogadores que contam para o draft (presentes, se houver check-in).</summary>
    public IEnumerable<TournamentEntry> ActiveEntries =>
        Entries.Where(e => !RequireCheckIn || e.CheckedIn);

    public int RequiredPlayers => TeamCount * TeamSize;

    public Result Cancel()
    {
        if (Status is TournamentStatus.Finished or TournamentStatus.Cancelled)
            return Result.Fail("Esse campeonato já foi encerrado.");
        Status = TournamentStatus.Cancelled;
        CurrentPickDeadline = null;
        return Result.Success();
    }

    public bool IsCaptainOf(Team team, ulong discordUserId) =>
        Entries.Any(e => e.Id == team.CaptainEntryId && e.Player.DiscordUserId == discordUserId);

    // =====================================================================
    // Configuração
    // =====================================================================

    public Result Configure(int? teamCount = null, SeriesFormat? defaultFormat = null,
        SeriesFormat? semifinalFormat = null, SeriesFormat? finalFormat = null,
        int? pickTimerSeconds = null, bool? enforceRoles = null, bool? requireCheckIn = null)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result.Fail("A configuração só pode mudar antes do draft.");

        if (teamCount is { } tc)
        {
            if (tc is < MinTeams or > MaxTeams)
                return Result.Fail($"O número de times precisa ficar entre {MinTeams} e {MaxTeams}.");
            if (Entries.Count(e => e.IsCaptain) > tc)
                return Result.Fail("Há mais capitães definidos do que times. Remova capitães primeiro.");
            TeamCount = tc;
        }

        if (pickTimerSeconds is { } t)
        {
            if (t is < 15 or > 600) return Result.Fail("O timer do pick precisa ficar entre 15 e 600 segundos.");
            PickTimerSeconds = t;
        }

        if (defaultFormat is { } df) DefaultFormat = df;
        if (semifinalFormat is { } sf) SemifinalFormat = sf;
        if (finalFormat is { } ff) FinalFormat = ff;
        if (enforceRoles is { } er) EnforceRoles = er;
        if (requireCheckIn is { } rc) RequireCheckIn = rc;
        return Result.Success();
    }

    // =====================================================================
    // Capitães
    // =====================================================================

    public Result SetCaptain(int playerId, bool isCaptain)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result.Fail("Capitães só podem ser alterados antes do draft.");

        var entry = Entries.FirstOrDefault(e => e.PlayerId == playerId);
        if (entry == null) return Result.Fail("Esse jogador não está inscrito no campeonato.");

        if (isCaptain && !entry.IsCaptain && Entries.Count(e => e.IsCaptain) >= TeamCount)
            return Result.Fail($"Já existem {TeamCount} capitães. Aumente o número de times ou remova um capitão.");

        entry.IsCaptain = isCaptain;
        return Result.Success();
    }

    // =====================================================================
    // Draft
    // =====================================================================

    /// <summary>Picks necessários: cada time completa TeamSize - 1 (o capitão já está no time).</summary>
    public int TotalPicks => TeamCount * (TeamSize - 1);

    public IReadOnlyList<int> PickOrder => DraftOrder.Snake(TeamCount, TeamSize - 1);

    /// <summary>
    /// Fecha as inscrições e monta os times com os capitães.
    /// </summary>
    /// <param name="draftOrderShuffle">Embaralha a ordem dos capitães. Passe null para manter a ordem de inscrição.</param>
    public Result StartDraft(DateTimeOffset now, Random? draftOrderShuffle = null, IReadOnlyList<string>? teamNames = null)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result.Fail("O draft já começou ou o campeonato terminou.");

        var active = ActiveEntries.ToList();
        var captains = active.Where(e => e.IsCaptain).ToList();

        if (captains.Count != TeamCount)
            return Result.Fail($"São necessários {TeamCount} capitães presentes (há {captains.Count}).");

        var poolSize = active.Count - captains.Count;
        if (poolSize < TotalPicks)
            return Result.Fail($"Jogadores insuficientes: precisa de {RequiredPlayers} presentes, há {active.Count}.");

        if (draftOrderShuffle != null)
            captains = captains.OrderBy(_ => draftOrderShuffle.Next()).ToList();

        Teams.Clear();
        Picks.Clear();
        for (var i = 0; i < captains.Count; i++)
        {
            var captain = captains[i];
            var team = new Team
            {
                TournamentId = Id,
                DraftPosition = i + 1,
                Name = teamNames != null && i < teamNames.Count ? teamNames[i] : $"Time {captain.Player.DisplayName}",
                ColorHex = TeamColors.Palette[i % TeamColors.Palette.Length],
                CaptainEntryId = captain.Id,
            };
            team.Members.Add(captain);
            captain.Team = team;
            captain.AssignedRole = captain.Player.PrimaryRole;
            Teams.Add(team);
        }

        Status = TournamentStatus.Drafting;
        CurrentPickDeadline = now.AddSeconds(PickTimerSeconds);
        return Result.Success();
    }

    public bool IsDraftComplete => Picks.Count >= TotalPicks;

    /// <summary>Time da vez, ou null se o draft acabou / não começou.</summary>
    public Team? CurrentTeam
    {
        get
        {
            if (Status != TournamentStatus.Drafting || IsDraftComplete) return null;
            var position = PickOrder[Picks.Count];
            return Teams.First(t => t.DraftPosition == position);
        }
    }

    public int CurrentRound => Picks.Count / TeamCount + 1;

    /// <summary>Jogadores ainda sem time.</summary>
    public IEnumerable<TournamentEntry> AvailablePool =>
        ActiveEntries.Where(e => !e.IsCaptain && e.Team == null && e.TeamId == null);

    private static Dictionary<Player, LolRole> CaptainLock(Team team, IEnumerable<TournamentEntry> members)
    {
        var captain = members.First(m => m.Id == team.CaptainEntryId || m.IsCaptain);
        return new Dictionary<Player, LolRole> { [captain.Player] = captain.Player.PrimaryRole };
    }

    /// <summary>Roles que o time ainda precisa preencher.</summary>
    public IReadOnlyList<LolRole> MissingRoles(Team team)
    {
        var members = team.Members.ToList();
        return RoleAssignment.MissingRoles(members.Select(m => m.Player).ToList(), CaptainLock(team, members));
    }

    /// <summary>
    /// Se o time pode escolher esse jogador: (1) o próprio time continua conseguindo fechar as 5 roles e
    /// (2) os jogadores que sobram ainda conseguem completar todos os outros times.
    /// </summary>
    public bool CanTeamPick(Team team, TournamentEntry candidate) => CanTeamPick(team, candidate, new PickContext(this));

    private bool CanTeamPick(Team team, TournamentEntry candidate, PickContext ctx)
    {
        if (!EnforceRoles) return true;

        var members = team.Members.ToList();
        var lockMap = CaptainLock(team, members);
        var withCandidate = members.Select(m => m.Player).Append(candidate.Player).ToList();
        var solved = RoleAssignment.Solve(withCandidate, lockMap);
        if (solved == null) return false;

        // Se a situação já era impossível antes do pick, não bloqueia (evita travar o draft).
        if (!ctx.GloballyFeasible) return true;

        var slotsAfter = new List<LolRole>();
        foreach (var t in Teams)
        {
            if (t == team) slotsAfter.AddRange(LolRoleExtensions.All.Where(r => !solved.ContainsValue(r)));
            else slotsAfter.AddRange(ctx.Missing(t));
        }
        var poolAfter = ctx.Pool.Where(e => e != candidate).Select(e => e.Player).ToList();
        return PoolFeasibility.CanFill(slotsAfter, poolAfter);
    }

    /// <summary>Cache por cálculo de elegibilidade (roles faltantes de cada time e o pool atual).</summary>
    private sealed class PickContext
    {
        private readonly Tournament _t;
        private readonly Dictionary<Team, IReadOnlyList<LolRole>> _missing = new();
        public List<TournamentEntry> Pool { get; }
        public bool GloballyFeasible { get; }

        public PickContext(Tournament t)
        {
            _t = t;
            Pool = t.AvailablePool.ToList();
            var slots = t.Teams.SelectMany(Missing).ToList();
            GloballyFeasible = PoolFeasibility.CanFill(slots, Pool.Select(e => e.Player).ToList());
        }

        public IReadOnlyList<LolRole> Missing(Team team)
        {
            if (!_missing.TryGetValue(team, out var m)) _missing[team] = m = _t.MissingRoles(team);
            return m;
        }
    }

    /// <summary>Opções válidas para o time da vez, melhores primeiro.</summary>
    public IReadOnlyList<TournamentEntry> EligibleFor(Team team)
    {
        var ctx = new PickContext(this);
        var missing = ctx.Missing(team);
        return ctx.Pool
            .Where(e => CanTeamPick(team, e, ctx))
            .OrderBy(e => missing.Contains(e.Player.PrimaryRole) ? 0 : 1)
            .ThenByDescending(e => e.Player.RatingScore ?? -1)
            .ThenBy(e => e.JoinedAt)
            .ToList();
    }

    public Result<DraftPick> Pick(int teamId, int entryId, DateTimeOffset now, bool autoPicked = false)
    {
        if (Status != TournamentStatus.Drafting) return Result<DraftPick>.Fail("O draft não está em andamento.");

        var team = CurrentTeam;
        if (team == null) return Result<DraftPick>.Fail("O draft já terminou.");
        if (team.Id != teamId) return Result<DraftPick>.Fail($"Não é sua vez. Agora é a vez do **{team.Name}**.");

        var entry = AvailablePool.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) return Result<DraftPick>.Fail("Esse jogador não está disponível.");

        if (!CanTeamPick(team, entry))
            return Result<DraftPick>.Fail(
                $"{entry.Player.DisplayName} não pode ser escolhido: ou não fecha uma role que falta no seu time " +
                $"({string.Join(", ", MissingRoles(team).Select(r => r.Label()))}), ou deixaria outro time sem jogador para alguma role.");

        var pick = new DraftPick
        {
            TournamentId = Id,
            PickNumber = Picks.Count + 1,
            Round = CurrentRound,
            TeamId = team.Id,
            EntryId = entry.Id,
            AutoPicked = autoPicked,
            PickedAt = now,
        };
        Picks.Add(pick);

        entry.Team = team;
        entry.TeamId = team.Id == 0 ? null : team.Id;
        team.Members.Add(entry);
        RecalculateRoles(team);

        if (IsDraftComplete)
        {
            Status = TournamentStatus.Drafted;
            CurrentPickDeadline = null;
        }
        else
        {
            CurrentPickDeadline = now.AddSeconds(PickTimerSeconds);
        }

        return Result<DraftPick>.Success(pick);
    }

    /// <summary>Escolhe automaticamente para o time da vez (timer estourado ou capitão ausente).</summary>
    public Result<DraftPick> AutoPick(DateTimeOffset now)
    {
        var team = CurrentTeam;
        if (team == null) return Result<DraftPick>.Fail("Não há pick pendente.");

        var choice = EligibleFor(team).FirstOrDefault()
                     ?? AvailablePool.OrderByDescending(e => e.Player.RatingScore ?? -1).FirstOrDefault();
        if (choice == null) return Result<DraftPick>.Fail("Não há jogadores disponíveis.");

        // Se nenhum elegível existir (EnforceRoles impossível), escolhe mesmo assim sem travar o draft.
        var enforce = EnforceRoles;
        if (!CanTeamPick(team, choice)) EnforceRoles = false;
        try { return Pick(team.Id, choice.Id, now, autoPicked: true); }
        finally { EnforceRoles = enforce; }
    }

    /// <summary>
    /// Monta os times sem draft manual: completa os capitães com os jogadores de maior elo (se faltar)
    /// e roda o draft em cobrinha automático, que equilibra elo e respeita as roles.
    /// </summary>
    public Result AutoBalance(DateTimeOffset now, Random? shuffle = null)
    {
        var start = AutoBalanceStart(now, shuffle);
        return start.Ok ? AutoDraftAll(now) : start;
    }

    /// <summary>
    /// Passo 1 do balanceamento: completa capitães e cria os times (salve antes do passo 2
    /// para os times terem Id no banco).
    /// </summary>
    public Result AutoBalanceStart(DateTimeOffset now, Random? shuffle = null)
    {
        if (Status is not (TournamentStatus.Registration or TournamentStatus.CheckIn))
            return Result.Fail("O balanceamento só pode ser feito antes do draft.");

        var active = ActiveEntries.ToList();
        if (active.Count < RequiredPlayers)
            return Result.Fail($"Jogadores insuficientes: precisa de {RequiredPlayers} presentes, há {active.Count}.");

        var missingCaptains = TeamCount - active.Count(e => e.IsCaptain);
        foreach (var e in active.Where(e => !e.IsCaptain).OrderByDescending(e => e.Player.RatingScore ?? -1).Take(Math.Max(0, missingCaptains)))
            e.IsCaptain = true;

        // Mais fraco escolhe primeiro: o capitão de menor elo fica na posição 1.
        var start = StartDraft(now, shuffle);
        if (!start.Ok) return start;
        var order = Teams
            .OrderBy(t => Entries.First(e => e.Id == t.CaptainEntryId || (e.Team == t && e.IsCaptain)).Player.RatingScore ?? -1)
            .ToList();
        for (var i = 0; i < order.Count; i++) order[i].DraftPosition = i + 1;
        return Result.Success();
    }

    /// <summary>Passo 2: faz todos os picks restantes automaticamente.</summary>
    public Result AutoDraftAll(DateTimeOffset now)
    {
        if (Status != TournamentStatus.Drafting) return Result.Fail("O draft não está em andamento.");
        var guard = TotalPicks + 5;
        while (Status == TournamentStatus.Drafting && guard-- > 0)
        {
            var r = AutoPick(now);
            if (!r.Ok) return Result.Fail(r.Error!);
        }
        return Result.Success();
    }

    /// <summary>Elo médio do time (só quem tem elo), ou null.</summary>
    public int? TeamAverageRating(Team team)
    {
        var ratings = team.Members.Select(m => m.Player.RatingScore).Where(r => r.HasValue).Select(r => r!.Value).ToList();
        return ratings.Count == 0 ? null : (int)ratings.Average();
    }

    public bool IsPickExpired(DateTimeOffset now) =>
        Status == TournamentStatus.Drafting && CurrentPickDeadline is { } d && now >= d;

    /// <summary>
    /// Vez de um capitão de teste (🤖): o sistema escolhe por ele depois de alguns segundos,
    /// para dar tempo de acompanhar o quadro.
    /// </summary>
    public bool IsBotTurnReady(DateTimeOffset now, int delaySeconds = 3)
    {
        var team = CurrentTeam;
        if (team == null || CurrentPickDeadline is not { } deadline) return false;
        var captain = Entries.FirstOrDefault(e => e.Id == team.CaptainEntryId);
        if (captain == null || !captain.Player.IsFake) return false;
        var pickStarted = deadline.AddSeconds(-PickTimerSeconds);
        return now >= pickStarted.AddSeconds(delaySeconds);
    }

    private void RecalculateRoles(Team team)
    {
        var members = team.Members.ToList();
        var solved = RoleAssignment.Solve(members.Select(m => m.Player).ToList(), CaptainLock(team, members));
        foreach (var m in members)
            m.AssignedRole = solved != null && solved.TryGetValue(m.Player, out var r) ? r : null;
    }

    // =====================================================================
    // Chave
    // =====================================================================

    public SeriesFormat FormatForRound(int round, int totalRounds)
    {
        if (round == totalRounds && FinalFormat is { } f) return f;
        if (round == totalRounds - 1 && SemifinalFormat is { } s) return s;
        return DefaultFormat;
    }

    /// <summary>Gera a chave de eliminação simples. Seeds = ordem da lista (use embaralhado se quiser sorteio).</summary>
    public Result GenerateBracket(IReadOnlyList<Team>? seeding = null)
    {
        if (Status != TournamentStatus.Drafted)
            return Result.Fail("A chave só pode ser gerada depois que o draft terminar.");

        var seeds = seeding ?? Teams.OrderBy(t => t.DraftPosition).ToList();
        Matches.Clear();
        Matches.AddRange(Bracket.SingleElimination.Generate(seeds, FormatForRound));
        Status = TournamentStatus.Running;
        return Result.Success();
    }

    /// <summary>Registra a vitória de um jogo da série. matchNumber é o número visível ("Partida 3").</summary>
    public Result<Match> ReportGame(int matchNumber, int winnerTeamId)
    {
        if (Status != TournamentStatus.Running) return Result<Match>.Fail("O campeonato não está em andamento.");
        var match = Matches.FirstOrDefault(m => m.Number == matchNumber);
        if (match == null) return Result<Match>.Fail("Partida não encontrada.");

        var result = match.ReportGame(winnerTeamId);
        if (!result.Ok) return Result<Match>.Fail(result.Error!);

        UpdateFinishedStatus();
        return Result<Match>.Success(match);
    }

    /// <summary>Correção manual de placar (admin).</summary>
    public Result<Match> SetScore(int matchNumber, int winsA, int winsB)
    {
        if (Status != TournamentStatus.Running) return Result<Match>.Fail("O campeonato não está em andamento.");
        var match = Matches.FirstOrDefault(m => m.Number == matchNumber);
        if (match == null) return Result<Match>.Fail("Partida não encontrada.");
        var r = match.SetScore(winsA, winsB);
        if (!r.Ok) return Result<Match>.Fail(r.Error!);
        UpdateFinishedStatus();
        return Result<Match>.Success(match);
    }

    /// <summary>Troca MD1/MD3/MD5 de uma partida específica.</summary>
    public Result<Match> ChangeMatchFormat(int matchNumber, SeriesFormat format)
    {
        var match = Matches.FirstOrDefault(m => m.Number == matchNumber);
        if (match == null) return Result<Match>.Fail("Partida não encontrada.");
        var r = match.ChangeFormat(format);
        return r.Ok ? Result<Match>.Success(match) : Result<Match>.Fail(r.Error!);
    }

    public Result RenameTeam(int draftPosition, string name)
    {
        if (Status is TournamentStatus.Finished or TournamentStatus.Cancelled)
            return Result.Fail("Esse campeonato já foi encerrado.");
        name = name.Trim();
        if (name.Length is < 2 or > 40) return Result.Fail("O nome precisa ter entre 2 e 40 caracteres.");
        var team = Teams.FirstOrDefault(t => t.DraftPosition == draftPosition);
        if (team == null) return Result.Fail($"Não existe o time {draftPosition}.");
        if (Teams.Any(t => t != team && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Fail("Já existe um time com esse nome.");
        team.Name = name;
        return Result.Success();
    }

    /// <summary>Partidas prontas para jogar (os dois times definidos e sem vencedor).</summary>
    public IEnumerable<Match> ReadyMatches => Matches.Where(m => m.IsReady).OrderBy(m => m.Number);

    public bool IsCaptainOfTeamId(int teamId, ulong discordUserId) =>
        Teams.FirstOrDefault(t => t.Id == teamId) is { } team && IsCaptainOf(team, discordUserId);

    private void UpdateFinishedStatus()
    {
        if (Matches.Count > 0 && Matches.All(m => m.IsFinished)) Status = TournamentStatus.Finished;
    }

    public Team? Champion
    {
        get
        {
            if (Matches.Count == 0) return null;
            var final = Matches.OrderByDescending(m => m.Round).First();
            return final.WinnerTeamId is { } w ? Teams.FirstOrDefault(t => t.Id == w) ?? final.Winner : final.Winner;
        }
    }
}

public static class TeamColors
{
    /// <summary>Cores distintas para os cargos dos times.</summary>
    public static readonly string[] Palette =
    [
        "#E84057", "#3B82F6", "#22C55E", "#F59E0B", "#A855F7", "#14B8A6", "#EC4899", "#F97316",
        "#84CC16", "#06B6D4", "#EAB308", "#8B5CF6", "#EF4444", "#10B981", "#6366F1", "#D946EF"
    ];
}
