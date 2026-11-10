using LolDraft.Application.Tournaments;

namespace LolDraft.Host.Bot;

/// <summary>A cada poucos segundos, faz auto-pick nos drafts cujo tempo do capitão acabou.</summary>
public class DraftTimerService(IServiceScopeFactory scopes, ILogger<DraftTimerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var tournaments = scope.ServiceProvider.GetRequiredService<TournamentService>();
                foreach (var id in await tournaments.ListDraftingIdsAsync(stoppingToken))
                {
                    if (await tournaments.AutoPickIfExpiredAsync(id, stoppingToken))
                        logger.LogInformation("Auto-pick por tempo no campeonato {Id}", id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no timer do draft");
            }
        }
    }
}
