using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting;

public enum SpellUseContext
{
    Combat,
    Exploration
}

public sealed class SpellCastRequest
{
    public Character Caster { get; set; } = null!;
    public string SpellId { get; set; } = string.Empty;
    public SpellUseContext Context { get; set; }
    public List<SpellCastTarget> Targets { get; set; } = new();
    public List<Character> PartyTargets { get; set; } = new();
    public List<MonsterInstance> MonsterTargets { get; set; } = new();
    public Random? Rng { get; set; }
    public Spell? Spell { get; set; }
    public int RoundNumber { get; set; }
    public CombatSession? CombatSession { get; set; }
    public bool IsScrollSpell { get; set; }
    public string? SourceItemName { get; set; }
    public int? EffectiveCasterLevel { get; set; }

    /// <summary>Asks for the Malor destination and teleports the party; supplied by the game window. Returns whether it worked.</summary>
    public Func<(bool Teleported, string Message)>? MalorTeleport { get; set; }

    /// <summary>Set when the spell is cast on an item instead of a character (Remove Curse).</summary>
    public Adnd.Core.Items.Item? TargetItem { get; set; }
}
