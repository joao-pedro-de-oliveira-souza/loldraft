using Discord.Commands;
using LolDraft.Domain.Players;
using LolDraft.Domain.Tournaments;
using LolDraft.Host.Bot.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LolDraft.Tests;

public class TextCommandTests
{
    [Theory]
    [InlineData("top", LolRole.Top)]
    [InlineData("JG", LolRole.Jungle)]
    [InlineData("jungle", LolRole.Jungle)]
    [InlineData("meio", LolRole.Mid)]
    [InlineData("bot", LolRole.Adc)]
    [InlineData("suporte", LolRole.Support)]
    [InlineData("supp", LolRole.Support)]
    public void Roles_accept_common_aliases(string input, LolRole expected) => Assert.Equal(expected, Parse.Role(input));

    [Fact]
    public void Invalid_values_are_rejected()
    {
        Assert.Null(Parse.Role("banana"));
        Assert.Null(Parse.Format("md7"));
        Assert.Null(Parse.YesNo("talvez"));
        Assert.Equal(SeriesFormat.Bo5, Parse.Format("MD5"));
        Assert.Equal(SeriesFormat.Bo3, Parse.Format("bo3"));
        Assert.True(Parse.YesNo("sim"));
        Assert.False(Parse.YesNo("não"));
    }

    /// <summary>Mesmos serviços que o bot usa (banco em memória), só para montar os módulos.</summary>
    private static IServiceProvider Services()
    {
        var sc = new ServiceCollection();
        sc.AddDbContext<LolDraft.Application.Data.AppDbContext>(o =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(o, "Data Source=:memory:"));
        sc.AddScoped<LolDraft.Application.Players.PlayerService>();
        sc.AddScoped<LolDraft.Application.Tournaments.TournamentService>();
        sc.AddScoped<LolDraft.Application.Dev.DevService>();
        sc.AddSingleton<LolDraft.Application.Tournaments.TournamentLocks>();
        sc.AddSingleton<LolDraft.Application.Tournaments.ITournamentEvents, LolDraft.Application.Tournaments.NoopTournamentEvents>();
        sc.AddSingleton<Discord.WebSocket.DiscordSocketClient>();
        sc.AddLogging();
        sc.AddSingleton<LolDraft.Host.Bot.DiscordResources>();
        sc.AddSingleton<LolDraft.Application.Riot.IRiotApi>(new FakeRiot { Enabled = false });
        sc.AddSingleton<LolDraft.Application.Riot.RiotLinkService>();
        // Igual ao ASP.NET em Development: bloqueia resolver serviço com escopo no provedor raiz.
        return sc.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<CommandService> LoadCommands()
    {
        var commands = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });
        await TextCommandSetup.LoadAsync(commands, new CombinedProvider(Services(), commands), devEnabled: true);
        return commands;
    }

    /// <summary>Provedor raiz do teste + o CommandService (que o !ajuda recebe por injeção).</summary>
    private sealed class CombinedProvider(IServiceProvider inner, CommandService commands) : IServiceProvider, IServiceScopeFactory
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(CommandService) ? commands
            : serviceType == typeof(IServiceScopeFactory) ? this
            : inner.GetService(serviceType);

        public IServiceScope CreateScope() => new Scope(inner.CreateScope(), commands);

        private sealed class Scope(IServiceScope scope, CommandService commands) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type t) => t == typeof(CommandService) ? commands : scope.ServiceProvider.GetService(t);
            public void Dispose() => scope.Dispose();
        }
    }

    [Fact]
    public async Task Loading_from_root_provider_without_scope_would_fail()
    {
        // Garante que o teste reproduz o erro real do bot: módulos com serviço de escopo no provedor raiz.
        var commands = new CommandService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            commands.AddModulesAsync(typeof(LolDraft.Host.Bot.Ui).Assembly, new CombinedProvider(Services(), commands)));
    }

    [Fact]
    public async Task Every_command_has_help_and_help_fits_discord_limits()
    {
        var commands = await LoadCommands();
        Assert.True(commands.Commands.Count() >= 35);
        Assert.All(commands.Commands, c => Assert.NotNull(HelpText.Meta(c)));
        Assert.All(commands.Commands, c => Assert.False(string.IsNullOrWhiteSpace(c.Summary)));

        var embeds = HelpText.Build(commands, null);
        Assert.NotEmpty(embeds);
        Assert.All(embeds, e =>
        {
            Assert.True(e.Length <= 6000);
            Assert.True(e.Fields.Length <= 25);
            Assert.All(e.Fields, f => Assert.True(f.Value.Length <= 1024));
        });
    }

    [Theory]
    [InlineData("camp criar 4 md3 Copa da Firma", "camp criar", true)]
    [InlineData("camp criar 4 md7 Copa", "camp criar", false)]
    [InlineData("registrar mid top fill", "registrar", true)]
    [InlineData("registrar banana", "registrar", false)]
    [InlineData("vincular Shirayuki Himeji#Hime", "vincular", true)]
    [InlineData("camp placar 1 2", "camp placar", false)]
    [InlineData("CAMP ENCERRAR", "camp encerrar", true)]
    public async Task Commands_route_and_validate_arguments(string input, string expectedCommand, bool shouldParse)
    {
        var commands = await LoadCommands();
        var search = commands.Search(input);
        Assert.True(search.IsSuccess);
        var match = search.Commands.First();
        Assert.Equal(expectedCommand, match.Command.Aliases[0]);
        using var scope = Services().CreateScope();
        var parse = await match.ParseAsync(null!, search, null, scope.ServiceProvider);
        Assert.Equal(shouldParse, parse.IsSuccess);
    }

    [Fact]
    public async Task Unknown_command_is_not_found()
    {
        var commands = await LoadCommands();
        Assert.False(commands.Search("camp xyz").IsSuccess);
        Assert.False(commands.Search("banana").IsSuccess);
    }
}
