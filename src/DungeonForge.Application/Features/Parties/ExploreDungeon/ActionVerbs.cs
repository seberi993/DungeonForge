using System.Text.RegularExpressions;
using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// Picks the animation a member's plan plays on the client. Pure presentation: it never affects the roll.
// The earliest keyword in the text wins; with no keyword the class decides.
public static partial class ActionVerbs
{
    private static readonly (ActionVerb Verb, Regex Words)[] Rules =
    [
        (ActionVerb.Heal, HealWords()),
        (ActionVerb.Cast, CastWords()),
        (ActionVerb.Stab, StabWords()),
        (ActionVerb.Strike, StrikeWords()),
    ];

    public static ActionVerb Pick(CharacterClass characterClass, string? text)
    {
        var best = (Verb: DefaultFor(characterClass), Index: int.MaxValue);
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var (verb, words) in Rules)
            {
                var match = words.Match(text);
                if (match.Success && match.Index < best.Index)
                {
                    best = (verb, match.Index);
                }
            }
        }

        return best.Verb;
    }

    public static ActionVerb DefaultFor(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Mage or CharacterClass.Necromancer => ActionVerb.Cast,
        CharacterClass.Priest or CharacterClass.Druid => ActionVerb.Heal,
        CharacterClass.Bard => ActionVerb.Stab,
        _ => ActionVerb.Strike,
    };

    [GeneratedRegex(@"\b(?:heal|mend|bless|pray|cure|protect|shield|ward|restore|bandage|revive|cleanse|guard|defend|aid|comfort)", RegexOptions.IgnoreCase)]
    private static partial Regex HealWords();

    [GeneratedRegex(@"\b(?:cast|spell|fire|magic|zap|blast|lightning|hex|curse|summon|conjure|scan|detect|ice|frost|bolt|orb|ritual|wand|staff)", RegexOptions.IgnoreCase)]
    private static partial Regex CastWords();

    [GeneratedRegex(@"\b(?:stab|sneak|backstab|pick|lock|steal|slip|dagger|shoot|arrow|bow|throw|dodge|hide|shadow|sing|song|trick|disarm)", RegexOptions.IgnoreCase)]
    private static partial Regex StabWords();

    [GeneratedRegex(@"\b(?:attack|charge|strike|slash|cleave|smash|swing|hit|punch|kick|bash|pry|break|force|stomp|chop|hack|sword|axe|hammer|club|fight)", RegexOptions.IgnoreCase)]
    private static partial Regex StrikeWords();
}
