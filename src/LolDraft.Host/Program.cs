using System.Text.Json.Serialization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LolDraft.Application.Data;
using LolDraft.Application.Players;
using LolDraft.Application.Tournaments;
using LolDraft.Host.Bot;
using LolDraft.Host.Web;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Segredos locais (token do bot, client secret do OAuth). Arquivo fora do git, opcional.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// ---------- Banco ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// ---------- Aplicação ----------
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<LolDraft.Application.Dev.DevService>();
builder.Services.AddSingleton<TournamentLocks>();

// ---------- Riot API ----------
builder.Services.Configure<LolDraft.Application.Riot.RiotOptions>(builder.Configuration.GetSection("Riot"));
builder.Services.AddHttpClient<LolDraft.Application.Riot.IRiotApi, LolDraft.Application.Riot.RiotApiClient>(c =>
    c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<LolDraft.Application.Riot.RiotLinkService>();

// Eventos vão para o Discord (mensagens) e para o painel (SignalR).
builder.Services.AddSingleton<DraftNotifier>();
builder.Services.AddSingleton<DiscordTournamentEvents>();
builder.Services.AddSingleton<SignalRTournamentEvents>();
builder.Services.AddSingleton<ITournamentEvents>(sp => new CompositeTournamentEvents(
[
    sp.GetRequiredService<DiscordTournamentEvents>(),
    sp.GetRequiredService<SignalRTournamentEvents>(),
]));

// ---------- Discord ----------
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddSingleton(new DiscordSocketConfig
{
    // GuildMembers e MessageContent são "privileged": ative "Server Members Intent" e
    // "Message Content Intent" no Developer Portal (aba Bot).
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildVoiceStates
                     | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
    AlwaysDownloadUsers = true,
    LogLevel = LogSeverity.Info,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig
{
    CaseSensitiveCommands = false,
    DefaultRunMode = Discord.Commands.RunMode.Sync, // o bot já roda cada comando em segundo plano, com escopo próprio
    LogLevel = LogSeverity.Info,
}));
builder.Services.AddSingleton(sp => new InteractionService(
    sp.GetRequiredService<DiscordSocketClient>(),
    new InteractionServiceConfig { UseCompiledLambda = true, DefaultRunMode = RunMode.Async }));
builder.Services.AddSingleton<DiscordResources>();
builder.Services.AddHostedService<DiscordBotService>();
builder.Services.AddHostedService<DraftTimerService>();

// ---------- Web (painel) ----------
builder.AddDiscordAuth();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<DtoMapper>();
builder.Services.AddSingleton<AdminChecker>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Cria/atualiza o banco ao subir.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.GetMigrations().Any()) db.Database.Migrate();
    else db.Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapDevLogin();
app.MapApi();
app.MapHub<TournamentHub>("/hubs/tournaments");

// Rotas do Angular (/draft, /chave...) caem no index.html.
app.MapFallbackToFile("index.html");

app.Run();

// Para testes de integração (WebApplicationFactory).
public partial class Program;
