using Microsoft.Extensions.Hosting;
namespace DungeonForge.Application.Features.Social.Supervision;

// Drains the queue one request at a time. Failures never escape: the rule-based outcome already stands.
public sealed class NarratorJudgeWorker(NarratorJudgeQueue queue, NarratorJudge judge, ILogger<NarratorJudgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await judge.JudgeAsync(request.Party, request.Event, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Narrator judge failed for {EventId}: {Error}", request.Event.Id, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
