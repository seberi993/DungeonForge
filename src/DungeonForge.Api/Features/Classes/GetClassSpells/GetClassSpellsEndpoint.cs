using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Application.Features.Classes;
using DungeonForge.Application.Features.Classes.GetClassSpells;
using DungeonForge.Application.Errors;
using DungeonForge.Api.Http;
using DungeonForge.Domain;
using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Classes.GetClassSpells;

public sealed class GetClassSpellsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/classes/{id}/spells", Results<Ok<IReadOnlyList<Spell>>, NotFound> (string id) =>
                ClassIdParser.Parse(id) is { } cls
                    ? TypedResults.Ok(SpellCatalog.For(cls))
                    : TypedResults.NotFound())
           .WithName("GetClassSpells");
}
