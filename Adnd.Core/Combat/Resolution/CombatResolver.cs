using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static readonly Dictionary<string, int[]> MeleeWeaponVsArmorAdjustments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axe battle"] = [-3, -2, -1, -1, 0, 0, 1, 1, 2],
        ["axe hand"] = [-3, -2, -2, -1, 0, 0, 1, 1, 1],
        ["bardiche"] = [-2, -1, 0, 0, 1, 1, 2, 2, 3],
        ["bec de corbin"] = [2, 2, 2, 0, 0, 0, 0, 0, -1],
        ["bill guisarme"] = [0, 0, 0, 0, 0, 0, 1, 0, 0],
        ["bo stick"] = [-9, -7, -5, -3, -1, 0, 1, 0, 3],
        ["club"] = [-5, -4, -3, -2, -1, -1, 0, 0, 1],
        ["dagger"] = [-3, -3, -2, -2, 0, 0, 1, 1, 3],
        ["fauchard"] = [-2, -2, -1, -1, 0, 0, 0, -1, -1],
        ["fauchard fork"] = [-1, -1, -1, 0, 0, 0, 1, 0, 1],
        ["fist or open hand"] = [-7, -5, -3, -1, 0, 0, 2, 0, 4],
        ["flail footman"] = [2, 2, 1, 2, 1, 1, 1, 1, -1],
        ["flail horseman"] = [0, 0, 0, 0, 0, 1, 1, 1, 0],
        ["fork military"] = [-2, -2, -1, 0, 0, 1, 1, 0, 1],
        ["glaive"] = [-1, -1, 0, 0, 0, 0, 0, 0, 0],
        ["glaive guisarme"] = [-1, -1, 0, 0, 0, 0, 0, 0, 0],
        ["guisarme"] = [-2, -2, -1, -1, 0, 0, 0, -1, -1],
        ["guisarme voulge"] = [-1, -1, 0, 1, 1, 1, 0, 0, 0],
        ["halberd"] = [1, 1, 1, 2, 2, 2, 1, 1, 0],
        ["hammer lucern"] = [1, 1, 2, 2, 2, 1, 1, 0, 0],
        ["hammer"] = [0, 1, 0, 1, 0, 0, 0, 0, 0],
        ["jo stick"] = [-8, -6, -4, -2, -1, 0, 1, 0, 2],
        ["lance heavy horse"] = [3, 3, 2, 2, 2, 1, 1, 0, 0],
        ["lance light horse"] = [-2, -2, -1, 0, 0, 0, 0, 0, 0],
        ["lance medium horse"] = [0, 1, 1, 1, 1, 0, 0, 0, 0],
        ["mace footman"] = [1, 1, 0, 0, 0, 0, 0, 1, -1],
        ["mace horseman"] = [1, 1, 0, 0, 0, 0, 0, 0, 0],
        ["morning star"] = [0, 1, 1, 1, 1, 1, 1, 2, 2],
        ["partisan"] = [0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["pick military footman"] = [2, 2, 1, 1, 0, -1, -1, -1, -2],
        ["pick military horseman"] = [1, 1, 1, 1, 0, 0, -1, -1, -1],
        ["pike awl"] = [-1, 0, 0, 0, 0, 0, 0, -1, -2],
        ["ranseur"] = [-2, -1, -1, 0, 0, 0, 0, 0, 1],
        ["scimitar"] = [-3, -2, -2, -1, 0, 0, 1, 1, 3],
        ["spear"] = [-2, -1, -1, -1, 0, 0, 0, 0, 0],
        ["spetum"] = [-2, -1, 0, 0, 0, 0, 0, 1, 2],
        ["staff quarter"] = [-7, -5, -3, -1, 0, 0, 1, 1, 1],
        ["sword bastard"] = [0, 0, 1, 1, 1, 1, 1, 1, 0],
        ["sword broad"] = [-3, -2, -1, 0, 0, 1, 1, 1, 2],
        ["sword long"] = [-2, -1, 0, 0, 0, 0, 0, 1, 2],
        ["sword short"] = [-3, -2, -1, 0, 0, 0, 1, 0, 2],
        ["sword two handed"] = [2, 2, 2, 2, 3, 3, 3, 1, 0],
        ["trident"] = [-3, -2, -1, -1, 0, 0, 1, 0, 1],
        ["voulge"] = [-1, -1, 0, 1, 1, 1, 0, 0, 0],
    };

    private static readonly Dictionary<string, int[]> RangedWeaponVsArmorAdjustments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axe hand"] = [-4, -3, -2, -1, -1, 0, 0, 0, 1],
        ["bow composite long"] = [-2, -1, 0, 0, 1, 2, 2, 3, 3],
        ["bow composite short"] = [-3, -3, -1, 0, 1, 2, 2, 2, 3],
        ["bow long"] = [-1, 0, 0, 1, 2, 3, 3, 3, 3],
        ["bow short"] = [-5, -4, -1, 0, 0, 1, 2, 2, 2],
        ["club"] = [-7, -5, -3, -2, -1, -1, -1, 0, 0],
        ["crossbow heavy"] = [-1, 0, 1, 2, 3, 3, 4, 4, 4],
        ["crossbow light"] = [-2, -1, 0, 0, 1, 2, 3, 3, 3],
        ["dagger"] = [-5, -4, -3, -2, -1, -1, 0, 0, 1],
        ["dart"] = [-5, -4, -3, -2, -1, 0, 1, 0, 1],
        ["hammer"] = [-2, -1, 0, 0, 0, 0, 0, 0, 1],
        ["javelin"] = [-5, -4, -3, -2, -1, 0, 1, 0, 1],
        ["sling bullet"] = [-2, -2, -1, 0, 0, 0, 2, 1, 3],
        ["sling stone"] = [-5, -4, -2, -1, 0, 0, 2, 1, 3],
        ["spear"] = [-3, -3, -2, -2, -1, 0, 0, 0, 0],
    };

    // Strict mappings from exact item names in Adnd.Data/Items/*.json to table keys.
    private static readonly Dictionary<string, string> ExactMeleeWeaponNameToTableKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Club"] = "club",
        ["Spear"] = "spear",
        ["Battle Axe"] = "axe battle",
        ["Battle Axe +1"] = "axe battle",
        ["Axe, Hand or throwing"] = "axe hand",
        ["Axe +1"] = "axe hand",
        ["Axe +2"] = "axe hand",
        ["Axe +3"] = "axe hand",
        ["Mace"] = "mace footman",
        ["Mace+1"] = "mace footman",
        ["Mace+2"] = "mace footman",
        ["Mace+3"] = "mace footman",
        ["Mace Pro Poison +2"] = "mace footman",
        ["Dagger"] = "dagger",
        ["Dagger +1"] = "dagger",
        ["Dagger +2"] = "dagger",
        ["Dagger +3"] = "dagger",
        ["Dagger of Speed"] = "dagger",
        ["Thieves Dagger"] = "dagger",
        ["Staff"] = "staff quarter",
        ["Staff +1"] = "staff quarter",
        ["Staff +2"] = "staff quarter",
        ["Staff +3"] = "staff quarter",
        ["Staff of Silence"] = "staff quarter",
        ["Flail"] = "flail military",
        ["Flail +1"] = "flail military",
        ["Flail, footman's"] = "flail footman",
        ["Flail, horseman's"] = "flail horseman",
        ["Fork, Military"] = "flail military",
        ["Hammer"] = "hammer",
        ["Hammer +1"] = "hammer",
        ["Hammer, Lucern"] = "hammer lucern",
        ["Bardiche"] = "bardiche",
        ["Bec de corbin"] = "boc de corbin",
        ["Bill-Guisarme"] = "bill guisarme",
        ["Bo Stick"] = "bo stick",
        ["Fauchard"] = "fauchard",
        ["Fauchard-Fork"] = "fauchard fork",
        ["Glaive"] = "glaive",
        ["Glaive-Guisarme"] = "glaive guisarme",
        ["Guisarme"] = "guisarme",
        ["Guisarme-Voulge"] = "guisarme voulge",
        ["Halberd"] = "halberd",
        ["Jo Stick"] = "jo stick",
        ["Sword, Bastard"] = "sword bastard",
        ["Sword, Broad"] = "sword broad",
        ["Two Handed Sword"] = "sword two handed",
        ["Long Sword"] = "sword long",
        ["Cold Iron Long Sword"] = "sword long",
        ["Silver Long Sword"] = "sword long",
        ["Long Sword +1"] = "sword long",
        ["Long Sword +2"] = "sword long",
        ["Long Sword +3"] = "sword long",
        ["Long Sword +4"] = "sword long",
        ["Long Sword +5"] = "sword long",
        ["Short Sword"] = "sword short",
        ["Short Sword +1"] = "sword short",
        ["Short Sword +2"] = "sword short",
        ["Short Sword +3"] = "sword short",
        ["Short Sword +4"] = "sword short",
        ["Short Sword +5"] = "sword short",
        ["Short Sword -2"] = "sword short",
        ["Sword +1"] = "sword long",
        ["Sword +1, +2 vs. magic-using & enchanted creatures"] = "sword long",
        ["Sword +1, +3 vs. lycanthropes & shape changers"] = "sword long",
        ["Sword +1, +3 vs. regenerating creatures"] = "sword long",
        ["Sword +1, +4 vs. reptiles"] = "sword long",
        ["Sword +1, Flame Tongue"] = "sword long",
        ["Sword +1, Luck Blade"] = "sword long",
        ["Sword +1"] = "sword long",
        ["Sword +2"] = "sword long",
        ["Sword +2, Giant Slayer"] = "sword long",
        ["Sword +2, Dragon Slayer"] = "sword long",
        ["Sword +2, Nine Lives Stealer"] = "sword long",
        ["Sword +3, Frost Brand"] = "sword long",
        ["Sword +3 (No Special Abilities)"] = "sword long",
        ["Sword +4, Defender"] = "sword long",
        ["Sword +5, Holy Avenger"] = "sword long",
        ["Sword of Dancing"] = "sword long",
        ["Sword, Vorpal Weapon"] = "sword long",
        ["Sword of Sharpness"] = "sword long",
        ["Sword of Life Stealing"] = "sword long",
        ["Sword of Wounding"] = "sword long",
        ["Vorpal Blade +1"] = "sword long",
        ["Mage Masher"] = "sword short",
        ["Were Slayer"] = "sword long",
        ["Dragon Slayer"] = "sword long",
        ["Shuriken"] = "dagger",
        ["Muramasa Blade"] = "sword long"
    };

    private static readonly Dictionary<string, string> ExactRangedWeaponNameToTableKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hand Axe (Thrown)"] = "axe hand",
        ["Composite Long Bow"] = "bow composite long",
        ["Composite Short Bow"] = "bow composite short",
        ["Long Bow"] = "bow long",
        ["Short Bow"] = "bow short",
        ["Bow +1"] = "bow long",
        ["Club (Thrown)"] = "club",
        ["Heavy Crossbow"] = "crossbow heavy",
        ["Light Crossbow"] = "crossbow light",
        ["Dagger (Thrown)"] = "dagger",
        ["Dart"] = "dart",
        ["Hammer (Thrown)"] = "hammer",
        ["Hammer of Thunderbolts"] = "hammer",
        ["Javelin"] = "javelin",
        ["Sling"] = "sling stone",
        ["Spear (Thrown)"] = "spear",
        ["Dagger of Venom"] = "dagger",
        ["Hammer +3, Dwarven Thrower"] = "hammer",
        ["Crossbow of Speed"] = "crossbow light",
        ["Crossbow of Speed (Heavy Crossbow)"] = "crossbow heavy",
        ["Crossbow of Distance"] = "crossbow light",
        ["Crossbow of Distance (Heavy Crossbow)"] = "crossbow heavy",
        ["Crossbow of Accuracy, +3"] = "crossbow light",
        ["Crossbow of Accuracy, +3 (Heavy Crossbow)"] = "crossbow heavy",
        ["Javelin of Lightning"] = "javelin"
    };

    private readonly Random _rng = new();//for assassination rolls 

    private readonly IDice _dice;
    private readonly SpellCastingService? _spellCastingService;
    private readonly CharacterSavingThrowService _savingThrowService = new();
    private readonly Dictionary<MonsterInstance, int> _acidSpitRangeInches = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _confusingGazeResolved = new();
    private readonly Dictionary<MonsterInstance, int> _manticoreVolleys = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _sleepGazeResolved = new();
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _fearAuraResolved = new();
    private readonly HashSet<MonsterInstance> _deathGroanUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _nightHagSleepUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _nightHagEnfeeblementUses = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _phaseSpidersInPhase = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance, string)> _ogreMageSpellsUsed = new();
    private readonly Dictionary<MonsterInstance, int> _nightHagMagicMissileUses = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _boneHooks = new();
    private readonly Dictionary<Character, (int Amount, int RoundsRemaining)> _boneDevilStrengthLoss = new(ReferenceEqualityComparer.Instance as IEqualityComparer<Character>);
    private readonly DualClassService _dualClassService = new();

    // Lets Core resolve monster templates by name (e.g. for gating/summoning) without referencing the data layer.
    public Func<string, Monster?>? MonsterTemplateLookup { get; set; }
    private readonly Dictionary<MonsterInstance, bool> _iceDevilSpears = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _hornedWeapons = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _wallOfFireUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _whirlwindUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _rockHurlOpened = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _whirlwindPhase = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _petrifyingBreathUses = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _golemBerserkChance = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _golemBerserk = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<string> _bleedingCharacters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<MonsterInstance> _iceStormUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<string, int> _frightenedRounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Character Owner, Item Weapon)> _thrownWeapons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retrievingWeaponRounds = new(StringComparer.OrdinalIgnoreCase);
    public const string TelekinesisPromptPrefix = "TELEKINESIS_PROMPT::";
    private readonly HashSet<MonsterInstance> _nightHagGateAttempted = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance Hag, Character Victim)> _dreamRidingVictims = new();

    public CombatResolver(IDice? dice = null, SpellCastingService? spellCastingService = null)
    {
        _dice = dice ?? new SystemDice();
        _spellCastingService = spellCastingService;
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatActionType> partyActions)
    {
        var converted = new Dictionary<string, CombatAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in partyActions)
        {
            if (type is not (CombatActionType.Spell or CombatActionType.CastSpell))
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var caster = session.Party.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (caster == null || _spellCastingService == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var spell = _spellCastingService.FindFirstCastableSpell(caster, SpellUseContext.Combat);
            if (spell == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            SpellCastTarget? target = null;
            if (spell.RangeType == SpellRangeType.Enemy)
            {
                var enemy = session.AliveMonsters.FirstOrDefault();
                if (enemy != null)
                    target = SpellCastTarget.Enemy(enemy.Index);
            }
            else if (spell.RangeType == SpellRangeType.Self)
            {
                target = SpellCastTarget.Ally(caster);
            }
            else
            {
                var ally = session.ExposedParty.OrderBy(a => a.CurrentHitPoints).FirstOrDefault() ?? caster;
                target = SpellCastTarget.Ally(ally);
            }

            converted[name] = new CombatAction
            {
                Type = CombatActionType.CastSpell,
                SpellId = spell.Id,
                Target = target
            };
        }

        return ResolveRound(session, converted);
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatAction> partyActions)
    {
        ApplyRingProtectionAuras(session);
        ApplyRingWizardryEffects(session);
        ApplyHolyAvengerPartyAuras(session);

        var events = new List<CombatEvent>
        {
            new($"-- Round {session.RoundNumber} --")
        };
        ApplyMummyRotRaiseWindowRoundProgress(session, events);

        if (session.Outcome != CombatOutcome.InProgress)
            return events;

        ResolveSwallowedCharacters(session, events, advanceTime: false);
        foreach (var monster in session.AliveMonsters)
            TryApplyNauseaFromCarrionStench(monster, session, events);

        var parrying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool partyAttemptedRun = false;

        var chantActiveAtRoundStart = session.IsChantActive;
        var prayerActiveAtRoundStart = session.IsPrayerActive;

        if (chantActiveAtRoundStart)
        {
            var chantCaster = session.Party.FirstOrDefault(p => string.Equals(p.Name, session.ActiveChantCasterName, StringComparison.OrdinalIgnoreCase));
            if (chantCaster == null || !IsAlive(chantCaster))
            {
                session.BreakChant();
                chantActiveAtRoundStart = false;
                events.Add(new CombatEvent("Chant ends as the chanter can no longer continue."));
            }
        }

        if (prayerActiveAtRoundStart)
        {
            var remainingPrayerRounds = session.ActivePrayerRounds;
            var prayerCaster = string.IsNullOrWhiteSpace(session.ActivePrayerCasterName)
                ? "A cleric"
                : session.ActivePrayerCasterName;
            events.Add(new CombatEvent($"{prayerCaster}'s Prayer remains in effect ({remainingPrayerRounds} round(s) remaining)."));
        }

        bool monstersActFirst;
        if (session.RoundNumber == 1 && session.PartySurprisedRound1)
        {
            monstersActFirst = true;
            events.Add(new CombatEvent("Initiative: the surprised party cannot act in round 1, so the monsters act first."));
        }
        else if (session.RoundNumber == 1 && session.MonstersSurprisedRound1)
        {
            monstersActFirst = false;
            events.Add(new CombatEvent("Initiative: the surprised monsters cannot act in round 1, so the party acts first."));
        }
        else
        {
            var partyInitiative = _dice.Roll(6);
            var monsterInitiative = _dice.Roll(6);
            var tieBroken = partyInitiative == monsterInitiative;
            monstersActFirst = tieBroken ? _dice.Roll(2) == 1 : monsterInitiative > partyInitiative;
            events.Add(new CombatEvent($"Initiative: party {partyInitiative} vs monsters {monsterInitiative}"
                + (tieBroken ? " (tie, decided by chance)" : string.Empty)
                + (monstersActFirst ? " - the monsters act first." : " - the party acts first.")));
        }

        var timeStopCaster = session.Party.FirstOrDefault(p => string.Equals(p.Name, session.TimeStopCaster, StringComparison.OrdinalIgnoreCase));
        if (session.TimeStopRounds > 0 && (timeStopCaster == null || !IsAlive(timeStopCaster)))
            session.EndTimeStop();
        var timeStopExtraRound = session.TimeStopRounds > 0 && session.RoundNumber > session.TimeStopCastRound;

        if (monstersActFirst)
            goto MonsterPhase;

    PartyPhase:
        foreach (var member in session.Party)
        {
            if (!IsAlive(member))
                continue;

            if (timeStopExtraRound && !ReferenceEquals(member, timeStopCaster))
                continue;

            TryApplyMaceOfDisruptionEvilBacklash(session, member, events);
            if (!IsAlive(member))
                continue;

            var thunderboltsRest = session.GetPartyThunderboltsRestRounds(member.Name);
            if (thunderboltsRest > 0)
            {
                events.Add(new CombatEvent($"{member.Name} is resting after repeated Hammer of Thunderbolts throws and cannot act ({thunderboltsRest} round(s) remaining)."));
                session.TickPartyThunderboltsRest(member.Name);
                continue;
            }

            if (session.RoundNumber == 1 && session.PartySurprisedRound1)
            {
                if (!partyActions.TryGetValue(member.Name, out var surpriseAction))
                    surpriseAction = CombatAction.OfType(CombatActionType.Parry);

                if (!CanActDuringCompleteSurpriseWithCrossbowOfSpeed(member, surpriseAction))
                {
                    events.Add(new CombatEvent($"{member.Name} is surprised and cannot act in round 1."));
                    continue;
                }

                events.Add(new CombatEvent($"{member.Name}'s Crossbow of Speed snaps to readiness and can fire once despite complete surprise (2nd segment effect)."));
            }

            var drainRemaining = session.GetPartyDrainBloodRemaining(member.Name);
            if (drainRemaining > 0)
            {
                var drainDieSides = session.GetPartyDrainBloodDieSides(member.Name);
                var drainEffectName = session.GetPartyDrainBloodEffectName(member.Name);
                var drainRoll = _dice.Roll(drainDieSides);
                var drainAmount = session.ConsumePartyDrainBlood(member.Name, drainRoll);
                if (drainAmount > 0)
                {
                    var beforeHp = member.CurrentHitPoints;
                    member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - drainAmount);
                    var actualDrain = beforeHp - member.CurrentHitPoints;
                    WakeCharacterIfAsleepAfterDamage(member, actualDrain, events);

                    var remainingAfter = session.GetPartyDrainBloodRemaining(member.Name);
                    events.Add(new CombatEvent($"{member.Name} suffers {drainEffectName} for {actualDrain} (rolled {drainRoll} on 1d{drainDieSides}). Remaining drain: {remainingAfter}. HP {beforeHp}->{member.CurrentHitPoints}."));

                    if (member.CurrentHitPoints <= 0)
                    {
                        member.AddStatus(CharacterStatus.Dead);
                        events.Add(new CombatEvent($"{member.Name} dies from blood loss!"));
                        continue;
                    }
                }
            }

            if (TryApplyConstrictionDamage(session, member, events))
                continue;

            var veryHotFireExposureRounds = session.GetPartyVeryHotFireExposureRounds(member.Name);
            if (veryHotFireExposureRounds > 0)
            {
                if (TryExtinguishFireWithFrostBrand(member, session, events))
                {
                    veryHotFireExposureRounds = session.GetPartyVeryHotFireExposureRounds(member.Name);
                }

                var veryHotFireDamage = session.GetPartyVeryHotFireExposureDamagePerRound(member.Name);
                var appliedFireDamage = ApplyFireProtectionDamageReduction(
                    member,
                    veryHotFireDamage,
                    damageDiceCount: 0,
                    isMagicalFire: false,
                    isNormalFire: false);

                if (appliedFireDamage <= 0)
                {
                    events.Add(new CombatEvent($"{member.Name} is protected from very hot fire exposure."));
                }
                else
                {
                    var beforeFireHp = member.CurrentHitPoints;
                    member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - appliedFireDamage);
                    var actualFireDamage = beforeFireHp - member.CurrentHitPoints;
                    WakeCharacterIfAsleepAfterDamage(member, actualFireDamage, events);

                    events.Add(new CombatEvent($"{member.Name} is engulfed in very hot fire for {actualFireDamage} damage. HP {beforeFireHp}->{member.CurrentHitPoints}."));
                    TryBurnAwayWebsFromFireDamage(member, "very hot fire", session, events);

                    if (member.CurrentHitPoints <= 0)
                    {
                        member.AddStatus(CharacterStatus.Dead);
                        events.Add(new CombatEvent($"{member.Name} is consumed by the conflagration!"));
                        continue;
                    }
                }

                var exposureRemaining = session.TickPartyVeryHotFireExposure(member.Name);
                if (exposureRemaining > 0)
                    events.Add(new CombatEvent($"{member.Name} remains within very hot fire ({exposureRemaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{member.Name} is no longer directly within very hot fire."));
            }

            if (_retrievingWeaponRounds.TryGetValue(member.Name, out var retrievingLeft))
            {
                if (retrievingLeft <= 0 || !IsAlive(member) || !_thrownWeapons.ContainsKey(member.Name))
                {
                    _retrievingWeaponRounds.Remove(member.Name);
                }
                else
                {
                    var remainingToFetch = retrievingLeft - 1;
                    if (remainingToFetch <= 0)
                    {
                        _retrievingWeaponRounds.Remove(member.Name);
                        var thrown = _thrownWeapons[member.Name].Weapon;
                        _thrownWeapons.Remove(member.Name);
                        member.Equipment[EquipmentSlot.MainHand] = thrown;
                        events.Add(new CombatEvent($"{member.Name} runs after the weapon, retrieves {thrown.Name} and re-equips it."));
                    }
                    else
                    {
                        _retrievingWeaponRounds[member.Name] = remainingToFetch;
                        events.Add(new CombatEvent($"{member.Name} is running after the weapon thrown by telekinesis ({remainingToFetch} round(s) remaining)."));
                    }

                    continue;
                }
            }

            if (_frightenedRounds.TryGetValue(member.Name, out var frightenedLeft))
            {
                if (frightenedLeft <= 0 || !IsAlive(member))
                {
                    _frightenedRounds.Remove(member.Name);
                }
                else
                {
                    events.Add(new CombatEvent($"{member.Name} is gripped by fear and flees from the fight ({frightenedLeft} round(s) remaining)."));
                    if (frightenedLeft - 1 <= 0)
                    {
                        _frightenedRounds.Remove(member.Name);
                        events.Add(new CombatEvent($"{member.Name} regains courage."));
                    }
                    else
                    {
                        _frightenedRounds[member.Name] = frightenedLeft - 1;
                    }

                    continue;
                }
            }

            if (member.HasStatus(CharacterStatus.Confused))
            {
                var confusedRounds = session.GetPartyConfusedRounds(member.Name);
                events.Add(new CombatEvent(confusedRounds > 0
                    ? $"{member.Name} is confused and cannot act ({confusedRounds} round(s) remaining)."
                    : $"{member.Name} is confused and cannot act."));

                var remainingConfused = session.TickPartyConfused(member.Name);
                if (remainingConfused <= 0)
                {
                    member.RemoveStatus(CharacterStatus.Confused);
                    events.Add(new CombatEvent($"{member.Name} regains clarity."));
                }

                continue;
            }

            if (member.HasStatus(CharacterStatus.Charmed))
            {
                ResolveCharmedCharacterTurn(session, member, events);
                continue;
            }

            var regeneration = GetEquippedRegeneration(member);
            if (regeneration > 0 && member.CurrentHitPoints < member.MaxHitPoints)
            {
                var before = member.CurrentHitPoints;
                member.CurrentHitPoints = Math.Min(member.MaxHitPoints, member.CurrentHitPoints + regeneration);
                var healed = member.CurrentHitPoints - before;
                if (healed > 0)
                    events.Add(new CombatEvent($"{member.Name} regenerates {healed} HP. HP {before}->{member.CurrentHitPoints}."));
            }

            var mirrorRounds = session.GetMirrorImageRounds(member.Name);
            TickBoneDevilStrengthLoss(member, events);
            if (mirrorRounds > 0)
            {
                var imageCount = session.GetMirrorImageCount(member.Name);
                var remaining = session.TickMirrorImage(member.Name);
                if (remaining <= 0 && imageCount > 0)
                    events.Add(new CombatEvent($"{member.Name}'s mirror image fades."));
            }

            if (session.IsBlinking(member.Name) && session.TickBlink(member.Name) <= 0)
                events.Add(new CombatEvent($"{member.Name} stops blinking and stays put."));

            var barkskinRounds = session.GetBarkskinRounds(member.Name);
            if (barkskinRounds > 0)
            {
                var remaining = session.TickBarkskin(member.Name);
                if (remaining <= 0)
                {
                    var bonus = session.GetBarkskinBonus(member.Name);
                    if (bonus > 0)
                    {
                        member.ArmorClass += bonus;
                        session.ClearBarkskin(member.Name);
                        events.Add(new CombatEvent($"{member.Name}'s Barkskin fades (+{bonus} AC removed)."));
                    }
                }
            }

            var hasteRounds = session.GetHasteRounds(member.Name);
            if (hasteRounds > 0)
            {
                var hasteRemaining = session.TickHaste(member.Name);
                if (hasteRemaining <= 0)
                {
                    var originalMove = session.GetHasteOriginalMove(member.Name);
                    if (originalMove > 0)
                        member.Move = originalMove;

                    session.ClearHaste(member.Name);
                    events.Add(new CombatEvent($"{member.Name}'s Haste fades."));
                }
            }

            var strengthRounds = session.GetStrengthBuffRounds(member.Name);
            if (strengthRounds > 0)
            {
                var remaining = session.TickStrengthBuff(member.Name);
                if (remaining <= 0)
                {
                    var bonus = session.GetStrengthBuffBonus(member.Name);
                    if (bonus > 0)
                    {
                        member.Abilities.Strength = Math.Max(1, member.Abilities.Strength - bonus);
                        session.ClearStrengthBuff(member.Name);
                        member.TemporaryStrengthBonus = 0;
                        member.TemporaryStrengthRoundsRemaining = 0;
                        events.Add(new CombatEvent($"{member.Name}'s Strength spell fades (−{bonus} STR)."));
                    }
                }
                else
                {
                    member.TemporaryStrengthBonus = session.GetStrengthBuffBonus(member.Name);
                    member.TemporaryStrengthRoundsRemaining = remaining;
                }
            }

            if (member.FireProtectionRoundsRemaining > 0)
            {
                member.FireProtectionRoundsRemaining = Math.Max(0, member.FireProtectionRoundsRemaining - 1);
                if (member.FireProtectionRoundsRemaining <= 0)
                {
                    member.ClearProtectionFromFire();
                    events.Add(new CombatEvent($"{member.Name}'s Protection From Fire expires."));
                }
            }

            if (member.LightningProtectionRoundsRemaining > 0)
            {
                member.LightningProtectionRoundsRemaining = Math.Max(0, member.LightningProtectionRoundsRemaining - 1);
                if (member.LightningProtectionRoundsRemaining <= 0)
                {
                    member.ClearProtectionFromLightning();
                    events.Add(new CombatEvent($"{member.Name}'s Protection From Lightning expires."));
                }
            }

            if (member.PotionFireResistanceRoundsRemaining > 0)
            {
                member.PotionFireResistanceRoundsRemaining = Math.Max(0, member.PotionFireResistanceRoundsRemaining - 1);
                if (member.PotionFireResistanceRoundsRemaining <= 0)
                {
                    member.ClearPotionFireResistance();
                    events.Add(new CombatEvent($"{member.Name}'s Potion of Fire Resistance effect expires."));
                }
            }

            if (member.PotionInvulnerabilityRoundsRemaining > 0)
            {
                member.PotionInvulnerabilityRoundsRemaining = Math.Max(0, member.PotionInvulnerabilityRoundsRemaining - 1);
                if (member.PotionInvulnerabilityRoundsRemaining <= 0)
                {
                    member.ClearPotionInvulnerability();
                    events.Add(new CombatEvent($"{member.Name}'s Potion of Invulnerability effect expires."));
                }
            }

            if (member.ProtectionFromMagicScrollRoundsRemaining > 0)
            {
                member.ProtectionFromMagicScrollRoundsRemaining = Math.Max(0, member.ProtectionFromMagicScrollRoundsRemaining - 1);
                if (member.ProtectionFromMagicScrollRoundsRemaining <= 0)
                {
                    member.ClearProtectionFromMagicScroll();
                    events.Add(new CombatEvent($"{member.Name}'s anti-magic protection expires."));
                }
            }

            if (member.ColdResistanceRoundsRemaining > 0)
            {
                member.ColdResistanceRoundsRemaining = Math.Max(0, member.ColdResistanceRoundsRemaining - 1);
                if (member.ColdResistanceRoundsRemaining <= 0)
                {
                    member.ClearResistCold();
                    events.Add(new CombatEvent($"{member.Name}'s Resist Cold expires."));
                }
            }

            var partySlowRounds = session.GetPartySlowRounds(member.Name);
            if (partySlowRounds > 0)
            {
                var slowRemaining = session.TickPartySlow(member.Name);
                if (slowRemaining <= 0)
                {
                    var originalMove = session.GetPartySlowOriginalMove(member.Name);
                    if (originalMove > 0)
                        member.Move = originalMove;

                    session.ClearPartySlow(member.Name);
                    member.RemoveStatus(CharacterStatus.Slowed);
                    events.Add(new CombatEvent($"{member.Name} is no longer slowed."));
                }
                else
                {
                    member.AddStatus(CharacterStatus.Slowed);
                }
            }
            else if (member.HasStatus(CharacterStatus.Slowed))
            {
                member.RemoveStatus(CharacterStatus.Slowed);
            }

            if (member.HasStatus(CharacterStatus.Invisible))
            {
                var improvedRounds = session.GetImprovedInvisibilityRounds(member.Name);
                if (improvedRounds > 0)
                {
                    var remaining = session.TickImprovedInvisibility(member.Name);
                    if (remaining <= 0)
                    {
                        member.RemoveStatus(CharacterStatus.Invisible);
                        member.ArmorClass += 4;
                        events.Add(new CombatEvent($"{member.Name}'s improved invisibility fades."));
                    }
                }
            }

            if (member.HasStatus(CharacterStatus.Asleep))
            {
                var roundsRemaining = session.GetPartyAsleepRounds(member.Name);
                events.Add(new CombatEvent(roundsRemaining > 0
                    ? $"{member.Name} is asleep and cannot act ({roundsRemaining} round(s) remaining)."
                    : $"{member.Name} is asleep and cannot act."));

                var afterTick = session.TickPartyAsleep(member.Name);
                if (afterTick <= 0)
                {
                    member.RemoveStatus(CharacterStatus.Asleep);
                    events.Add(new CombatEvent($"{member.Name} wakes up."));
                }

                continue;
            }

            if (IsHeldByMonster(session, member, events))
                continue;

            if (member.HasStatus(CharacterStatus.Webbed))
            {
                var roundsRemaining = session.GetPartyWebbedRounds(member.Name);
                events.Add(new CombatEvent(roundsRemaining > 0
                    ? $"{member.Name} is webbed and cannot act ({roundsRemaining} round(s) remaining)."
                    : $"{member.Name} is webbed and cannot act."));

                var afterTick = session.TickPartyWebbed(member.Name);
                if (afterTick <= 0)
                {
                    member.RemoveStatus(CharacterStatus.Webbed);
                    events.Add(new CombatEvent($"{member.Name} breaks free of the webs."));
                }

                continue;
            }

            if (member.HasStatus(CharacterStatus.Paralyzed))
            {
                var remainingParalysis = member.TickParalysisRound();
                events.Add(new CombatEvent(remainingParalysis > 0
                    ? $"{member.Name} is paralyzed and cannot act ({remainingParalysis} round(s) remaining)."
                    : $"{member.Name} is no longer paralyzed."));
                continue;
            }

            if (member.HasStatus(CharacterStatus.Petrified))
            {
                events.Add(new CombatEvent($"{member.Name} is petrified and cannot act."));
                continue;
            }

            if (member.RotGrubDeathRoundsRemaining > 0)
            {
                member.RotGrubDeathRoundsRemaining = Math.Max(0, member.RotGrubDeathRoundsRemaining - 1);
                if (member.RotGrubDeathRoundsRemaining <= 0)
                {
                    member.CurrentHitPoints = 0;
                    member.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{member.Name} dies as rot grubs reach the heart."));
                    continue;
                }
            }

            if (!partyActions.TryGetValue(member.Name, out var action))
                action = CombatAction.OfType(CombatActionType.Parry);

            if (session.IsSwallowed(member.Name) && action.Type is not (CombatActionType.Fight or CombatActionType.Parry))
            {
                events.Add(new CombatEvent($"{member.Name} is swallowed and can only fight from inside or wait; outside actions and escape are unavailable."));
                continue;
            }
            if (action.Target?.CharacterName is string allyName && session.IsSwallowed(allyName))
            {
                events.Add(new CombatEvent($"{member.Name} cannot reach {allyName} inside the Purple Worm."));
                continue;
            }

            switch (action.Type)
            {
                case CombatActionType.Fight:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartyAttack(session, member, action, events);
                    break;
                case CombatActionType.Parry:
                    parrying.Add(member.Name);
                    events.Add(new CombatEvent($"{member.Name} parries."));
                    break;
                case CombatActionType.UseItem:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartyUseItem(session, member, action, events);
                    break;
                case CombatActionType.BardCharm:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveBardCharm(session, member, action, events);
                    break;
                case CombatActionType.DispellUndead:
                    if (member.HasStatus(CharacterStatus.LycanthropeTransformed))
                    {
                        events.Add(new CombatEvent($"{member.Name} is transformed by lycanthropy and cannot use class abilities."));
                        break;
                    }

                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveDispellUndead(session, member, action, events);
                    break;
                case CombatActionType.LayOnHands:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveLayOnHands(session, member, action, events);
                    break;
                case CombatActionType.MonkBodyHeal:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveMonkBodyHeal(session, member, events);
                    break;
                case CombatActionType.Spell:
                case CombatActionType.CastSpell:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartySpell(session, member, action, events);
                    break;
                case CombatActionType.Run:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    partyAttemptedRun = true;
                    events.Add(new CombatEvent($"{member.Name} tries to run!"));
                    break;
            }

            ResolveSwallowedCharacters(session, events, advanceTime: false);
            if (!session.AliveMonsters.Any())
            {
                session.Outcome = CombatOutcome.Victory;
                events.Add(new CombatEvent("All monsters are defeated!"));
                return FinalizeRound(session, events);
            }
        }

        if (partyAttemptedRun)
        {
            var allEncounterGroupsSnared = session.GetDistinctGroupIds().All(groupId =>
            {
                var livingInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
                return livingInGroup.Count == 0 || livingInGroup.All(m => m.HasStatus(MonsterStatus.Snared));
            });

            if (allEncounterGroupsSnared)
            {
                session.Outcome = CombatOutcome.Escaped;
                events.Add(new CombatEvent("All enemy groups are snared. The party escapes automatically!"));
                return FinalizeRound(session, events);
            }

            events.Add(new CombatEvent(ResolveRunAttempt(session, out var escaped)));
            if (escaped)
            {
                session.Outcome = CombatOutcome.Escaped;
                return FinalizeRound(session, events);
            }
        }

        if (!timeStopExtraRound)
        {
            ResolveFigurineAnimalAllies(session, events);
            ResolveHornOfValhallaSummons(session, events);
        }

        if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
            return FinalizeRound(session, events);
        }

        if (monstersActFirst)
            goto RoundEnd;

    MonsterPhase:
        if (session.TimeStopRounds > 0)
        {
            if (session.RoundNumber > session.TimeStopCastRound)
            {
                session.TimeStopRounds--;
                events.Add(new CombatEvent(session.TimeStopRounds > 0
                    ? "Time is stopped - the monsters stand frozen."
                    : "Time resumes!"));
            }
            else
            {
                events.Add(new CombatEvent("Time is stopped - the monsters stand frozen."));
            }

            if (monstersActFirst)
                goto PartyPhase;
            goto RoundEnd;
        }

        foreach (var monster in session.AliveMonsters.ToList())
        {
            if (!monster.IsAlive || !session.Monsters.Contains(monster))
                continue;

            var levelMagicUseDecisions = new Dictionary<int, MonsterLevelMagicUseDecision>();

            ApplySwordOfWoundingOngoingDamage(session, monster, events);
            if (!monster.IsAlive)
                continue;

            ApplyMonsterRegeneration(session, monster, events);
            if (TryApplyMummyBurningOilSecondRoundDamage(session, monster, events))
                continue;

            if (session.RoundNumber == 1 && session.MonstersSurprisedRound1)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is surprised and cannot act in round 1."));
                continue;
            }

            if (session.RoundNumber == 1 && session.EncounterSleepingMonstersRound1.Contains(monster.DisplayName))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is asleep and cannot act in round 1."));
                continue;
            }

            var isFeebleminded = monster.HasStatus(MonsterStatus.Feebleminded);
            var isSilenced = monster.HasStatus(MonsterStatus.Silenced);

            var monsterBarkskinRounds = session.GetMonsterBarkskinRounds(monster);
            if (monsterBarkskinRounds > 0)
            {
                var remaining = session.TickMonsterBarkskin(monster);
                if (remaining <= 0)
                {
                    var bonus = session.GetMonsterBarkskinBonus(monster);
                    if (bonus > 0)
                    {
                        monster.AdjustArmorClass(bonus);
                        session.ClearMonsterBarkskin(monster);
                        events.Add(new CombatEvent($"{monster.DisplayName}'s Barkskin fades (+{bonus} AC removed)."));
                    }
                }
            }

            if (isSilenced)
            {
                var remainingSilence = monster.TickStatus(MonsterStatus.Silenced);
                if (HasAnyMonsterSpellcastingAbility(monster))
                {
                    events.Add(new CombatEvent(remainingSilence > 0
                        ? $"{monster.DisplayName} is silenced and cannot cast spells ({remainingSilence} round(s) remaining)."
                        : $"{monster.DisplayName} is no longer silenced."));
                }
            }

            if (!isSilenced && isFeebleminded
                && HasAnyMonsterSpellcastingAbility(monster))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is feebleminded and cannot cast spells."));
            }

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Illusionist)
                && ResolveLevel5IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 5, MonsterSpellTradition.MagicUser)
                && ResolveLevel5MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Druid)
                && ResolveLevel5DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Cleric)
                && ResolveLevel5ClericSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 4, MonsterSpellTradition.Illusionist)
                && ResolveLevel4IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 4, MonsterSpellTradition.MagicUser)
                && ResolveLevel4MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 3, MonsterSpellTradition.MagicUser)
                && ResolveLevel3MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Illusionist)
                && ResolveLevel3IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Cleric)
                && ResolveLevel3ClericSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 2, MonsterSpellTradition.Illusionist)
                && ResolveLevel2IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 2, MonsterSpellTradition.MagicUser)
                && ResolveLevel2MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 1, MonsterSpellTradition.MagicUser)
                )
            {
                ResolveLevel1MageSpells(monster, session, events);
                continue;
            }

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Cleric)
                && ResolveLevel1PriestSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 4, MonsterSpellTradition.Druid)
                && ResolveLevel4DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Druid)
                && ResolveLevel3DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 2, MonsterSpellTradition.Druid)
                && ResolveLevel2DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
                && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Druid)
                && ResolveLevel1DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
                && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Illusionist)
                && ResolveLevel1IllusionistSpell(monster, session, events))
                continue;

            if (HasSpecialAbility(monster, "Dragon breath")
                && ResolveDragonBreathCurrentHp(monster, session, events))
                continue;

            if (TryResolveHellHoundBreatheFire(monster, session, events))
                continue;

            if (TryGetHpDamageBreathDamage(monster, out var breathDamage)
                && ResolveHpDamageBreath(monster, breathDamage, session, events))
                continue;

            // A phase spider shifts into phase to bite, so the first attack against it next round can land.
            if (HasSpecialDefense(monster, "Phasing"))
                _phaseSpidersInPhase.Add(monster);

            ApplyConfusingGaze(session, monster, events);
            ApplyFearAura(session, monster, events);
            ApplySleepGaze(session, monster, events);
            ApplyMedusaGaze(session, monster, events);

            if (TryResolveBrownMoldFreezing(session, monster, events))
                continue;

            if (TryResolveManticoreTailSpikes(session, monster, events))
                continue;

            if (TryResolveDeathGroan(session, monster, events))
                continue;

            if (TryResolveOgreMageSpell(session, monster, events))
                continue;

            if (TryResolveNightHagGate(session, monster, events))
                continue;

            if (TryResolveTelekinesisThrow(session, monster, events))
                continue;

            if (TryResolveCauseFear(session, monster, events))
                continue;

            if (TryResolveIceDevilPower(session, monster, events))
                continue;

            if (TryResolveBarbedDevilPower(session, monster, events))
                continue;

            if (TryResolveHornedWallOfFire(session, monster, events))
                continue;

            if (TryResolveEfreetiWish(session, monster, events))
                continue;

            if (TryResolveAirElementalWhirlwind(session, monster, events))
                continue;

            if (TryResolveHurlRocks(session, monster, events))
                continue;

            if (TryResolvePetrifyingBreath(session, monster, events))
                continue;

            if (TryResolveFleshGolemBerserk(session, monster, events))
                continue;

            if (TryResolveLevel8Powers(session, monster, events))
                continue;

            if (TryResolveGreenSlime(session, monster, events))
                continue;

            if (TryResolveLevel9Powers(session, monster, events))
                continue;

            if (TryResolveLevel10Powers(session, monster, events))
                continue;

            if (TryResolveVrockGate(session, monster, events))
                continue;

            if (TryResolveSuccubusPower(session, monster, events))
                continue;

            if (TryResolveNightHagStrangulation(session, monster, events))
                continue;

            if (TryResolveNightHagSleep(session, monster, events))
                continue;

            if (TryResolveNightHagMagicMissile(session, monster, events))
                continue;

            if (TryResolveNightHagEnfeeblement(session, monster, events))
                continue;

            if (TryResolveMonsterAcidSpit(session, monster, events))
                continue;

            if (TryResolveBoneDevilTail(session, monster, events))
                continue;

            if (TryResolveBoneDevilSummon(session, monster, events))
                continue;

            if (TryResolveMonsterPickPockets(session, monster, events))
                continue;

            if (TryResolveMonsterBardCharm(session, monster, events))
                continue;

            if (monster.HasStatus(MonsterStatus.IncendiaryCloud))
            {
                var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
                var beforeHp = monster.CurrentHitPoints;
                var appliedDamage = HasSpecialDefense(monster, "Immunity to fire and heat") ? 0 : rolledDamage;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - appliedDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.IncendiaryCloud);

                if (appliedDamage <= 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is immune to fire and heat and ignores incendiary cloud damage (rolled {rolledDamage})."));
                else
                    events.Add(new CombatEvent($"Incendiary cloud burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains inside the incendiary cloud ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The incendiary cloud around {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is consumed by flames."));
                    continue;
                }
            }

            var monsterMirrorCount = session.GetMonsterMirrorImageCount(monster);
            if (monsterMirrorCount > 0)
            {
                var remaining = session.TickMonsterMirrorImage(monster);
                if (remaining <= 0)
                    events.Add(new CombatEvent($"{monster.DisplayName}'s mirror image fades."));
            }

            if (monster.HasStatus(MonsterStatus.SummonInsects))
            {
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - 2);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.SummonInsects);

                events.Add(new CombatEvent($"Insects bite {monster.DisplayName} for {actualDamage}. HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains swarmed by insects ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The insect swarm around {monster.DisplayName} disperses."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is torn down by the insect swarm."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.DeathFog))
            {
                var rolledDamage = _dice.Roll(10);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.DeathFog);

                events.Add(new CombatEvent($"Death fog engulfs {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains in the death fog ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The death fog around {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} dies in the death fog."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.WallOfFire))
            {
                var resistedWallOfFire = SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, _rng, "Wall of Fire");
                var remaining = monster.TickStatus(MonsterStatus.WallOfFire);

                if (resistedWallOfFire)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} negates wall of fire damage with magic resistance."));
                    if (remaining > 0)
                        events.Add(new CombatEvent($"{monster.DisplayName} remains within wall of fire ({remaining} round(s) remaining)."));
                    else
                        events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} is no longer affecting it."));

                    continue;
                }

                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - 10);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);

                events.Add(new CombatEvent($"Wall of fire burns {monster.DisplayName} for {actualDamage}. HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains within wall of fire ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} is no longer affecting it."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is consumed by the wall of fire."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.AcidArrow))
            {
                var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.AcidArrow);

                events.Add(new CombatEvent($"Acid burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is still corroding ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The acid on {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is destroyed."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.PermanentIllusion))
            {
                var rolledDamage = 0;
                for (int i = 0; i < 16; i++)
                    rolledDamage += _dice.Roll(6);

                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);

                events.Add(new CombatEvent($"{monster.DisplayName} is tormented by permanent illusion for {actualDamage} damage (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} dies from terror and shock."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.Asleep))
            {
                var remaining = monster.TickStatus(MonsterStatus.Asleep);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is asleep ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} wakes up."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Webbed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Webbed);
                var trappedRounds = session.IncrementMonsterWebTrappedRounds(monster);
                var fullTurnsTrapped = trappedRounds / 10;

                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is held in webs ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of the webs."));

                if (fullTurnsTrapped > 0 && monster.IsAlive)
                {
                    var suffocationChance = Math.Min(100, fullTurnsTrapped * 5);
                    var suffocationRoll = _dice.Roll(100);
                    events.Add(new CombatEvent($"{monster.DisplayName} makes a suffocation check at {suffocationChance}% (rolled {suffocationRoll})."));
                    if (suffocationRoll <= suffocationChance)
                    {
                        monster.CurrentHitPoints = 0;
                        monster.SetStatus(MonsterStatus.Webbed, 0);
                        session.ResetMonsterWebTrappedRounds(monster);
                        events.Add(new CombatEvent($"{monster.DisplayName} suffocates in the webs."));
                        continue;
                    }
                }

                if (remaining <= 0)
                    session.ResetMonsterWebTrappedRounds(monster);

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Entangled))
            {
                var remaining = monster.TickStatus(MonsterStatus.Entangled);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is entangled ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of entangle."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Snared))
            {
                var remaining = monster.TickStatus(MonsterStatus.Snared);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is snared ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of the snare."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.TurnedUndead))
            {
                var remaining = monster.TickStatus(MonsterStatus.TurnedUndead);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is turned and cannot attack ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer turned."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Paralyzed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Paralyzed);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is paralyzed ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Stunned))
            {
                var remaining = monster.TickStatus(MonsterStatus.Stunned);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is stunned ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer stunned."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Disarmed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Disarmed);
                events.Add(new CombatEvent(remaining > 0
                    ? $"{monster.DisplayName} is running after its weapon ({remaining} round(s) remaining)."
                    : $"{monster.DisplayName} retrieves its weapon."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Charmed))
            {
                ResolveCharmedMonsterTurn(session, monster, events);
                continue;
            }

            if (monster.HasStatus(MonsterStatus.WalledOff))
            {
                if (WallOfStoneRules.IsBurrower(monster))
                {
                    if (monster.TickStatus(MonsterStatus.WalledOff) > 0)
                        events.Add(new CombatEvent($"{monster.DisplayName} tunnels through the wall of stone."));
                    else
                        events.Add(new CombatEvent($"{monster.DisplayName} bursts through the wall of stone!"));
                }
                else if (WallOfStoneRules.IsBreacher(monster) && _dice.Roll(3) == 1)
                {
                    var how = _dice.Roll(4) switch
                    {
                        1 => "disintegrates the wall of stone",
                        2 => "teleports through the wall of stone",
                        3 => "passes around the wall of stone",
                        _ => "finally batters down the wall of stone"
                    };
                    monster.SetStatus(MonsterStatus.WalledOff, 0);
                    events.Add(new CombatEvent($"{monster.DisplayName} {how}!"));
                }
                else
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is held back by the wall of stone."));
                }

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Mazed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Mazed);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is trapped in the maze ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} finds the exit and returns from the maze."));

                continue;
            }

            if (TryResolveConfusedMonsterTurn(session, monster, events))
                continue;

            if (monster.HasStatus(MonsterStatus.Panicked))
            {
                var remaining = monster.TickStatus(MonsterStatus.Panicked);
                var fleeRoll = _dice.Roll(100);
                if (fleeRoll <= 50)
                {
                    monster.HasFled = true;
                    monster.CurrentHitPoints = 0;
                    events.Add(new CombatEvent($"{monster.DisplayName} flees in panic!"));
                    continue;
                }

                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} panics and cannot act ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains its nerve."));

                continue;
            }

            var isSlowed = monster.HasStatus(MonsterStatus.Slowed);
            if (isSlowed)
            {
                var remaining = monster.TickStatus(MonsterStatus.Slowed);
                if (remaining > 0)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is slowed ({remaining} round(s) remaining)."));
                }
                else
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer slowed."));
                    isSlowed = false;
                }
            }

            if (isSlowed && session.RoundNumber % 2 != 0)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is too slowed to attack this round."));
                continue;
            }

            if (monster.HasStatus(MonsterStatus.Unconscious))
            {
                var remaining = monster.TickStatus(MonsterStatus.Unconscious);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is unconscious ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains consciousness."));

                continue;
            }

            if (IsShrieker(monster))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} shrieks and does not make physical attacks."));
                continue;
            }

            var isPiercer = IsPiercer(monster);
            if (isPiercer)
            {
                var climbRounds = session.GetPiercerClimbRounds(monster);
                if (climbRounds > 0)
                {
                    var remainingClimb = session.TickPiercerClimbRounds(monster);
                    if (remainingClimb > 0)
                        events.Add(new CombatEvent($"{monster.DisplayName} climbs up the wall ({remainingClimb} round(s) remaining) and cannot attack."));
                    else
                        events.Add(new CombatEvent($"{monster.DisplayName} finishes climbing and can attack next round."));

                    continue;
                }
            }

            if (TryResolvePyroHydraBreath(monster, session, events))
                continue;

            if (TryResolveMonsterLayOnHands(session, monster, events))
                continue;

            if (TryResolveMonsterBodyHeal(session, monster, events))
                continue;

            if (TryResolveMonsterColdRadiation(session, monster, events))
                continue;

            if (TryResolveMonsterAssassination(session, monster, events))
                continue;

            var monsterBackstabInfo = TryGetMonsterThiefBackstabInfo(session, monster);

            var attacks = monster.Template.Attacks.Count > 0 ? monster.Template.Attacks : new List<Adnd.Core.Monsters.MonsterAttack> { new() { NumberOfAttacks = 1, Damage = "1d4", Name = "Claw" } };

            foreach (var attack in attacks)
            {
                if (IsPurpleWorm(monster) && string.Equals(attack.Name, "Sting", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CanPurpleWormSting(session, monster))
                    {
                        LogPurpleWormRule(events, $"{monster.DisplayName} cannot use its tail sting: it needs rear defense or a spacious area with at least four exposed opponents.");
                        continue;
                    }
                    LogPurpleWormRule(events, $"{monster.DisplayName} uses its tail sting in {(GetPurpleWormRearAttackers(session, monster).Any() ? "rear defense" : "a spacious area against numerous opponents")}: 2d4 damage, save vs. poison or die.");
                }
                int attackCount = Math.Max(1, attack.NumberOfAttacks);
                if (_golemHasteRounds.GetValueOrDefault(monster) > 0)
                    attackCount *= 2;
                for (int i = 0; i < attackCount; i++)
                {
                    if (TryResolveBlinkDogPackRetreat(session, monster, events))
                        continue;

                    var rearAttackers = IsPurpleWorm(monster) && string.Equals(attack.Name, "Sting", StringComparison.OrdinalIgnoreCase)
                        ? GetPurpleWormRearAttackers(session, monster).ToList()
                        : new List<Character>();
                    var target = rearAttackers.Count > 0 ? rearAttackers[_dice.Roll(rearAttackers.Count) - 1] : SelectMonsterTarget(session);
                    if (target is null)
                    {
                        if (!session.AliveParty.Any())
                        {
                            session.Outcome = CombatOutcome.Defeat;
                            events.Add(new CombatEvent("The party is defeated."));
                            return FinalizeRound(session, events);
                        }
                        break;
                    }

                    if (!CanMonsterLocateTarget(monster, target, events))
                        continue;

                    var invulnerabilityAcBonus = target.HasActivePotionInvulnerability
                        ? target.PotionInvulnerabilityArmorClassBonus
                        : 0;
                    var targetAc = (target.ArmorClass - invulnerabilityAcBonus) + (parrying.Contains(target.Name) ? 2 : 0);
                    var defenderAcBonus = GetDefenderArmorClassBonus(target, attack);
                    if (defenderAcBonus > 0)
                        targetAc += defenderAcBonus;

                    var blinkBehindAttack = TryResolveBlinkDogTeleportBehind(monster, target, events);
                    if (blinkBehindAttack)
                    {
                        var dexAcBonus = Math.Min(0, AbilitiesTables.DexterityACModifier(target.Abilities.Dexterity));
                        if (dexAcBonus < 0)
                        {
                            targetAc -= dexAcBonus;
                            events.Add(new CombatEvent($"{target.Name} loses Dexterity AC bonus against {monster.DisplayName}'s rear blink attack."));
                        }
                    }

                    var thac0 = GetMonsterThac0(monster);
                    if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
                        thac0 += 1;
                    if (monsterBackstabInfo.Enabled)
                        thac0 -= 4;
                    if (blinkBehindAttack)
                    {
                        thac0 -= 2;
                        events.Add(new CombatEvent($"{monster.DisplayName} gains +2 to hit from partial invisibility while blinking."));
                    }
                    var incapacitatedCharacterReason = GetIncapacitatedTargetReason(target, session);
                    if (!string.IsNullOrWhiteSpace(incapacitatedCharacterReason))
                    {
                        thac0 -= 4;
                        RuleApplicationInfo.PublishLinked(
                            "PHB",
                            "38WeaponTypeToHitAdjustment",
                            $"Weapon type to-hit adjustment: {monster.DisplayName} attacks {target.Name}. Weapon type: natural attack. Adjustment: +4 because target is {incapacitatedCharacterReason}.");
                    }
                    if (monster.HasStatus(MonsterStatus.Blinded)
                        && !target.HasStatus(CharacterStatus.Invisible))
                    {
                        thac0 += 4;
                    }
                    int needed = Math.Clamp(thac0 - targetAc, 1, 20);
                    var cloakDisplacementAutoMiss = ShouldApplyCloakOfDisplacementAutoMiss(session, target);
                    int roll = cloakDisplacementAutoMiss ? 0 : _dice.Roll(20);
                    var hasDoubleDamageOnNaturalTwenty = HasAnySpecialAbility(monster, "Double Damage on Natural 20");

                    if (GameRulesProvider.Current.ShowToHitRoll)
                    {
                        var toHitMessage = $"TO-HIT: {monster.DisplayName} THAC0 {thac0}, {target.Name} AC {targetAc}, needs {needed} on 1d20, rolled {roll}.";
                        events.Add(new CombatEvent(toHitMessage));
                        RuleApplicationInfo.PublishLinked("DMG", "74ToHitTablesFightersAndMonsters", toHitMessage);
                    }

                    if (cloakDisplacementAutoMiss)
                    {
                        events.Add(new CombatEvent($"{monster.DisplayName} attacks where {target.Name} appears to be, but the cloak's displacement causes the strike to miss!"));
                        continue;
                    }

                    if (roll >= needed && session.IsBlinking(target.Name) && _dice.Roll(100) <= 50)
                    {
                        events.Add(new CombatEvent($"{target.Name} blinks out as {monster.DisplayName} strikes; the attack misses."));
                        continue;
                    }

                    if (roll >= needed)
                    {
                        var imageCount = session.GetMirrorImageCount(target.Name);
                        if (imageCount > 0)
                        {
                            var imageHitRoll = _dice.Roll(imageCount + 1);
                            if (imageHitRoll > 1)
                            {
                                var remainingImages = session.RemoveOneMirrorImage(target.Name);
                                events.Add(new CombatEvent($"{monster.DisplayName} hits a mirror image of {target.Name}!"));
                                events.Add(new CombatEvent($"{target.Name} has {remainingImages} mirror image(s) remaining."));
                                if (remainingImages <= 0)
                                    events.Add(new CombatEvent($"{target.Name} has no mirror images left."));
                                continue;
                            }
                        }

                        if (target.HasActivePotionInvulnerability
                            && !IsMonsterAttackConsideredMagical(monster, attack)
                            && (monster.Template.HitDice < 4 || !MonsterHasMagicalProperties(monster)))
                        {
                            events.Add(new CombatEvent($"{monster.DisplayName}'s attack cannot harm {target.Name} due to Potion of Invulnerability."));
                            continue;
                        }

                        int damage = RollDamage(attack.Damage);
                        var enemyDamagePenaltyApplied = chantActiveAtRoundStart || prayerActiveAtRoundStart;
                        if (enemyDamagePenaltyApplied)
                            damage = Math.Max(1, damage - 1);
                        if (monsterBackstabInfo.Enabled)
                        {
                            var baseDamage = damage;
                            damage *= monsterBackstabInfo.Multiplier;
                            events.Add(new CombatEvent($"{monster.DisplayName} backstabs! +4 to hit, damage x{monsterBackstabInfo.Multiplier} ({baseDamage}->{damage})."));
                            RuleApplicationInfo.PublishLinked(
                                "PHB",
                                "27Backstab",
                                $"{monster.DisplayName} uses backstab from surprise/behind: +4 to hit, damage multiplier x{monsterBackstabInfo.Multiplier} ({baseDamage}->{damage}).");
                        }

                        if (hasDoubleDamageOnNaturalTwenty && roll == 20)
                        {
                            var beforeDouble = damage;
                            damage *= 2;
                            events.Add(new CombatEvent($"{monster.DisplayName} rolled a natural 20: Double Damage on Natural 20 ({beforeDouble}->{damage})."));
                        }

                        if (target.HasStatus(CharacterStatus.LycanthropeTransformed)
                            && !CanMonsterHarmLycanthropeForm(monster))
                        {
                            events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} with {attack.Name}, but lycanthrope form ignores non-silver, non-magical attacks."));
                            continue;
                        }

                        target.CurrentHitPoints -= damage;
                        var enemyDamageModText = enemyDamagePenaltyApplied ? " (chant/prayer -1 damage)" : string.Empty;
                        events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} with {attack.Name} for {damage}.{enemyDamageModText}"));
                        WakeCharacterIfAsleepAfterDamage(target, damage, events);

                        if (damage > 0
                            && target.CanBecomeLycanthropeNow()
                            && target.TriggerLycanthropeTransformationByDamage())
                        {
                            events.Add(new CombatEvent($"{target.Name} transforms into lycanthrope form after taking damage!"));
                        }

                        TryApplyDestroyMetalOnMonsterHit(monster, target, events);
                        TryApplyDissolveWoodOnMonsterHit(monster, target, events);

                        if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, target.Name, StringComparison.OrdinalIgnoreCase) && damage > 0)
                        {
                            session.BreakChant();
                            chantActiveAtRoundStart = false;
                            events.Add(new CombatEvent($"{target.Name}'s chant is interrupted and ends."));
                        }

                        if (target.CurrentHitPoints <= 0)
                        {
                            target.CurrentHitPoints = 0;
                            target.AddStatus(CharacterStatus.Dead);
                            TryApplyMummyRotOnDeath(monster, target, events);
                            events.Add(new CombatEvent($"{target.Name} is slain!"));
                        }
                        else
                        {
                            ApplyPurpleWormHit(session, monster, target, attack, roll, needed, events);
                            if (!IsAlive(target) || session.IsSwallowed(target.Name))
                                continue;
                            TryApplyLycanthropy(monster, target, attack.Name, damage, events);
                            TryApplyEarSeekerDisease(monster, target, events);
                            TryApplyInfestation(monster, target, events);
                            TryApplyTickDisease(monster, target, events);
                            TryApplyEnergyDrain(monster, target, session, events);
                            TryApplyLevel8OnHit(session, monster, target, attack.Name, events);
        TryApplyPitFiendTail(monster, target, attack.Name, events);
        TryApplyLevel10OnHit(monster, target, attack.Name, events);
                            if (!IsAlive(target))
                                continue;
                            TryApplyStrengthDrain(monster, target, events);
                            TryApplyDrainBlood(monster, target, session, events);
                            TryApplyRotGrubExposure(monster, target, events);
                            TryApplyGiantRatDisease(monster, target, events);
                            TryApplyOtyughDisease(monster, target, attack.Name, events);
                            TryApplySalamanderHeat(monster, target, events);
                            if (!IsAlive(target))
                                continue;
                            TryApplyRottingDisease(monster, target, events);
                            TryApplyPetrificationFromSpecialAttacks(monster, target, events);
                            TryApplyBoneHook(monster, target, events);
                            TryApplyIceDevilSpear(monster, target, attack.Name, events);
                            TryApplyBarbedFear(monster, target, events);
                            TryApplyHornedDevilAttack(monster, target, attack.Name, events);
                            if (!IsAlive(target))
                                continue;
                            TryApplyErinyesDagger(monster, target, session, events);
                            TryApplyMedusaAspBite(monster, target, events);
                            if (!IsAlive(target))
                                continue;
                            TryApplyWebsSpecialAttack(monster, target, session, events);
                            if (TryApplyGiantSnakePoison(monster, target, events, chantActiveAtRoundStart, prayerActiveAtRoundStart))
                                continue;
                            if (TryApplyPoisonSpit(monster, target, events, chantActiveAtRoundStart, prayerActiveAtRoundStart))
                                continue;
                            if (TryApplyPoisonSpores(monster, target, events, chantActiveAtRoundStart, prayerActiveAtRoundStart))
                                continue;

                            if (HasAnySpecialAbility(monster, "Poison", "Weak Poison", "Huge Spider Poison", "Deadly Poison", "Phase Spider Poison"))
                            {
                                if (target.IsMonkImmuneToPoison())
                                {
                                    events.Add(new CombatEvent($"{target.Name} is immune to poison."));
                                    continue;
                                }

                                var hasDeadlyPoison = HasSpecialAbility(monster, "Deadly Poison");
                                var poisonRoll = _dice.Roll(100);
                                if (hasDeadlyPoison || poisonRoll <= 70)
                                {
                                    var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                                        target,
                                        _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
                                    var isWeakPoison = HasSpecialAbility(monster, "Weak Poison");
                                    var isHugeSpiderPoison = HasSpecialAbility(monster, "Huge Spider Poison");
                                    var isPhaseSpiderPoison = HasSpecialAbility(monster, "Phase Spider Poison");
                                    var poisonSaveBonus = isWeakPoison ? 2 : isHugeSpiderPoison ? 1 : isPhaseSpiderPoison ? -2 : 0;
                                    if (poisonSaveBonus != 0)
                                        saveTarget = Math.Max(1, saveTarget - poisonSaveBonus);
                                    if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
                                        saveTarget = Math.Max(1, saveTarget - 1);
                                    var saveRoll = _dice.Roll(20);
                                    if (saveRoll >= saveTarget)
                                    {
                                        var poisonLabel = isWeakPoison ? "weak poison" : isPhaseSpiderPoison ? "phase spider poison" : "huge spider poison";
                                        var resistMessage = hasDeadlyPoison
                                            ? $"{target.Name} resists deadly poison (save {saveRoll} vs {saveTarget})."
                                            : poisonSaveBonus != 0
                                                ? $"{target.Name} resists {poisonLabel} (save {saveRoll} vs {saveTarget}, {poisonSaveBonus:+#;-#} save modifier)."
                                                : $"{target.Name} resists poison (save {saveRoll} vs {saveTarget}).";
                                        events.Add(new CombatEvent(resistMessage));
                                        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", resistMessage);
                                    }
                                    else if (hasDeadlyPoison)
                                    {
                                        var beforeHp = target.CurrentHitPoints;
                                        target.CurrentHitPoints = 0;
                                        target.RemoveStatus(CharacterStatus.Poisoned);
                                        target.AddStatus(CharacterStatus.Dead);
                                        var failedDeadlyPoisonSaveMessage = $"{target.Name} fails save ({saveRoll} vs {saveTarget}) against deadly poison from {monster.DisplayName} and dies instantly. HP {beforeHp}->0.";
                                        events.Add(new CombatEvent(failedDeadlyPoisonSaveMessage));
                                        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedDeadlyPoisonSaveMessage);
                                    }
                                    else if (!target.HasStatus(CharacterStatus.Poisoned))
                                    {
                                        target.AddStatus(CharacterStatus.Poisoned);
                                        var failedPoisonSaveMessage = poisonSaveBonus != 0
                                            ? $"{target.Name} is poisoned by {(isWeakPoison ? "weak poison" : isPhaseSpiderPoison ? "phase spider poison" : "huge spider poison")} from {monster.DisplayName}! (save {saveRoll} vs {saveTarget}, {poisonSaveBonus:+#;-#} save modifier)"
                                            : $"{target.Name} is poisoned by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})";
                                        events.Add(new CombatEvent(failedPoisonSaveMessage));
                                        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedPoisonSaveMessage);
                                    }
                                }

                            }

                            if (HasAnySpecialAbility(monster, "Paralyze", "Paralyzation", "Paralysis"))
                            {
                                TryApplyMonsterParalyzation(monster, target, session, events);
                            }

                            TryApplyConstrictionOnHit(monster, target, session, events);
                        }
                    }
                    else
                    {
                        events.Add(new CombatEvent($"{monster.DisplayName} misses {target.Name}."));
                    }
                }
            }

            session.RearAttackers.Remove(monster);
            if (isPiercer)
            {
                session.SetPiercerClimbRounds(monster, 5);
                events.Add(new CombatEvent($"{monster.DisplayName} starts climbing up the wall and cannot attack for 5 rounds."));
            }
        }

        if (monstersActFirst)
            goto PartyPhase;

    RoundEnd:
        ApplyPoisonDamageDuringCombat(session, events);
        ApplyBleedingDamage(session, events);

        if (!session.AliveParty.Any())
        {
            session.Outcome = CombatOutcome.Defeat;
            events.Add(new CombatEvent("The party is defeated."));
        }
        else if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
        }

        if (prayerActiveAtRoundStart)
        {
            var afterTickPrayerRounds = session.TickPrayer();
            if (afterTickPrayerRounds <= 0)
                events.Add(new CombatEvent("Prayer fades."));
        }

        return FinalizeRound(session, events);
    }

    private List<CombatEvent> FinalizeRound(CombatSession session, List<CombatEvent> events)
    {
        foreach (var message in session.SwallowingOutcomeMessages)
            LogPurpleWormRule(events, message);
        session.SwallowingOutcomeMessages.Clear();
        if (session.Outcome != CombatOutcome.InProgress)
        {
            ApplyDreamRidingDrain(session, events);
            ClearBoneDevilEffects();
        }
        if (session.Outcome == CombatOutcome.InProgress)
            session.RoundNumber++;

        return events;
    }

    private int GetAttacksThisRound(float attacksPerRound, int roundNumber)
    {
        // 1 attack per round
        if (attacksPerRound <= 1f)
            return 1;

        // 1.25 attacks per round (5/4)
        // Every 4th round: 2 attacks, otherwise 1.
        if (Math.Abs(attacksPerRound - 1.25f) < 0.01f)
            return (roundNumber % 4 == 0) ? 2 : 1;

        // 2 attacks per round
        if (attacksPerRound >= 2f)
            return 2;

        // 1.5 attacks per round (3/2)
        // Odd rounds: 1 attack
        // Even rounds: 2 attacks
        if (Math.Abs(attacksPerRound - 1.5f) < 0.01f)
            return (roundNumber % 2 == 0) ? 2 : 1;

        // fallback
        return 1;
    }

    private int GetMonsterThac0(MonsterInstance monster)
    {
        return Math.Max(1, Math.Max(10, 20 - Math.Max(0, monster.Template.HitDice - 1)) + monster.Thac0Modifier);
    }

    private Character? SelectMonsterTarget(CombatSession session)
    {
        var frontline = session.ExposedParty.Where(member => CombatFormation.CanFight(member, session)).Take(3).ToList();
        if (frontline.Count == 0)
            frontline = session.ExposedParty.ToList();

        if (frontline.Count == 0)
            return null;

        var index = _dice.Roll(frontline.Count) - 1;
        return frontline[index];
    }

    private List<Character> SelectRandomPartyMembersInOneInchRange(CombatSession session)
    {
        var candidates = session.ExposedParty.Where(IsAlive).ToList();
        if (candidates.Count == 0)
            return new List<Character>();

        var affectedCount = Math.Min(candidates.Count, Math.Max(1, _dice.Roll(3)));
        var selected = new List<Character>(affectedCount);
        for (var i = 0; i < affectedCount; i++)
        {
            var index = _dice.Roll(candidates.Count) - 1;
            selected.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return selected;
    }

    private (bool Enabled, int Multiplier) TryGetMonsterThiefBackstabInfo(CombatSession session, MonsterInstance monster)
    {
        if (session.RoundNumber != 1 || !session.PartySurprisedRound1)
            return (false, 1);

        var isThiefByName = monster.Template.Name?.IndexOf("thief", StringComparison.OrdinalIgnoreCase) >= 0;
        var isThiefByAbility = HasAnySpecialAbility(monster, "Thief", "Backstab", "Thief Backstab");
        if (!isThiefByName && !isThiefByAbility)
            return (false, 1);

        var thiefLevel = Math.Max(1, monster.Template.HitDice);
        var multiplier = GetThiefBackstabMultiplierByLevel(thiefLevel);
        return (true, multiplier);
    }

    private int RollDamage(string damageExpression)
    {
        var normalized = (damageExpression ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        normalized = Regex.Replace(normalized, @"\([^)]*\)", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        if (normalized.Contains(" or ", StringComparison.Ordinal))
        {
            var options = normalized
                .Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (options.Length > 0)
                normalized = options[_dice.Roll(options.Length) - 1];
        }

        if (normalized.Contains('/', StringComparison.Ordinal))
        {
            var options = normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (options.Length > 0)
                normalized = options[_dice.Roll(options.Length) - 1];
        }

        if (normalized.Contains("nil", StringComparison.Ordinal)
            || normalized.Contains("none", StringComparison.Ordinal)
            || normalized.Contains("special", StringComparison.Ordinal)
            || normalized.Contains("instant death", StringComparison.Ordinal))
            return 0;

        if (normalized.Contains("level", StringComparison.Ordinal))
            return 0;

        if (normalized.Contains("by weapon", StringComparison.Ordinal))
            return _dice.Roll(8);

        var weaponBonusMatch = Regex.Match(normalized, @"^weapon\s*[+]\s*(?<bonus>\d+)$", RegexOptions.IgnoreCase);
        if (weaponBonusMatch.Success
            && int.TryParse(weaponBonusMatch.Groups["bonus"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weaponBonus))
        {
            return _dice.Roll(8) + Math.Max(0, weaponBonus);
        }

        var diceMatch = Regex.Match(normalized, @"(?<count>\d+)\s*d\s*(?<sides>\d+)\s*(?<mod>[+-]\s*\d+)?", RegexOptions.IgnoreCase);
        if (diceMatch.Success
            && int.TryParse(diceMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && int.TryParse(diceMatch.Groups["sides"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides))
        {
            var mod = 0;
            if (diceMatch.Groups["mod"].Success)
            {
                var modRaw = diceMatch.Groups["mod"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
                _ = int.TryParse(modRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out mod);
            }

            var value = _dice.RollMany(Math.Max(1, sides), Math.Max(1, count)) + mod;
            return Math.Max(0, value);
        }

        var rangeMatch = Regex.Match(normalized, @"^(?<min>\d+)\s*-\s*(?<max>\d+)$");
        if (rangeMatch.Success
            && int.TryParse(rangeMatch.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minRange)
            && int.TryParse(rangeMatch.Groups["max"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRange))
        {
            if (maxRange < minRange)
                (minRange, maxRange) = (maxRange, minRange);

            return _dice.Roll(maxRange - minRange + 1) + minRange - 1;
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var constant))
            return Math.Max(0, constant);

        return 1;
    }

    private static bool HasSpecialAbility(MonsterInstance monster, string abilityName)
    {
        return GetMonsterAbilityNames(monster)
            .Any(name => string.Equals(name, abilityName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetMonsterAbilityNames(MonsterInstance monster)
    {
        if (monster?.Template == null)
            yield break;

        foreach (var name in monster.Template.SpecialAbilities.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }

        foreach (var name in monster.Template.SpecialAttacks.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }

        foreach (var name in monster.Template.SpecialDefenses.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }
    }

    private string ResolveRunAttempt(CombatSession session, out bool escaped)
    {
        var party = session.ExposedParty.Where(IsAlive).ToList();
        var slowest = party
            .Select(c => (Name: c.Name, Speed: GetPartyRunSpeed(session, c)))
            .OrderBy(p => p.Speed)
            .FirstOrDefault();

        var pursuers = session.AliveMonsters
            .Where(m => !m.HasStatus(MonsterStatus.Snared) && !m.HasStatus(MonsterStatus.Asleep)
                && !m.HasStatus(MonsterStatus.Paralyzed) && !m.HasStatus(MonsterStatus.Petrified)
                && !m.HasStatus(MonsterStatus.Unconscious) && !m.HasStatus(MonsterStatus.Entangled)
                && !m.HasStatus(MonsterStatus.Webbed))
            .Select(m => (Name: m.DisplayName, Speed: GetMonsterRunSpeed(m)))
            .OrderByDescending(p => p.Speed)
            .ToList();

        if (pursuers.Count == 0 || slowest.Name == null)
        {
            escaped = true;
            return "The party escapes! No enemy is able to pursue.";
        }

        var fastest = pursuers[0];
        var summary = $"Run speeds: slowest party member {slowest.Name} {slowest.Speed}\", fastest pursuer {fastest.Name} {fastest.Speed}\".";
        if (session.PyrotechnicsCast)
        {
            escaped = true;
            return $"{summary} The party escapes under cover of the pyrotechnics' blinding smoke and sparks.";
        }
        if (slowest.Speed > fastest.Speed)
        {
            escaped = true;
            return $"{summary} The party escapes because even its slowest member outpaces every pursuer.";
        }

        if (slowest.Speed < fastest.Speed)
        {
            escaped = false;
            return $"{summary} The party fails to escape because {fastest.Name} is faster than {slowest.Name}.";
        }

        var roll = _dice.Roll(100);
        escaped = roll <= 50;
        return escaped
            ? $"{summary} Equal speed: 50% chance to escape (rolled {roll}). The party escapes!"
            : $"{summary} Equal speed: 50% chance to escape (rolled {roll}). The party fails to escape!";
    }

    private static int GetPartyRunSpeed(CombatSession session, Character member)
    {
        var speed = Math.Max(1, member.Move);
        if (member.HasStatus(CharacterStatus.Slowed))
            speed /= 2;
        if (session.IsHasted(member.Name))
            speed *= 2;
        return speed;
    }

    private static int GetMonsterRunSpeed(MonsterInstance monster)
    {
        var movement = monster.Template.Movement;
        var speed = movement == null ? 0 : Math.Max(movement.Walk, movement.Fly);
        return monster.HasStatus(MonsterStatus.Slowed) ? speed / 2 : speed;
    }

}
