using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed class GameSession
{
    public const int StartingHp = 20;

    private readonly List<LogEntry> _log = [];

    public Guid PartyId { get; private set; }
    public string Name { get; private set; } = "";
    public int Hp { get; private set; }
    public int Gold { get; private set; }
    public int Turn { get; private set; }
    public bool IsDefeated { get; private set; }
    public bool HasParty => PartyId != Guid.Empty;
    public IReadOnlyList<LogEntry> Log => _log;

    public void Start(PartyResponse party)
    {
        Clear();
        PartyId = party.PartyId;
        Name = party.Name;
        Hp = StartingHp;
    }

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        Hp = result.Hp;
        Gold = result.Gold;
        IsDefeated = result.Hp <= 0;
        _log.Add(new LogEntry(result.Turn, result.RoomType, result.HpChange, result.GoldFound, result.FlavorEvent));
        Changed?.Invoke();
        return true;
    }

    public event Action? Changed;

    public void Join(PartyStateDto party)
    {
        Clear();
        PartyId = party.PartyId;
        Name = party.Name;
        Hp = party.Hp;
        Gold = party.Gold;
        Turn = party.Turn;
        IsDefeated = party.Hp <= 0;
    }

    public bool ApplyRemote(ExploreResponse result) => Apply(result);

    public void MarkDefeated()
    {
        Hp = 0;
        IsDefeated = true;
    }

    public void Clear()
    {
        PartyId = Guid.Empty;
        Name = "";
        Hp = 0;
        Gold = 0;
        Turn = 0;
        IsDefeated = false;
        _log.Clear();
    }
}
