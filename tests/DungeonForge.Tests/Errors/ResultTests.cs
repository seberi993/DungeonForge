using DungeonForge.Api.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Errors;

public class ResultTests
{
    private static readonly Error Failure = new(
        "test.failed", ErrorKind.Conflict, "Command:Test", "It failed.", new Dictionary<string, string>(), 409);

    [Fact]
    public void ImplicitValue_IsSuccess_AndExposesTheValue()
    {
        Result<int> result = 7;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ImplicitError_IsFailure_AndValueThrows()
    {
        Result<int> result = Failure;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(Failure);
        var read = () => result.Value;
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_PicksTheBranchThatApplies()
    {
        Result<int> ok = 3;
        Result<int> bad = Failure;

        ok.Match(v => $"v{v}", e => e.Code).Should().Be("v3");
        bad.Match(v => $"v{v}", e => e.Code).Should().Be("test.failed");
    }

    [Fact]
    public void NonGeneric_SuccessAndFailure()
    {
        Result.Success.IsSuccess.Should().BeTrue();
        Result failed = Failure;

        failed.IsFailure.Should().BeTrue();
        failed.Error.Should().BeSameAs(Failure);
        failed.Match(() => "ok", e => e.Code).Should().Be("test.failed");
    }
}
