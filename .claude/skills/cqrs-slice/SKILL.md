---
name: cqrs-slice
description: Generate an ASP.NET Core Minimal API Vertical Slice with request filtering and handler logic.
---

# Vertical Slice Generation Standard

When requested to build a new feature slice, implement the files inside `src/DungeonForge.Api/Features/<FeatureGroup>/<FeatureName>/`:

1. **`Contracts.cs`**: Contains sealed records for the request (Command/Query) and response.
2. **`Endpoint.cs`**: Registers the route using Minimal APIs, maps endpoint filters for validation, and calls the handler.
3. **`Handler.cs`**: Contains domain processing logic, injecting `IMemoryCache` or the `DungeonDbContext`.

### Example Pattern:
```csharp
public static class CreatePartyEndpoint
{
    public static RouteGroupBuilder MapCreatePartyEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/parties", HandleAsync)
             .WithName("CreateParty");
        return group;
    }

    private static async Task<IResult> HandleAsync(
        CreatePartyCommand command, 
        IMemoryCache cache, 
        CancellationToken ct)
    {
        // Minimal logic implementation
        return Results.Ok(new PartyResponse(Guid.NewGuid(), command.PartyName));
    }
}
```
