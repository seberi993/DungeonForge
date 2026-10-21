using DungeonForge.Application.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Tests.Errors;

public class BestEffortTests
{
    private static readonly Guid PartyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (ILogger Logger, LogCapture Logs) NewLogger()
    {
        var logs = new LogCapture();
        return (logs, logs);
    }

    [Fact]
    public async Task FailingSend_DoesNotThrow_AndLogsOneCodedWarningInsideTheScope()
    {
        var (logger, logs) = NewLogger();

        await BestEffort.RunAsync(logger, "Command:Test", PartyId, "TurnResolved", () => throw new InvalidOperationException("hub is down"));

        var entry = logs.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(LogEvents.BroadcastFailedId);
        entry.EventId.Name.Should().Be("BroadcastFailed");
        entry.Message.Should().Contain("TurnResolved");
        entry.Exception.Should().BeOfType<InvalidOperationException>();
        entry.Scope.Should().Contain("Operation", "Command:Test").And.Contain("PartyId", PartyId);
    }

    [Fact]
    public async Task AsyncFailure_IsCaughtToo()
    {
        var (logger, logs) = NewLogger();

        await BestEffort.RunAsync(logger, "Command:Test", PartyId, "PartyUpdated", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("late failure");
        });

        logs.Entries.Should().ContainSingle(e => e.EventId.Id == LogEvents.BroadcastFailedId);
    }

    [Fact]
    public async Task SuccessfulSend_LogsNothing()
    {
        var (logger, logs) = NewLogger();
        var sent = false;

        await BestEffort.RunAsync(logger, "Command:Test", PartyId, "PartyUpdated", () =>
        {
            sent = true;
            return Task.CompletedTask;
        });

        sent.Should().BeTrue();
        logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Scope_CarriesOperationPartyIdAndTurn()
    {
        var (logger, logs) = NewLogger();

        using (LogScope.Begin(logger, "Command:ResolveTurn", PartyId, turn: 4))
        {
            logger.LogInformation("inside");
        }

        logs.Entries.Single().Scope.Should()
            .Contain("Operation", "Command:ResolveTurn").And.Contain("PartyId", PartyId).And.Contain("Turn", 4);
    }
}
