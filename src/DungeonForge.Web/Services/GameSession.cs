using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed class GameSession
{
    private readonly List<LogEntry> _log = [];
    private List<MemberDto> _members = [];

    public Guid PartyId { get; private set; }
    public string InviteCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<MemberDto> Members => _members;
    public int TotalHp => _members.Sum(m => m.Hp);
    public int TotalMaxHp => _members.Sum(m => m.MaxHp);
    public int Gold { get; private set; }
    public int Turn { get; private set; }
    public bool IsDefeated { get; private set; }
    public bool HasParty => PartyId != Guid.Empty;
    public IReadOnlyList<LogEntry> Log => _log;

    public void Start(PartyResponse party)
    {
        Clear();
        PartyId = party.PartyId;
        InviteCode = party.InviteCode;
        Name = party.Name;
        _members = [.. party.Members];
    }

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        Gold = result.Gold;
        IsDefeated = result.IsPartyDefeated;
        ApplyDamage(result.TargetName, result.Damage);
        _log.Add(new LogEntry(result.Turn, result.RoomType, result.TargetName, result.Damage, result.GoldFound, result.FlavorEvent));
        Changed?.Invoke();
        return true;
    }

    public event Action? Changed;

    public void Join(PartyStateDto party)
    {
        Clear();
        PartyId = party.PartyId;
        InviteCode = party.InviteCode;
        Name = party.Name;
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        IsDefeated = party.IsPartyDefeated;
    }

    public bool ApplyRemote(ExploreResponse result) => Apply(result);

    public void MarkDefeated()
    {
        _members = [.. _members.Select(m => m with { Hp = 0, IsDefeated = true })];
        IsDefeated = true;
    }

    public void Clear()
    {
        PartyId = Guid.Empty;
        InviteCode = "";
        Name = "";
        _members = [];
        Gold = 0;
        Turn = 0;
        IsDefeated = false;
        _log.Clear();
    }

    private void ApplyDamage(string? targetName, int damage)
    {
        if (targetName is null || damage <= 0) return;
        var index = _members.FindIndex(m => m.Name == targetName);
        if (index < 0) return;
        var hp = Math.Max(0, _members[index].Hp - damage);
        _members[index] = _members[index] with { Hp = hp, IsDefeated = hp == 0 };
    }
}
