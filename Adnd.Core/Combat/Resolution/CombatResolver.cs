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
        ["Staff of Montino"] = "staff quarter",
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

        var state = new RoundState(session, events, partyActions, parrying)
        {
            ChantActiveAtRoundStart = chantActiveAtRoundStart,
            PrayerActiveAtRoundStart = prayerActiveAtRoundStart,
            TimeStopCaster = timeStopCaster,
            TimeStopExtraRound = timeStopExtraRound,
        };

        if (monstersActFirst)
            goto MonsterPhase;

    PartyPhase:
        foreach (var member in session.Party)
        {
            if (ResolvePartyMemberTurn(state, member))
                return FinalizeRound(session, events);
        }

        if (state.PartyAttemptedRun && session.CannotRun)
        {
            events.Add(new CombatEvent("There is no escape from this encounter!"));
        }
        else if (state.PartyAttemptedRun)
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
            if (ResolveMonsterTurn(state, monster))
                return FinalizeRound(session, events);
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
