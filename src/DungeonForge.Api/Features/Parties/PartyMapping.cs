using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.GetPartyState;

namespace DungeonForge.Api.Features.Parties;

public sealed record MemberDto(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

// Maps from immutable snapshots only, so readers never observe a half-applied turn.
internal static class PartyMapping
{
    public static IReadOnlyList<MemberDto> ToMemberDtos(PartySnapshot snapshot) =>
        snapshot.Members.Select(m => new MemberDto(m.Name, m.Class, m.Hp, m.MaxHp, m.IsDefeated)).ToList();

    public static GetPartyStateResponse ToResponse(PartySnapshot s) =>
        new(s.Id, s.Name, ToMemberDtos(s), s.CreatedAt, s.Gold, s.Turn, s.IsPartyDefeated,
            s.InviteCode.Length == 0 ? "" : InviteCodes.Format(s.InviteCode));
}
