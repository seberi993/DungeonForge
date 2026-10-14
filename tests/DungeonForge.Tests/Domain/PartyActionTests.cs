using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyActionTests
{
    private static Party CreateParty() =>
        Party.Create(Guid.NewGuid(), "Heroes", [new PartyMember("Conan", CharacterClass.Warrior)], DateTimeOffset.UtcNow, "ABCD-EFGH");

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Puzzle_IsADistinctRoomType() => Enum.IsDefined(RoomType.Puzzle).Should().BeTrue();

    [Fact]
    public void TrySubmitAction_WithoutPending_ReturnsFalse() =>
        CreateParty().TrySubmitAction(new PlayerAction(A, "Conan", "hit it")).Should().BeFalse();

    [Fact]
    public void TrySubmitAction_StoresActionsInSubmissionOrder()
    {
        var party = CreateParty();
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));

        party.TrySubmitAction(new PlayerAction(A, "Conan", "first")).Should().BeTrue();
        party.TrySubmitAction(new PlayerAction(B, "Merlin", "second")).Should().BeTrue();

        party.Pending!.Discussion!.Select(a => a.Text).Should().Equal("first", "second");
    }

    [Fact]
    public void TrySubmitAction_SameMember_ReplacesInPlace()
    {
        var party = CreateParty();
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));
        party.TrySubmitAction(new PlayerAction(A, "Conan", "old"));
        party.TrySubmitAction(new PlayerAction(B, "Merlin", "other"));

        party.TrySubmitAction(new PlayerAction(A, "Conan", "new"));

        party.Pending!.Discussion!.Select(a => a.Text).Should().Equal("new", "other");
    }

    [Fact]
    public void ApplyChoiceOutcome_ClearsDiscussionWithPending()
    {
        var party = CreateParty();
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));
        party.TrySubmitAction(new PlayerAction(A, "Conan", "x"));

        party.ApplyChoiceOutcome(0, 0);

        party.Pending.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitAction_Concurrent_KeepsOneActionPerMember()
    {
        var party = CreateParty();
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => party.TrySubmitAction(new PlayerAction(i % 2 == 0 ? A : B, "n", $"t{i}")))));

        party.Pending!.Discussion.Should().HaveCount(2);
    }

    [Fact]
    public void TrySubmitAction_BeyondCap_RejectsNewMember_ButReplacingExistingStillWorks()
    {
        var party = CreateParty();
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            party.TrySubmitAction(new PlayerAction(id, "n", "x")).Should().BeTrue();
        }

        party.TrySubmitAction(new PlayerAction(Guid.NewGuid(), "n", "extra")).Should().BeFalse();
        party.Pending!.Discussion.Should().HaveCount(8);

        party.TrySubmitAction(new PlayerAction(ids[3], "n", "replaced")).Should().BeTrue();
        party.Pending!.Discussion!.Should().HaveCount(8).And.Contain(a => a.Text == "replaced");
    }

    [Fact]
    public void ActionBonusCount_IsCappedAtMemberCount()
    {
        var party = CreateParty();
        party.ActionBonusCount.Should().Be(0);
        party.TrySetPending(new PendingEncounter(RoomType.Puzzle, "p"));
        for (var i = 0; i < 5; i++)
        {
            party.TrySubmitAction(new PlayerAction(Guid.NewGuid(), "n", "x"));
        }

        party.ActionBonusCount.Should().Be(1);
    }
}
