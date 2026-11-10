using System.Security.Claims;
using LolDraft.Application.Players;
using LolDraft.Application.Tournaments;
using LolDraft.Domain;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using LolDraft.Host.Bot;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace LolDraft.Host.Web;

public record ProfileRequest(string PrimaryRole, string? SecondaryRole, bool Fill);
public record PickRequest(int EntryId);
public record ReportRequest(int WinnerTeamId);
public record CaptainRequest(string DiscordId, bool IsCaptain);
public record ShuffleRequest(bool Shuffle = true);

/// <summary>API REST do painel. Toda escrita passa pelos mesmos serviços usados pelo bot.</summary>
public static class Api
{
    private static IResult Fail(string error) => Results.BadRequest(new { error });
    private static IResult From(Result r) => r.Ok ? Results.NoContent() : Fail(r.Error!);
    private static IResult From<T>(Result<T> r) => r.Ok ? Results.NoContent() : Fail(r.Error!);

    private static ulong GuildId(IOptions<DiscordOptions> o) => o.Value.GuildId;

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ---------------- Conta ----------------

        api.MapGet("/me", async (ClaimsPrincipal user, IConfiguration cfg, PlayerService players, DtoMapper map, AdminChecker admins) =>
        {
            var id = user.DiscordId();
            if (id == null) return Results.Ok(new MeDto(Auth.IsConfigured(cfg), false, null, null, null, false, null));
            var player = await players.FindByDiscordIdAsync(id.Value);
            return Results.Ok(new MeDto(true, true, id.ToString(),
                admins.GuildDisplayName(id.Value) ?? user.FindFirstValue(ClaimTypes.Name),
                user.AvatarUrl(), admins.IsAdmin(id), player == null ? null : map.Player(player)));
        });

        api.MapPut("/me/profile", async (ProfileRequest req, ClaimsPrincipal user, PlayerService players, AdminChecker admins, DtoMapper map) =>
        {
            if (user.DiscordId() is not { } id) return Results.Unauthorized();
            if (!Enum.TryParse<LolRole>(req.PrimaryRole, true, out var primary)) return Fail("Role principal inválida.");
            LolRole? secondary = null;
            if (!string.IsNullOrEmpty(req.SecondaryRole))
            {
                if (!Enum.TryParse<LolRole>(req.SecondaryRole, true, out var s)) return Fail("Role secundária inválida.");
                secondary = s;
            }
            var name = admins.GuildDisplayName(id) ?? user.FindFirstValue(ClaimTypes.Name) ?? "Jogador";
            var (player, _) = await players.UpsertAsync(id, name, primary, secondary, clearSecondary: secondary == null, fill: req.Fill);
            return Results.Ok(map.Player(player));
        }).RequireAuthorization();

        // ---------------- Leitura ----------------

        api.MapGet("/players", async (PlayerService players, DtoMapper map) =>
            (await players.ListAsync()).Select(map.Player));

        api.MapGet("/tournaments", async (TournamentService ts, DtoMapper map, IOptions<DiscordOptions> o) =>
            (await ts.ListAsync(GuildId(o))).Select(t => new TournamentSummaryDto(t.Id, t.Name, t.Status.ToString(), 0, t.TeamCount, t.CreatedAt)));

        api.MapGet("/tournaments/current", async (TournamentService ts, DtoMapper map, IOptions<DiscordOptions> o) =>
        {
            var t = await ts.GetActiveAsync(GuildId(o)) ?? await ts.GetLatestAsync(GuildId(o));
            return t == null ? Results.NoContent() : Results.Ok(map.Tournament(t));
        });

        api.MapGet("/tournaments/{id:int}", async (int id, TournamentService ts, DtoMapper map) =>
            await ts.GetAsync(id) is { } t ? Results.Ok(map.Tournament(t)) : Results.NotFound());

        // ---------------- Jogador ----------------

        var t = api.MapGroup("/tournaments/{id:int}").RequireAuthorization();

