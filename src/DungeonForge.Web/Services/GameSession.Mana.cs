using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// Mana: a client-side estimate for this player's hero only (the server has no mana pool).
public sealed partial class GameSession
{
    private int? _mana;
    private int _reservedMana;
    private IReadOnlyList<SpellDto> _spells = [];

    public int MaxMana => ManaRules.MaxFor(PlayerClass);
    public int Mana => Math.Clamp(_mana ?? MaxMana, 0, MaxMana);

    public bool HasSpells => _spells.Count > 0;

    public void SetSpells(IReadOnlyList<SpellDto> spells) => _spells = [.. spells];

    // Spends the cost of the (longest-named) spell mentioned in the action; a later lock-in replaces an earlier one.
    public int ReserveMana(string actionText)
    {
        ReleaseMana();
        var spell = _spells
            .Where(s => actionText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Name.Length)
            .FirstOrDefault();
        if (spell is null) return 0;

        var before = Mana;
        _mana = Math.Max(0, before - spell.ManaCost);
        _reservedMana = before - _mana.Value;
        Changed?.Invoke();
        return spell.ManaCost;
    }

    public void ReleaseMana()
    {
        if (_reservedMana == 0) return;
        _mana = Math.Min(MaxMana, Mana + _reservedMana);
        _reservedMana = 0;
        Changed?.Invoke();
    }
}
