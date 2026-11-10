using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Discord.WebSocket;
using LolDraft.Host.Bot;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace LolDraft.Host.Web;

/// <summary>Login com Discord (OAuth2) usando os handlers nativos do ASP.NET Core.</summary>
public static class Auth
{
    public const string Scheme = "Discord";
    public const string AvatarClaim = "urn:discord:avatar";

    public static bool IsConfigured(IConfiguration cfg) =>
        !string.IsNullOrWhiteSpace(cfg["Discord:ClientId"]) && !string.IsNullOrWhiteSpace(cfg["Discord:ClientSecret"]);

    public static void AddDiscordAuth(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var auth = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = IsConfigured(cfg) ? Scheme : CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(o =>
        {
            o.Cookie.Name = "loldraft.auth";
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // API responde 401 em vez de redirecionar.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });

        if (!IsConfigured(cfg)) return;

        auth.AddOAuth(Scheme, o =>
        {
            o.ClientId = cfg["Discord:ClientId"]!;
            o.ClientSecret = cfg["Discord:ClientSecret"]!;
            o.CallbackPath = "/signin-discord";
            o.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
            o.TokenEndpoint = "https://discord.com/api/oauth2/token";
            o.UserInformationEndpoint = "https://discord.com/api/users/@me";
            o.Scope.Add("identify");
            o.SaveTokens = false;

            // Em http://localhost o cookie de correlação precisa ser Lax (None exige https).
            o.CorrelationCookie.SameSite = SameSiteMode.Lax;
            o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            o.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
            o.ClaimActions.MapCustomJson(ClaimTypes.Name, u =>
                u.TryGetProperty("global_name", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : u.GetProperty("username").GetString());
            o.ClaimActions.MapJsonKey(AvatarClaim, "avatar");

            o.Events.OnCreatingTicket = async ctx =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                using var res = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
                res.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                ctx.RunClaimActions(json.RootElement);
            };
        });
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/login", (string? returnUrl, IConfiguration cfg) =>
        {
            if (!IsConfigured(cfg))
                return Results.Problem("Login não configurado: defina Discord:ClientSecret no appsettings.Local.json.", statusCode: 503);
            var safe = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = safe }, [Scheme]);
        });

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    public static ulong? DiscordId(this ClaimsPrincipal user) =>
        ulong.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? AvatarUrl(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var hash = user.FindFirstValue(AvatarClaim);
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(hash) ? null : $"https://cdn.discordapp.com/avatars/{id}/{hash}.png?size=128";
    }
}

/// <summary>Admin no painel = quem tem "Gerenciar servidor" no servidor do bot.</summary>
public class AdminChecker(DiscordSocketClient client, IOptions<DiscordOptions> options, IConfiguration cfg, IHostEnvironment env)
{
    public bool IsAdmin(ulong? discordId)
    {
        if (discordId is not { } id) return false;
        // Login de desenvolvimento: permite simular um admin sem estar no servidor.
        if (env.IsDevelopment() && cfg.GetValue<bool>("Web:DevLogin") && cfg["Web:DevAdminId"] == id.ToString()) return true;
        var user = client.GetGuild(options.Value.GuildId)?.GetUser(id);
        return user?.GuildPermissions.ManageGuild ?? false;
    }

    public string? GuildDisplayName(ulong discordId) =>
        client.GetGuild(options.Value.GuildId)?.GetUser(discordId)?.DisplayName;
}