        t.MapPost("/join", async (int id, ClaimsPrincipal u, TournamentService ts) => From(await ts.JoinAsync(id, u.DiscordId()!.Value)));
        t.MapPost("/leave", async (int id, ClaimsPrincipal u, TournamentService ts) => From(await ts.LeaveAsync(id, u.DiscordId()!.Value)));
        t.MapPost("/checkin", async (int id, ClaimsPrincipal u, TournamentService ts) => From(await ts.CheckInAsync(id, u.DiscordId()!.Value)));

        t.MapPost("/pick", async (int id, PickRequest req, ClaimsPrincipal u, TournamentService ts, AdminChecker admins) =>
        {
            var tour = await ts.GetAsync(id);
            var team = tour?.CurrentTeam;
            if (tour == null || team == null) return Fail("O draft não está em andamento.");
            if (!tour.IsCaptainOf(team, u.DiscordId()!.Value) && !admins.IsAdmin(u.DiscordId()))
                return Results.Json(new { error = $"Só o capitão do {team.Name} (ou um admin) pode escolher agora." }, statusCode: 403);
            return From(await ts.PickAsync(id, team.Id, req.EntryId));
        });

        t.MapPost("/autopick", async (int id, ClaimsPrincipal u, TournamentService ts, AdminChecker admins) =>
        {
            var tour = await ts.GetAsync(id);
            var team = tour?.CurrentTeam;
            if (tour == null || team == null) return Fail("O draft não está em andamento.");
            if (!tour.IsCaptainOf(team, u.DiscordId()!.Value) && !admins.IsAdmin(u.DiscordId()))
                return Results.Json(new { error = "Só o capitão da vez (ou um admin)." }, statusCode: 403);
            return From(await ts.PickAsync(id, 0, 0, auto: true));
        });

        t.MapPost("/matches/{number:int}/report", async (int id, int number, ReportRequest req, ClaimsPrincipal u,
            TournamentService ts, AdminChecker admins) =>
        {
            var tour = await ts.GetAsync(id);
            var m = tour?.Matches.FirstOrDefault(x => x.Number == number);
            if (tour == null || m == null) return Results.NotFound();
            var me = u.DiscordId()!.Value;
            var isCaptain = (m.TeamAId is { } a && tour.IsCaptainOfTeamId(a, me)) || (m.TeamBId is { } b && tour.IsCaptainOfTeamId(b, me));
            if (!isCaptain && !admins.IsAdmin(me))
                return Results.Json(new { error = "Só os capitães dessa partida (ou um admin)." }, statusCode: 403);
            return From(await ts.ReportGameAsync(id, number, req.WinnerTeamId));
        });

        // ---------------- Admin ----------------

        var admin = api.MapGroup("/tournaments/{id:int}/admin").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var admins = ctx.HttpContext.RequestServices.GetRequiredService<AdminChecker>();
                return admins.IsAdmin(ctx.HttpContext.User.DiscordId())
                    ? await next(ctx)
                    : Results.Json(new { error = "Só admins (Gerenciar servidor)." }, statusCode: 403);
            });

        admin.MapPost("/captain", async (int id, CaptainRequest req, TournamentService ts) =>
            ulong.TryParse(req.DiscordId, out var did) ? From(await ts.SetCaptainAsync(id, did, req.IsCaptain)) : Fail("Id inválido."));
        admin.MapPost("/checkin-open", async (int id, TournamentService ts) => From(await ts.OpenCheckInAsync(id)));
        admin.MapPost("/start-draft", async (int id, ShuffleRequest req, TournamentService ts) => From(await ts.StartDraftAsync(id, req.Shuffle)));
        admin.MapPost("/balance", async (int id, TournamentService ts) => From(await ts.BalanceAsync(id)));
        admin.MapPost("/bracket", async (int id, ShuffleRequest req, TournamentService ts) => From(await ts.GenerateBracketAsync(id, req.Shuffle)));
    }

    /// <summary>
    /// Login de desenvolvimento (sem Discord) para testar o painel localmente.
    /// Só existe em Development com Web:DevLogin=true.
    /// </summary>
    public static void MapDevLogin(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("Web:DevLogin")) return;

        app.MapGet("/auth/dev-login", async (HttpContext ctx, string discordId, string? name) =>
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, discordId),
                new Claim(ClaimTypes.Name, name ?? "Dev"),
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        });
    }
}
