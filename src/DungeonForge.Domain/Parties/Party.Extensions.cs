namespace DungeonForge.Domain;

// A typed slot for state that belongs to a feature but must share the party's lifetime and lock. Domain stays ignorant of
// the feature: the feature owns the type and exposes it through an extension method, e.g. party.Social().
public sealed partial class Party
{
    private readonly Dictionary<Type, object> _extensions = [];

    // Created on first use, once per type. Callers hold SyncRoot while they use the returned object (the lock is re-entrant).
    public T Extension<T>(Func<Guid, DateTimeOffset, T> create)
        where T : class
    {
        lock (SyncRoot)
        {
            if (!_extensions.TryGetValue(typeof(T), out var value))
            {
                _extensions[typeof(T)] = value = create(Id, CreatedAt);
            }

            return (T)value;
        }
    }
}
