using Adnd.Core.Spells.Casting;

namespace Adnd.Core.Combat.Actions;

public sealed class CombatAction
{
    public CombatActionType Type { get; set; }
    public string? SpellId { get; set; }
    public int? ItemInventoryIndex { get; set; }
    public SpellCastTarget? Target { get; set; }
    public string? TargetGroupId { get; set; }

    /// <summary>
    /// The one monster this attack is aimed at, as "group#index", or null to leave the choice to the
    /// resolver. Finer than <see cref="TargetGroupId"/>, which only narrows it to a group and then takes
    /// whichever of them happens to be first alive -- so a party could not finish off the wounded one.
    ///
    /// A named monster that is already dead by the time the round resolves is NOT an error: initiative
    /// means someone else may have killed it first, and the blow falls on the group instead of being lost.
    /// </summary>
    public string? TargetMonsterId { get; set; }

    /// <summary>
    /// Spread the party's attacks around instead of everyone hitting the same monster.
    ///
    /// Opt-in, because it is a real change to how a fight goes rather than a display choice: focusing fire
    /// kills one monster a round sooner, spreading it wounds several and kills none. The rotation is kept on
    /// the session, so each attacker who asks for this takes the next monster along rather than every one of
    /// them independently deciding to hit the same "next" one.
    /// </summary>
    public bool SpreadTargets { get; set; }

    public static CombatAction OfType(CombatActionType type) => new() { Type = type };

    /// <summary>"group#index" for a monster, matching the ids the viewer keys its figures on.</summary>
    public static string MonsterKey(string groupId, int index) => groupId + "#" + index;
}
