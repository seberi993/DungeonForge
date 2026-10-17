using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Supervision;

namespace DungeonForge.Api.Features.Social.Abilities;

// Which ability a member wants to use this turn. Sent beside the actions, never inside them: actions are public
// (they are broadcast when the turn resolves), the choice of gift is not.
public sealed record AbilityIntent(Guid MemberId, string AbilityId);

// Per-turn modifiers handed to the dungeon's own roll math. The explore handler stays the owner of the rules; this only
// moves the numbers it already uses.
public sealed class TurnPlan(IReadOnlyList<int> baseChances)
{
    public const int MaxChance = 90;
    public const int MaxShieldPercent = 75;

    public int[] Chances { get; } = [.. baseChances];

    public int[] ShieldPercent { get; } = new int[baseChances.Count];

    public int GoldBonus { get; set; }

    public int Chance(int index) => Math.Clamp(Chances[index], ActionOdds.MinPercent, MaxChance);

    // Damage a failed action takes after the member's shield (never below one unless fully shielded).
    public int Reduce(int index, int damage) =>
        ShieldPercent[index] <= 0 ? damage : (damage * (100 - Math.Min(ShieldPercent[index], MaxShieldPercent)) + 99) / 100;
}

public static class AbilityUse
{
    public static bool IsReady(PlayerProfile profile, Ability ability, int turn) =>
        !profile.LastUsed.TryGetValue(ability.Id, out var last) || turn - last >= ability.Stats.Cooldown;
}

// Effect types are plain strings resolved here. An effect with no handler is inert, so templates can be added or retired
// without a matching code change anywhere else.
public static class AbilityEffects
{
    public static void Apply(AbilityEffect effect, int actionIndex, TurnPlan plan, PlayerProfile user, PartySocial social)
    {
        switch (effect.Type)
        {
            case EffectTypes.RollBonus:
                plan.Chances[actionIndex] += effect.Amount;
                break;
            case EffectTypes.TeamBonus:
                for (var j = 0; j < plan.Chances.Length; j++)
                {
                    if (j != actionIndex) plan.Chances[j] += effect.Amount;
                }

                break;
            case EffectTypes.Shield:
                plan.ShieldPercent[actionIndex] += effect.Amount;
                break;
            case EffectTypes.GoldBonus:
                plan.GoldBonus += effect.Amount;
                break;
            case EffectTypes.GrantToken:
                user.AskTokens += effect.Amount;
                social.Outbox.Add(new SocialNotice(user.Name, "narrator", "A quiet channel opens. You gain a whisper-shard."));
                break;
        }
    }
}

// The explore handler's only contact with the social game. All three calls are no-ops when nothing social is configured.
public sealed class SocialTurnPipeline(IPartyStore parties, SocialModules modules, SocialPipeline pipeline, SocialDelivery delivery)
{
    // A lock-in replaces the member's previous choice: no ability listed for an action clears it.
    public void RecordIntents(Guid partyId, IReadOnlyList<PlayerAction>? actions, IReadOnlyList<AbilityIntent>? intents)
    {
        if (!modules.IsEnabled(SocialModule.Abilities)
            || !parties.TryGet(partyId, out var party))
        {
            return;
        }

        lock (party.SyncRoot)
        {
            var social = party.Social();
            foreach (var action in actions ?? [])
            {
                social.Intents.Remove(action.MemberId);
            }

            foreach (var intent in intents ?? [])
            {
                if (intent.MemberId != Guid.Empty && !string.IsNullOrWhiteSpace(intent.AbilityId))
                {
                    social.Intents[intent.MemberId] = intent.AbilityId;
                }
            }
        }
    }

    // Called under the party lock when the turn resolves, with the base odds. Spends cooldowns, queues private notices.
    public TurnPlan Plan(Party party, IReadOnlyList<PlayerAction> discussion, IReadOnlyList<int> chances, int turn)
    {
        var plan = new TurnPlan(chances);
        if (!modules.IsEnabled(SocialModule.Abilities))
        {
            return plan;
        }

        var social = party.Social();
        for (var i = 0; i < discussion.Count; i++)
        {
            var action = discussion[i];
            if (!social.Intents.TryGetValue(action.MemberId, out var abilityId))
            {
                continue;
            }

            var profile = social.FindProfile(action.Name);
            var ability = profile?.Abilities.FirstOrDefault(a => a.Id == abilityId);
            if (profile is null || ability is null)
            {
                continue;
            }

            if (!AbilityUse.IsReady(profile, ability, turn))
            {
                social.Outbox.Add(new SocialNotice(profile.Name, "narrator", $"{ability.Name} is still recovering."));
                continue;
            }

            profile.LastUsed[ability.Id] = turn;
            foreach (var effect in ability.Stats.Effects)
            {
                AbilityEffects.Apply(effect, i, plan, profile, social);
            }

            pipeline.Process(party, pipeline.NewEvent(
                party, SocialEventTypes.AbilityUsed, turn, [profile.Name], new Dictionary<string, string> { ["ability"] = ability.TemplateId }));
        }

        social.Intents.Clear();
        return plan;
    }

    public Task DeliverAsync(Guid partyId) =>
        parties.TryGet(partyId, out var party)
            ? delivery.DeliverAsync(party)
            : Task.CompletedTask;
}
