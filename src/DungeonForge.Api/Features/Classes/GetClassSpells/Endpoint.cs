using DungeonForge.Api.Domain;
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
