using DungeonForge.Api.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Tests.Errors;

public class ErrorMappingTests
{
    private static readonly Guid PartyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ErrorSpec Spec(ErrorKind kind, int? status = null, string? field = null) =>
        new("test.failed", kind, "Something went wrong.", status, field);

    private static ProblemHttpResult Map(Error error) =>
        ErrorMapping.ToProblem(error, new DefaultHttpContext { TraceIdentifier = "trace-1" });

    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Unexpected, 500)]
    public void Status_ComesFromTheKind(ErrorKind kind, int status)
    {
        var result = Map(Spec(kind).Create("Query:Test"));

        result.StatusCode.Should().Be(status);
        result.ProblemDetails.Status.Should().Be(status);
    }

    [Fact]
    public void Status_SpecOverrideWinsOverTheKind()
    {
        var result = Map(Spec(ErrorKind.Conflict, status: 400).Create("Command:Test"));

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Extensions_CarryCodeOperationParametersAndTraceId()
    {
        var error = Spec(ErrorKind.NotFound).Create("Query:GetThing", Param.PartyId(PartyId), Param.Turn(4));

        var problem = Map(error).ProblemDetails;

        problem.Detail.Should().Be("Something went wrong.");
        problem.Extensions["code"].Should().Be("test.failed");
        problem.Extensions["operation"].Should().Be("Query:GetThing");
        problem.Extensions["traceId"].Should().Be("trace-1");
        problem.Extensions["parameters"].Should().BeEquivalentTo(
            new Dictionary<string, string> { ["partyId"] = PartyId.ToString(), ["turn"] = "4" });
    }

    [Fact]
    public void WithoutAField_TheBodyIsPlainProblemDetails()
    {
        var problem = Map(Spec(ErrorKind.NotFound).Create("Query:Test")).ProblemDetails;

        problem.Should().NotBeOfType<HttpValidationProblemDetails>();
    }

    [Fact]
    public void WithAField_TheBodyKeepsTheErrorsDictionaryTheClientReads()
    {
        var problem = Map(Spec(ErrorKind.Conflict, status: 400, field: "actions").Create("Command:Test")).ProblemDetails;

        var validation = problem.Should().BeOfType<HttpValidationProblemDetails>().Subject;
        validation.Errors.Should().ContainKey("actions").WhoseValue.Should().Equal("Something went wrong.");
        problem.Status.Should().Be(400);
    }

    [Fact]
    public void ExplicitFields_AreMappedAsGiven()
    {
        var error = Spec(ErrorKind.Validation).Create("Command:Test") with
        {
            Fields = new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required."],
                ["members"] = ["At least one member is required."],
            },
        };

        var validation = Map(error).ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Subject;

        validation.Errors.Keys.Should().BeEquivalentTo("name", "members");
    }
}
