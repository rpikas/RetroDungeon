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
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _fearAuraResolved = new();
    private readonly HashSet<MonsterInstance> _deathGroanUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<MonsterInstance> _nightHagSleepUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _nightHagEnfeeblementUses = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _nightHagMagicMissileUses = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly HashSet<(MonsterInstance Monster, Character Victim)> _boneHooks = new();
    private readonly Dictionary<Character, (int Amount, int RoundsRemaining)> _boneDevilStrengthLoss = new(ReferenceEqualityComparer.Instance as IEqualityComparer<Character>);
    private readonly DualClassService _dualClassService = new();

    // Lets Core resolve monster templates by name (e.g. for gating/summoning) without referencing the data layer.
    public Func<string, Monster?>? MonsterTemplateLookup { get; set; }
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

        foreach (var member in session.Party)
        {
            if (!IsAlive(member))
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

            var runRoll = _dice.Roll(100);
            if (runRoll <= 50)
            {
                session.Outcome = CombatOutcome.Escaped;
                events.Add(new CombatEvent("The party escapes!"));
                return FinalizeRound(session, events);
            }

            events.Add(new CombatEvent("The party fails to escape!"));
        }

        ResolveFigurineAnimalAllies(session, events);
        ResolveHornOfValhallaSummons(session, events);

        if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
            return FinalizeRound(session, events);
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

            ApplyConfusingGaze(session, monster, events);
            ApplyFearAura(session, monster, events);

            if (TryResolveDeathGroan(session, monster, events))
                continue;

            if (TryResolveNightHagGate(session, monster, events))
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

            if (monster.HasStatus(MonsterStatus.Charmed))
            {
                ResolveCharmedMonsterTurn(session, monster, events);
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
                            TryApplyStrengthDrain(monster, target, events);
                            TryApplyDrainBlood(monster, target, session, events);
                            TryApplyRotGrubExposure(monster, target, events);
                            TryApplyGiantRatDisease(monster, target, events);
                            TryApplyRottingDisease(monster, target, events);
                            TryApplyPetrificationFromSpecialAttacks(monster, target, events);
                            TryApplyBoneHook(monster, target, events);
                            TryApplyWebsSpecialAttack(monster, target, session, events);
                            if (TryApplyPoisonSpit(monster, target, events, chantActiveAtRoundStart, prayerActiveAtRoundStart))
                                continue;
                            if (TryApplyPoisonSpores(monster, target, events, chantActiveAtRoundStart, prayerActiveAtRoundStart))
                                continue;

                            if (HasAnySpecialAbility(monster, "Poison", "Weak Poison", "Huge Spider Poison", "Deadly Poison"))
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
                                    var poisonSaveBonus = isWeakPoison ? 2 : isHugeSpiderPoison ? 1 : 0;
                                    if (poisonSaveBonus > 0)
                                        saveTarget = Math.Max(1, saveTarget - poisonSaveBonus);
                                    if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
                                        saveTarget = Math.Max(1, saveTarget - 1);
                                    var saveRoll = _dice.Roll(20);
                                    if (saveRoll >= saveTarget)
                                    {
                                        var resistMessage = hasDeadlyPoison
                                            ? $"{target.Name} resists deadly poison (save {saveRoll} vs {saveTarget})."
                                            : poisonSaveBonus > 0
                                                ? $"{target.Name} resists {(isWeakPoison ? "weak poison" : "huge spider poison")} (save {saveRoll} vs {saveTarget}, +{poisonSaveBonus} save bonus)."
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
                                        var failedPoisonSaveMessage = poisonSaveBonus > 0
                                            ? $"{target.Name} is poisoned by {(isWeakPoison ? "weak poison" : "huge spider poison")} from {monster.DisplayName}! (save {saveRoll} vs {saveTarget}, +{poisonSaveBonus} save bonus)"
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

        ApplyPoisonDamageDuringCombat(session, events);
        ResolveSwallowedCharacters(session, events, advanceTime: true);

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

    private void ResolvePartySpell(CombatSession session, Character caster, CombatAction action, List<CombatEvent> events)
    {
        if (caster.HasStatus(CharacterStatus.Petrified))
        {
            events.Add(new CombatEvent($"{caster.Name} is petrified and cannot cast spells."));
            return;
        }

        if (caster.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{caster.Name} is transformed by lycanthropy and cannot cast spells."));
            return;
        }

        if (caster.BreakRingInaudibilityForSpeaking())
            events.Add(new CombatEvent($"{caster.Name} speaks and loses ring inaudibility."));

        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{caster.Name} cannot cast spells right now."));
            return;
        }

        if (string.IsNullOrWhiteSpace(action.SpellId))
        {
            events.Add(new CombatEvent($"{caster.Name} has no spell selected."));
            return;
        }

        var selectedSpell = _spellCastingService.FindSpellById(action.SpellId);
        if (selectedSpell != null
            && _dualClassService.IsSpellFromOriginalClass(caster, selectedSpell.SpellClass))
        {
            _dualClassService.MarkOriginalClassFunctionUsed(caster);
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();

        var result = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = action.SpellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party.Where(p => !session.IsSwallowed(p.Name)).ToList(),
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session
        });

        if (!result.Success)
        {
            events.Add(new CombatEvent($"{caster.Name} fails to cast: {result.Error}"));
            return;
        }

        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private void ResolveBardCharm(CombatSession session, Character bard, CombatAction action, List<CombatEvent> events)
    {
        if (bard.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{bard.Name} is transformed by lycanthropy and cannot use bard abilities."));
            return;
        }

        if (!bard.IsBard())
        {
            events.Add(new CombatEvent($"{bard.Name} cannot use bard charm."));
            return;
        }

        var bardLevel = bard.GetBardLevel();
        var charmPercent = BardRules.GetCharmPercentage(bardLevel);
        if (charmPercent <= 0)
        {
            events.Add(new CombatEvent($"{bard.Name} cannot use bard charm yet."));
            return;
        }

        events.Add(new CombatEvent($"{bard.Name} uses Bard Charm (chance {charmPercent}%)."));
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm used: {bard.Name} L{bardLevel} has {charmPercent}% base chance.");

        var candidates = string.IsNullOrWhiteSpace(action.TargetGroupId)
            ? session.AliveMonsters.ToList()
            : session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();

        candidates = candidates
            .Where(m => !m.HasStatus(MonsterStatus.Charmed))
            .Where(m => m.InstanceMonsterType == MonsterType.Humanoid || m.InstanceMonsterType == MonsterType.Other)
            .ToList();

        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent($"{bard.Name} uses bard charm, but there are no suitable targets."));
            return;
        }

        var target = candidates[_rng.Next(candidates.Count)];
        var roll = _dice.Roll(100);
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm: {bard.Name} L{bardLevel} has {charmPercent}% chance (rolled {roll}) against {target.DisplayName}.");

        if (roll <= charmPercent)
        {
            target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
            events.Add(new CombatEvent($"{bard.Name} charms {target.DisplayName} ({roll} <= {charmPercent})."));
            return;
        }

        events.Add(new CombatEvent($"{bard.Name} fails to charm {target.DisplayName} ({roll} > {charmPercent})."));
    }

    private bool TryResolveMonsterBardCharm(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "BardCharm", "Bardic Music"))
            return false;

        var aliveParty = session.ExposedParty
            .Where(IsAlive)
            .ToList();
        if (aliveParty.Count == 0)
            return false;

        var bardLevel = Math.Max(1, monster.Template.HitDice);
        var charmPercent = BardRules.GetCharmPercentage(bardLevel);
        if (charmPercent <= 0)
            return false;

        var candidates = aliveParty
            .Where(c => !c.HasStatus(CharacterStatus.Charmed))
            .ToList();
        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} uses Bard Charm, but all party members are already charmed."));
            RuleApplicationInfo.PublishLinked(
                "PHB",
                "118BardTable",
                $"Bard charm: {monster.DisplayName} L{bardLevel} has {charmPercent}% chance, but there are no uncharmed party targets.");
            return true;
        }

        var target = candidates[_dice.Roll(candidates.Count) - 1];
        var roll = _dice.Roll(100);
        events.Add(new CombatEvent($"{monster.DisplayName} uses Bard Charm on {target.Name} (chance {charmPercent}%)."));
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm: {monster.DisplayName} L{bardLevel} has {charmPercent}% chance (rolled {roll}) against {target.Name}.");

        if (roll <= charmPercent)
        {
            target.AddStatus(CharacterStatus.Charmed);
            events.Add(new CombatEvent($"{target.Name} is charmed by {monster.DisplayName} ({roll} <= {charmPercent}) and will attack party allies."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} fails to charm {target.Name} ({roll} > {charmPercent})."));
        return true;
    }

    private void ResolvePartyUseItem(CombatSession session, Character user, CombatAction action, List<CombatEvent> events)
    {
        if (user.HasStatus(CharacterStatus.Petrified))
        {
            events.Add(new CombatEvent($"{user.Name} is petrified and cannot use items."));
            return;
        }

        if (user.BreakRingInaudibilityForSpeaking())
            events.Add(new CombatEvent($"{user.Name} speaks and loses ring inaudibility."));

        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{user.Name} cannot use magical items right now."));
            return;
        }

        if (string.Equals(action.SpellId, "__ring_mammal_control__", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveRingOfMammalControlUse(session, user, events))
                events.Add(new CombatEvent($"{user.Name} cannot invoke Ring of Mammal Control right now."));

            return;
        }

        if (!action.ItemInventoryIndex.HasValue || action.ItemInventoryIndex.Value < 0 || action.ItemInventoryIndex.Value >= user.Inventory.Count)
        {
            events.Add(new CombatEvent($"{user.Name} has no valid item selected."));
            return;
        }

        var item = user.Inventory[action.ItemInventoryIndex.Value];
        var spellId = action.SpellId;

        if (ItemSpecialAbilityParser.IsPotionOfDelusion(item))
        {
            var appearedAs = ItemSpecialAbilityParser.TryGetDelusionDisguiseName(item, out var disguise)
                ? disguise
                : item.Name;

            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);
            events.Add(new CombatEvent($"{user.Name} drinks {appearedAs}. It was a Potion of Delusion!"));
            return;
        }

        if (user.HasActiveProtectionFromMagicScroll)
        {
            events.Add(new CombatEvent($"{user.Name} is inside anti-magic protection and cannot activate magical items."));
            return;
        }

        var grantsRegenerationUntilDungeonExit = ItemSpecialAbilityParser.HasCastsAbility(item, "Regeneration");
        var grantsFireResistancePotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Fire Resistance");
        var grantsGiantStrengthPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Giant Strength");
        var grantsAnimalControlPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Animal Control");
        var grantsHumanControlPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Human Control");
        var grantsMammalControlRing = string.Equals(item.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase);
        var grantsHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Heroism");
        var grantsSuperHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Super-Heroism");
        var grantsInvulnerabilityPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Invulnerability");
        var grantsProtectionFromMagicScroll = ItemSpecialAbilityParser.HasCastsAbility(item, "Protection from Magic")
                                             || string.Equals(item.Name, "Scroll of Protection from Magic", StringComparison.OrdinalIgnoreCase);
        var grantsLevitationPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Levitate") || string.Equals(item.Name, "Potion of Levitation", StringComparison.OrdinalIgnoreCase);
        var grantsSpeedPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Haste")
                                || string.Equals(item.Name, "Potion of Speed", StringComparison.OrdinalIgnoreCase);
        var grantsDustOfAppearance = ItemSpecialAbilityParser.HasCastsAbility(item, "Dust of Appearance")
                                     || string.Equals(item.Name, "Dust of Appearance", StringComparison.OrdinalIgnoreCase);
        var grantsTalismanOfPureGood = ItemSpecialAbilityParser.HasSpecialAbility(item, "Talisman of Pure Good");
        var grantsHornOfValhalla = HornOfValhalla.IsHornOfValhalla(item);
        var isPotionOfHealing = string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase);

        bool IsDirectEffectConsumable()
            => grantsRegenerationUntilDungeonExit
               || grantsFireResistancePotion
               || grantsGiantStrengthPotion
               || grantsAnimalControlPotion
               || grantsHumanControlPotion
               || grantsMammalControlRing
               || grantsHeroismPotion
               || grantsSuperHeroismPotion
               || grantsInvulnerabilityPotion
               || grantsProtectionFromMagicScroll
               || grantsLevitationPotion
               || grantsSpeedPotion
               || grantsDustOfAppearance
               || grantsHornOfValhalla
               || isPotionOfHealing;

        bool FailsFighterRestriction()
        {
            if (grantsGiantStrengthPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Giant Strength can only be used by fighters."));
                return true;
            }

            if (grantsHeroismPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Heroism can only be used by fighters."));
                return true;
            }

            if (grantsSuperHeroismPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Super-Heroism can only be used by fighters."));
                return true;
            }

            if (grantsInvulnerabilityPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Invulnerability can only be used by fighters."));
                return true;
            }

            return false;
        }

        void ApplyDirectConsumableEffects()
        {
            if (grantsRegenerationUntilDungeonExit
                && !user.Inventory.Any(inv => ItemSpecialAbilityParser.HasSpecialAbility(inv, "Regeneration (Potion)")))
            {
                user.Inventory.Add(new Item
                {
                    Name = "Regeneration (Potion Effect)",
                    Type = ItemType.MagicItem,
                    IsShopBuyable = false,
                    SpecialAbilities = new List<string> { "Regeneration (Potion)" }
                });

                events.Add(new CombatEvent($"{user.Name} begins regenerating until leaving the dungeon."));
            }

            if (grantsFireResistancePotion)
            {
                user.SetPotionFireResistanceFullDose();
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Fire Resistance (full dose): normal fire immunity, +4 saves vs fire, -2 per fire die for 10 rounds."));
            }

            if (grantsGiantStrengthPotion)
            {
                var roll = _dice.Roll(20);
                var profile = roll switch
                {
                    <= 6 => (Type: "Hill Giant", Carry: 4500, Damage: 7, RockRange: 8, RockDamage: "1-6", BendBars: 50),
                    <= 10 => (Type: "Stone Giant", Carry: 5000, Damage: 8, RockRange: 9, RockDamage: "1-8", BendBars: 60),
                    <= 14 => (Type: "Frost Giant", Carry: 6000, Damage: 9, RockRange: 10, RockDamage: "1-8", BendBars: 70),
                    <= 17 => (Type: "Fire Giant", Carry: 7500, Damage: 10, RockRange: 12, RockDamage: "1-10", BendBars: 80),
                    <= 19 => (Type: "Cloud Giant", Carry: 9000, Damage: 11, RockRange: 14, RockDamage: "1-12", BendBars: 90),
                    _ => (Type: "Storm Giant", Carry: 12000, Damage: 12, RockRange: 16, RockDamage: "1-12", BendBars: 100)
                };

                user.SetPotionGiantStrength(
                    giantType: profile.Type,
                    carryWeightBonus: profile.Carry,
                    damageBonus: profile.Damage,
                    rockRangeInches: profile.RockRange,
                    rockDamage: profile.RockDamage,
                    bendBarsLiftGatesPercent: profile.BendBars);

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Giant Strength (roll {roll}): {profile.Type} strength until dungeon exit (+{profile.Carry} carry, +{profile.Damage} damage). Rock hurling stored: range {profile.RockRange}\" damage {profile.RockDamage}, bend bars/lift gates {profile.BendBars}%."));
            }

            if (isPotionOfHealing)
            {
                var heal = _dice.Roll(4) + _dice.Roll(4) + 2;
                var before = user.CurrentHitPoints;
                user.CurrentHitPoints = Math.Min(user.MaxHitPoints, user.CurrentHitPoints + heal);
                var actual = Math.Max(0, user.CurrentHitPoints - before);
                events.Add(actual > 0
                    ? new CombatEvent($"{user.Name} drinks Potion of Healing and recovers {actual} HP (rolled {heal} on 2d4+2).")
                    : new CombatEvent($"{user.Name} drinks Potion of Healing (rolled {heal} on 2d4+2), but is already at full health."));
            }

            if (grantsHeroismPotion)
            {
                var level = Math.Max(0, user.Level);
                var profile = level switch
                {
                    <= 0 => (LevelBonus: 4, Dice: 4, Bonus: 0),
                    <= 3 => (LevelBonus: 3, Dice: 3, Bonus: 1),
                    <= 6 => (LevelBonus: 2, Dice: 2, Bonus: 2),
                    <= 9 => (LevelBonus: 1, Dice: 1, Bonus: 3),
                    _ => (LevelBonus: 0, Dice: 0, Bonus: 0)
                };

                if (profile.LevelBonus <= 0)
                {
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Heroism, but gains no extra life energy at level {level}."));
                }
                else
                {
                    var rolled = 0;
                    for (var i = 0; i < profile.Dice; i++)
                        rolled += _dice.Roll(10);

                    var bonusHp = rolled + profile.Bonus;
                    user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit."));
                }
            }

            if (grantsAnimalControlPotion)
            {
                var categoryRoll = _dice.Roll(20);
                var category = ResolveAnimalControlCategory(categoryRoll);
                var eligibleAnimals = session.AliveMonsters
                    .Where(m => m.InstanceMonsterType == MonsterType.Animal)
                    .Where(m => IsAnimalControlCategoryMatch(m, category))
                    .ToList();

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Animal Control (d20 {categoryRoll}: {category})."));

                if (eligibleAnimals.Count == 0)
                {
                    events.Add(new CombatEvent("No controllable animals of the potion's type are present."));
                }
                else
                {
                    var targetSize = PickBestAnimalControlSize(eligibleAnimals);
                    var sizeTargets = eligibleAnimals
                        .Where(m => m.Template.Size == targetSize)
                        .OrderBy(_ => _rng.Next())
                        .ToList();

                    var capacity = RollAnimalControlCapacity(targetSize);
                    var attempted = 0;
                    var controlled = 0;

                    foreach (var target in sizeTargets)
                    {
                        if (attempted >= capacity)
                            break;

                        attempted += 1;

                        if (target.HasStatus(MonsterStatus.Charmed))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is already charmed."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, "Animal Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is immune to mind-affecting control."));
                            continue;
                        }

                        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
                        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);

                        if (intelligence >= 5)
                        {
                            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
                            var saveRoll = _dice.Roll(20);
                            if (saveRoll >= saveTarget)
                            {
                                events.Add(new CombatEvent($"{target.DisplayName} resists animal control (save {saveRoll} vs {saveTarget})."));
                                continue;
                            }
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, _rng, "Animal Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} negates animal control with magic resistance."));
                            continue;
                        }

                        target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
                        controlled += 1;
                        events.Add(new CombatEvent($"{target.DisplayName} is brought under animal control and fights for {user.Name}."));
                    }

                    events.Add(new CombatEvent($"Animal control affects {SizeLabel(targetSize)} animals: attempted {attempted} of capacity {capacity}, controlled {controlled}."));
                }
            }

            if (grantsHumanControlPotion)
            {
                var typeRoll = PotionOfHumanControl.EnsureAssignedControlRoll(item, sides => _dice.Roll(Math.Max(1, sides)));
                var category = PotionOfHumanControl.GetControlCategory(typeRoll);
                var durationRounds = 0;
                for (var i = 0; i < 5; i++)
                    durationRounds += _dice.Roll(6);

                var eligibleTargets = session.AliveMonsters
                    .Where(m => IsHumanControlCategoryMatch(m, typeRoll))
                    .OrderBy(_ => _rng.Next())
                    .ToList();

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Human Control (d20 {typeRoll}: {category}, duration {durationRounds} rounds)."));

                if (eligibleTargets.Count == 0)
                {
                    events.Add(new CombatEvent("No eligible humans, humanoids, or demi-humans match this potion's control type."));
                }
                else
                {
                    var hitDiceBudget = 32;
                    var attempted = 0;
                    var controlled = 0;

                    foreach (var target in eligibleTargets)
                    {
                        if (hitDiceBudget <= 0)
                            break;

                        var hitDiceCost = Math.Max(1, GetHumanControlHitDiceCost(target));
                        if (hitDiceCost > hitDiceBudget)
                            continue;

                        attempted += 1;
                        hitDiceBudget -= hitDiceCost;

                        if (target.HasStatus(MonsterStatus.Charmed))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is already charmed."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, "Human Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is immune to mind-affecting control."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, _rng, "Human Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} negates human control with magic resistance."));
                            continue;
                        }

                        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
                        var saveRoll = _dice.Roll(20);
                        if (saveRoll >= saveTarget)
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} resists human control (save {saveRoll} vs {saveTarget})."));
                            continue;
                        }

                        target.SetStatus(MonsterStatus.Charmed, durationRounds);
                        controlled += 1;
                        events.Add(new CombatEvent($"{target.DisplayName} is charmed for {durationRounds} rounds and fights for {user.Name}."));
                    }

                    events.Add(new CombatEvent($"Human control budget: started at 32 HD, attempted {attempted}, controlled {controlled}, remaining {hitDiceBudget}."));
                }
            }

            if (grantsMammalControlRing)
            {
                if (!TryResolveRingOfMammalControlUse(session, user, events))
                    events.Add(new CombatEvent($"{user.Name} cannot invoke Ring of Mammal Control right now."));
            }

            if (grantsProtectionFromMagicScroll)
            {
                var rounds = 0;
                for (var i = 0; i < 5; i++)
                    rounds += _dice.Roll(6);

                user.SetProtectionFromMagicScroll(rounds);
                var protectionMessage = $"{user.Name} reads Scroll of Protection from Magic: anti-magic globe (5' radius) surrounds the reader for {rounds} round(s).";
                events.Add(new CombatEvent(protectionMessage));
                RuleApplicationInfo.PublishLinked("DMG", "128ScrollOfProtectionFromMagic", protectionMessage);
                ResolveProtectionFromMagicItemContact(user, item, events);
            }

            if (grantsSuperHeroismPotion)
            {
                var level = Math.Max(0, user.Level);
                var profile = level switch
                {
                    <= 0 => (LevelBonus: 6, Dice: 5, Bonus: 0),
                    <= 3 => (LevelBonus: 5, Dice: 4, Bonus: 1),
                    <= 6 => (LevelBonus: 4, Dice: 3, Bonus: 2),
                    <= 9 => (LevelBonus: 3, Dice: 2, Bonus: 3),
                    <= 12 => (LevelBonus: 2, Dice: 1, Bonus: 4),
                    _ => (LevelBonus: 0, Dice: 0, Bonus: 0)
                };

                if (profile.LevelBonus <= 0)
                {
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Super-Heroism, but gains no extra life energy at level {level}."));
                }
                else
                {
                    var rolled = 0;
                    for (var i = 0; i < profile.Dice; i++)
                        rolled += _dice.Roll(10);

                    var bonusHp = rolled + profile.Bonus;
                    user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Super-Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit."));
                }
            }

            if (grantsInvulnerabilityPotion)
            {
                var rounds = _dice.Roll(16) + 4; // 5-20 rounds
                user.SetPotionInvulnerability(rounds);
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Invulnerability: +2 AC and +2 saves, and immunity to non-magical attacks from creatures with no magical properties or with fewer than 4 Hit Dice, for {rounds} rounds."));
            }

            if (grantsLevitationPotion)
            {
                user.SetPotionLevitation();
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Levitation and can levitate like the Levitate spell. Carry capacity becomes 6,000 gp equivalent until leaving the dungeon."));
            }

            if (grantsSpeedPotion)
            {
                var rounds = _dice.Roll(16) + 4; // 5-20 rounds
                if (!session.IsHasted(user.Name))
                {
                    session.SetHaste(user.Name, rounds, user.Move);
                    user.Move *= 2;
                }
                else
                {
                    session.SetHaste(user.Name, rounds, session.GetHasteOriginalMove(user.Name));
                }

                user.Age = Math.Max(0, user.Age + 1);
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Speed: move/attacks doubled for {rounds} rounds; ages 1 year permanently."));
            }

            if (grantsDustOfAppearance)
            {
                var revealedCount = 0;
                foreach (var monster in session.Monsters)
                {
                    if (!monster.IsAlive)
                        continue;

                    if (!monster.HasStatus(MonsterStatus.Invisible))
                        continue;

                    monster.SetStatus(MonsterStatus.Invisible, 0);
                    revealedCount += 1;
                }

                events.Add(revealedCount > 0
                    ? new CombatEvent($"{user.Name} uses Dust of Appearance: {revealedCount} invisible monster(s) are revealed.")
                    : new CombatEvent($"{user.Name} uses Dust of Appearance, but no invisible monsters are present."));
            }
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            var spell = _spellCastingService.FindSpellFromItem(item);
            spellId = spell?.Id;
        }

        if (string.Equals(action.SpellId, "__talisman_pure_good__", StringComparison.OrdinalIgnoreCase)
            || grantsTalismanOfPureGood)
        {
            ResolveTalismanOfPureGoodUse(session, user, action, item, events);
            return;
        }

        if (string.Equals(action.SpellId, "__horn_of_valhalla__", StringComparison.OrdinalIgnoreCase)
            || grantsHornOfValhalla)
        {
            ResolveHornOfValhallaUse(session, user, item, events);
            return;
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            if (!IsDirectEffectConsumable())
            {
                events.Add(new CombatEvent($"{item.Name} has no usable spell effect."));
                return;
            }

            if (FailsFighterRestriction())
                return;

            if (item.Type is ItemType.Potion or ItemType.Scroll)
                user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

            ApplyDirectConsumableEffects();
            events.Add(new CombatEvent($"{user.Name} uses {item.Name}."));
            return;
        }

        var targets = item.Type == ItemType.Potion
            ? new List<SpellCastTarget> { SpellCastTarget.Ally(user) }
            : action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();
        var result = _spellCastingService.CastFromItem(new SpellCastRequest
        {
            Caster = user,
            SpellId = spellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party.Where(p => !session.IsSwallowed(p.Name)).ToList(),
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session,
            IsScrollSpell = item.Type == ItemType.Scroll,
            SourceItemName = item.Name
        });

        if (item.Type == ItemType.Scroll)
            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

        if (!result.Success)
        {
            if (result.Events != null && result.Events.Count > 0)
            {
                foreach (var entry in result.Events)
                    events.Add(new CombatEvent(entry));
            }

            events.Add(new CombatEvent($"{user.Name} fails to use {item.Name}: {result.Error}"));
            return;
        }

        if (FailsFighterRestriction())
            return;

        if (item.Type == ItemType.Potion)
            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

        ApplyDirectConsumableEffects();

        events.Add(new CombatEvent($"{user.Name} uses {item.Name}."));
        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private void ResolveTalismanOfPureGoodUse(CombatSession session, Character user, CombatAction action, Item talisman, List<CombatEvent> events)
    {
        var charges = GetOrAssignTalismanOfPureGoodCharges(talisman);
        if (charges <= 0)
        {
            events.Add(new CombatEvent($"{user.Name} tries to use {talisman.Name}, but it has no charges remaining."));
            return;
        }

        SetTalismanOfPureGoodCharges(talisman, charges - 1);
        events.Add(new CombatEvent($"{user.Name} invokes {talisman.Name}. Charges remaining: {Math.Max(0, charges - 1)}."));

        var clericLevel = user.Classes.Contains(CharacterClass.Cleric)
            ? user.GetClassLevel(CharacterClass.Cleric)
            : 0;

        if (clericLevel < 9)
        {
            events.Add(new CombatEvent($"{talisman.Name} can only be properly used by a cleric of level 9 or higher."));
            return;
        }

        if (IsNeutralAlignment(user.Alignment))
        {
            var damage = RollNd4(7);
            ApplyDirectDamageToCharacter(user, damage, events,
                $"{talisman.Name} lashes back at {user.Name} for {{0}} damage (neutral alignment). HP {{1}}->{{2}}.");
            return;
        }

        if (IsEvilAlignment(user.Alignment))
        {
            var damage = RollNd4(12);
            ApplyDirectDamageToCharacter(user, damage, events,
                $"{talisman.Name} devastates {user.Name} for {{0}} damage (evil alignment). HP {{1}}->{{2}}.");
            return;
        }

        if (!IsGoodAlignment(user.Alignment))
        {
            events.Add(new CombatEvent($"{talisman.Name} does not respond to {user.Name}."));
            return;
        }

        var target = FindTargetedEvilCleric(session, action);
        if (target == null)
        {
            events.Add(new CombatEvent($"No evil cleric is affected by {talisman.Name}."));
            return;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{target.DisplayName} is sent to the center of the earth and destroyed by {talisman.Name}!"));
    }

    private static MonsterInstance? FindTargetedEvilCleric(CombatSession session, CombatAction action)
    {
        IEnumerable<MonsterInstance> candidates = session.Monsters.Where(m => m.IsAlive);

        var targetGroup = action.Target?.TargetGroupId;
        if (!string.IsNullOrWhiteSpace(targetGroup) && !string.Equals(targetGroup, "default", StringComparison.OrdinalIgnoreCase))
            candidates = candidates.Where(m => string.Equals(m.GroupId, targetGroup, StringComparison.OrdinalIgnoreCase));

        return candidates.FirstOrDefault(IsEvilClericMonster);
    }

    private static bool IsEvilClericMonster(MonsterInstance monster)
    {
        if (monster == null || !monster.IsAlive)
            return false;

        if (!IsMonsterLikelyCleric(monster))
            return false;

        return IsEvilAlignmentText(monster.Template.Alignment);
    }

    private static bool IsMonsterLikelyCleric(MonsterInstance monster)
    {
        var name = monster.Template.Name ?? string.Empty;
        if (name.Contains("Cleric", StringComparison.OrdinalIgnoreCase))
            return true;

        return monster.Template.SpecialAbilities.Any(a =>
            (a.Name ?? string.Empty).Contains("Cleric", StringComparison.OrdinalIgnoreCase)
            || (a.Description ?? string.Empty).Contains("Cleric", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGoodAlignment(Alignment alignment)
        => alignment is Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood;

    private static bool IsNeutralAlignment(Alignment alignment)
        => alignment is Alignment.LawfulNeutral or Alignment.TrueNeutral or Alignment.ChaoticNeutral;

    private static bool IsEvilAlignment(Alignment alignment)
        => alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;

    private static bool IsEvilAlignmentText(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return false;

        var text = alignment.Trim();
        return text.Equals("Lawful Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Neutral Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Chaotic Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("LE", StringComparison.OrdinalIgnoreCase)
               || text.Equals("NE", StringComparison.OrdinalIgnoreCase)
               || text.Equals("CE", StringComparison.OrdinalIgnoreCase);
    }

    private int RollNd4(int count)
    {
        var total = 0;
        for (var i = 0; i < Math.Max(0, count); i++)
            total += _dice.Roll(4);

        return total;
    }

    private static void ApplyDirectDamageToCharacter(Character target, int damage, List<CombatEvent> events, string messageTemplate)
    {
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - Math.Max(0, damage));
        events.Add(new CombatEvent(string.Format(messageTemplate, Math.Max(0, damage), before, target.CurrentHitPoints)));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain by the talisman's backlash."));
        }
    }

    private static int GetOrAssignTalismanOfPureGoodCharges(Item talisman)
    {
        talisman.SpecialAbilities ??= new List<string>();

        var existing = talisman.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("TalismanPureGoodCharges:", StringComparison.OrdinalIgnoreCase));

        if (existing != null
            && int.TryParse(existing.Split(':', 2)[1], out var parsed))
        {
            return Math.Max(0, parsed);
        }

        talisman.SpecialAbilities.Add("TalismanPureGoodCharges:7");
        return 7;
    }

    private static void SetTalismanOfPureGoodCharges(Item talisman, int charges)
    {
        talisman.SpecialAbilities ??= new List<string>();
        talisman.SpecialAbilities.RemoveAll(a => a.StartsWith("TalismanPureGoodCharges:", StringComparison.OrdinalIgnoreCase));
        talisman.SpecialAbilities.Add($"TalismanPureGoodCharges:{Math.Max(0, charges)}");
    }

    private bool TryResolveRingOfMammalControlUse(CombatSession session, Character user, List<CombatEvent> events)
    {
        if (!HasEquippedRingOfMammalControl(user) && !user.Inventory.Any(i => string.Equals(i.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase)))
            return false;

        events.Add(new CombatEvent($"{user.Name} concentrates for 3 segments and invokes Ring of Mammal Control."));

        var candidates = session.AliveMonsters
            .Where(IsEligibleMammalForRingControl)
            .OrderBy(m => Math.Max(1, m.Template.HitDice))
            .ThenBy(m => m.DisplayName)
            .ToList();

        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent("No eligible mammal is affected."));
            return true;
        }

        const int maxHitDiceControlled = 30;
        var controlledCount = 0;
        var controlledHitDice = 0;

        foreach (var mammal in candidates)
        {
            var hd = Math.Max(1, mammal.Template.HitDice);
            if (controlledHitDice + hd > maxHitDiceControlled)
                continue;

            mammal.SetStatus(MonsterStatus.Charmed, int.MaxValue);
            controlledCount += 1;
            controlledHitDice += hd;
            events.Add(new CombatEvent($"{mammal.DisplayName} is controlled by {user.Name}'s ring."));
        }

        events.Add(new CombatEvent($"Ring of Mammal Control affects {controlledCount} mammal(s), total {controlledHitDice} HD (max 30 HD)."));
        return true;
    }

    private static bool HasEquippedRingOfMammalControl(Character user)
    {
        return (user.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1)
                && ring1 != null
                && string.Equals(ring1.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase))
               ||
               (user.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2)
                && ring2 != null
                && string.Equals(ring2.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEligibleMammalForRingControl(MonsterInstance monster)
    {
        if (monster == null || !monster.IsAlive)
            return false;

        if (monster.InstanceMonsterType != MonsterType.Animal)
            return false;

        if (!IsLikelyMammal(monster))
            return false;

        if (!HasRingMammalControlEligibleIntelligence(monster.Template.Intelligence))
            return false;

        var name = (monster.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("lammasu") || name.Contains("shedu") || name.Contains("manes") || name.Contains("core"))
            return false;

        return true;
    }

    private static bool IsLikelyMammal(MonsterInstance monster)
    {
        var text = $"{monster.Template.Name} {monster.Template.TypeName}".ToLowerInvariant();

        if (text.Contains("bat") || text.Contains("bird") || text.Contains("avian")
            || text.Contains("snake") || text.Contains("lizard") || text.Contains("rept")
            || text.Contains("amphib") || text.Contains("frog") || text.Contains("toad")
            || text.Contains("fish") || text.Contains("shark") || text.Contains("eel")
            || text.Contains("insect") || text.Contains("spider") || text.Contains("scorpion"))
            return false;

        return text.Contains("rat")
               || text.Contains("weasel")
               || text.Contains("badger")
               || text.Contains("wolf")
               || text.Contains("dog")
               || text.Contains("fox")
               || text.Contains("cat")
               || text.Contains("lion")
               || text.Contains("tiger")
               || text.Contains("bear")
               || text.Contains("boar")
               || text.Contains("pig")
               || text.Contains("horse")
               || text.Contains("pony")
               || text.Contains("camel")
               || text.Contains("cow")
               || text.Contains("bull")
               || text.Contains("ox")
               || text.Contains("deer")
               || text.Contains("stag")
               || text.Contains("goat")
               || text.Contains("sheep")
               || text.Contains("ram")
               || text.Contains("dolphin")
               || text.Contains("whale")
               || text.Contains("ape")
               || text.Contains("baboon")
               || text.Contains("monkey")
               || text.Contains("mammal");
    }

    private static bool HasRingMammalControlEligibleIntelligence(string? intelligenceText)
    {
        if (string.IsNullOrWhiteSpace(intelligenceText))
            return false;

        var lower = intelligenceText.Trim().ToLowerInvariant();
        if (lower.StartsWith("non") || lower.StartsWith("animal") || lower.StartsWith("semi"))
            return true;

        var matches = Regex.Matches(lower, @"\d+")
            .Select(m => int.TryParse(m.Value, out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToList();

        if (matches.Count == 0)
            return false;

        return matches.Min() <= 4;
    }

    private static bool IsBroochOfShielding(Item? item)
    {
        if (item == null)
            return false;

        var name = item.Name?.Trim() ?? string.Empty;
        if (name.Contains("Brooch of Shielding", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.SpecialAbilities != null
               && item.SpecialAbilities.Any(a => !string.IsNullOrWhiteSpace(a)
                                                 && a.Contains("Brooch of Shielding", StringComparison.OrdinalIgnoreCase));
    }

    private static Item? FindBroochOfShielding(Character character)
    {
        if (character == null)
            return null;

        if (character.Equipment.TryGetValue(EquipmentSlot.Neck, out var neck)
            && IsBroochOfShielding(neck))
            return neck;

        return character.Inventory.FirstOrDefault(IsBroochOfShielding);
    }

    private static int ApplyBroochOfShieldingAgainstMagicMissile(Character target, int incomingDamage, List<CombatEvent> events)
    {
        if (target == null || incomingDamage <= 0)
            return incomingDamage;

        var brooch = FindBroochOfShielding(target);
        if (brooch == null)
            return incomingDamage;

        if (target.BroochOfShieldingRemainingHitPoints <= 0)
            target.BroochOfShieldingRemainingHitPoints = 101;

        var beforeCapacity = target.BroochOfShieldingRemainingHitPoints;
        var absorbed = Math.Min(beforeCapacity, incomingDamage);
        target.BroochOfShieldingRemainingHitPoints = Math.Max(0, beforeCapacity - absorbed);

        if (absorbed > 0)
        {
            events.Add(new CombatEvent(
                $"{target.Name}'s Brooch of Shielding absorbs {absorbed} magic missile damage ({beforeCapacity}->{target.BroochOfShieldingRemainingHitPoints} capacity)."));
        }

        if (target.BroochOfShieldingRemainingHitPoints <= 0)
        {
            if (target.Equipment.TryGetValue(EquipmentSlot.Neck, out var equippedNeck)
                && ReferenceEquals(equippedNeck, brooch))
            {
                target.Equipment[EquipmentSlot.Neck] = null;
            }

            target.Inventory.RemoveAll(i => ReferenceEquals(i, brooch));
            events.Add(new CombatEvent($"{target.Name}'s Brooch of Shielding melts and becomes useless."));
        }

        return Math.Max(0, incomingDamage - absorbed);
    }

    private void ResolveProtectionFromMagicItemContact(Character user, Item sourceItem, List<CombatEvent> events)
    {
        var candidates = user.Inventory
            .Where(i => !ReferenceEquals(i, sourceItem))
            .Concat(user.Equipment.Values.Where(i => i != null).Select(i => i!))
            .Where(IsMagicalItemForProtectionFromMagic)
            .Distinct()
            .OrderByDescending(GetProtectionFromMagicItemPower)
            .ToList();

        foreach (var magicItem in candidates)
        {
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= 11)
            {
                events.Add(new CombatEvent($"{magicItem.Name} resists anti-magic contact (save {saveRoll} vs 11)."));
                continue;
            }

            DrainItemMagic(magicItem);
            user.ClearProtectionFromMagicScroll();
            events.Add(new CombatEvent($"{magicItem.Name} fails anti-magic contact save ({saveRoll} vs 11), is drained of magic, and the anti-magic globe collapses."));
            return;
        }
    }

    private static bool IsMagicalItemForProtectionFromMagic(Item item)
    {
        if (item == null)
            return false;

        return item.Type is ItemType.Potion or ItemType.Scroll or ItemType.MagicItem
               || item.MagicBonus != 0
               || item.ToHitBonus != 0
               || item.ArmorClassBonus != 0
               || (item.SpecialAbilities != null && item.SpecialAbilities.Count > 0)
               || item.IsCursed;
    }

    private static int GetProtectionFromMagicItemPower(Item item)
    {
        if (item == null)
            return 0;

        var specialCount = item.SpecialAbilities?.Count ?? 0;
        return Math.Abs(item.MagicBonus) * 100
               + Math.Abs(item.ToHitBonus) * 80
               + Math.Abs(item.ArmorClassBonus) * 80
               + specialCount * 25
               + Math.Max(0, item.Cost / 100);
    }

    private static void DrainItemMagic(Item item)
    {
        item.MagicBonus = 0;
        item.ToHitBonus = 0;
        item.ArmorClassBonus = 0;
        item.IsCursed = false;

        if (item.SpecialAbilities != null)
            item.SpecialAbilities.Clear();
    }

    private int RollAnimalControlCapacity(MonsterSize size)
    {
        return size switch
        {
            MonsterSize.Small => _dice.Roll(16) + 4,
            MonsterSize.Medium => _dice.Roll(10) + 2,
            _ => _dice.Roll(4)
        };
    }

    private static MonsterSize PickBestAnimalControlSize(IReadOnlyCollection<MonsterInstance> animals)
    {
        return animals
            .GroupBy(a => a.Template.Size)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key == MonsterSize.Small ? 0 : g.Key == MonsterSize.Medium ? 1 : 2)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static string SizeLabel(MonsterSize size)
    {
        return size switch
        {
            MonsterSize.Small => "small",
            MonsterSize.Medium => "man-sized",
            _ => "large"
        };
    }

    private static string ResolveAnimalControlCategory(int d20)
    {
        return d20 switch
        {
            <= 4 => "mammal/marsupial",
            <= 8 => "avian",
            <= 12 => "reptile/amphibian",
            <= 15 => "fish",
            <= 17 => "mammal/marsupial/avian",
            <= 19 => "reptile/amphibian/fish",
            _ => "all animal types"
        };
    }

    private static bool IsAnimalControlCategoryMatch(MonsterInstance monster, string category)
    {
        if (string.Equals(category, "all animal types", StringComparison.OrdinalIgnoreCase))
            return true;

        var kind = ResolveAnimalKind(monster);
        return category.Split('/').Any(part => string.Equals(part.Trim(), kind, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveAnimalKind(MonsterInstance monster)
    {
        var name = monster.Template.Name?.ToLowerInvariant() ?? string.Empty;
        var typeName = monster.Template.TypeName?.ToLowerInvariant() ?? string.Empty;
        var merged = $"{name} {typeName}";

        if (merged.Contains("bird") || merged.Contains("avian") || merged.Contains("eagle") || merged.Contains("hawk") || merged.Contains("owl") || merged.Contains("vulture") || merged.Contains("raven"))
            return "avian";

        if (merged.Contains("snake") || merged.Contains("lizard") || merged.Contains("reptile") || merged.Contains("amphib") || merged.Contains("frog") || merged.Contains("toad") || merged.Contains("turtle") || merged.Contains("crocod"))
            return "reptile";

        if (merged.Contains("fish") || merged.Contains("shark") || merged.Contains("eel") || merged.Contains("piranha") || merged.Contains("trout") || merged.Contains("salmon"))
            return "fish";

        return "mammal";
    }

    private static int GetHumanControlHitDiceCost(MonsterInstance target)
    {
        return Math.Max(1, target.Template.HitDice);
    }

    private static bool IsHumanControlCategoryMatch(MonsterInstance target, int d20)
    {
        var category = PotionOfHumanControl.GetControlCategory(d20);
        var name = target.Template.Name?.Trim() ?? string.Empty;
        var lowered = name.ToLowerInvariant();
        var isDwarf = lowered.Contains("dwarf");
        var isElf = lowered.Contains("elf");
        var isHalfElf = lowered.Contains("half-elf") || lowered.Contains("half elf");
        var isGnome = lowered.Contains("gnome");
        var isHalfling = lowered.Contains("halfling");
        var isHalfOrc = lowered.Contains("half-orc") || lowered.Contains("half orc");
        var isHuman = IsHumanControlHumanType(lowered);

        return category switch
        {
            "Dwarves" => isDwarf,
            "Elves/Half-Elves" => isElf || isHalfElf,
            "Gnomes" => isGnome,
            "Halflings" => isHalfling,
            "Half-Orcs" => isHalfOrc,
            "Humans" => isHuman,
            "Humanoids" => target.InstanceMonsterType == MonsterType.Humanoid && !isHuman && !isDwarf && !isElf && !isHalfElf && !isGnome && !isHalfling && !isHalfOrc,
            "Elves, Half-Elves, and Humans" => isElf || isHalfElf || isHuman,
            _ => false
        };
    }

    private static bool IsHumanControlHumanType(string loweredName)
    {
        if (string.IsNullOrWhiteSpace(loweredName))
            return false;

        if (loweredName.Contains("human")
            || loweredName.Contains("bandit")
            || loweredName.Contains("berserker")
            || loweredName.Contains("brigand")
            || loweredName.Contains("brigadd"))
        {
            return true;
        }

        if (loweredName.StartsWith("lvl ", StringComparison.OrdinalIgnoreCase))
        {
            return loweredName.Contains("fighter")
                   || loweredName.Contains("magic-user")
                   || loweredName.Contains("cleric")
                   || loweredName.Contains("illusionist")
                   || loweredName.Contains("thief")
                   || loweredName.Contains("assassin")
                   || loweredName.Contains("monk")
                   || loweredName.Contains("ranger")
                   || loweredName.Contains("paladin");
        }

        if (loweredName.Contains("master thief")
            || loweredName.Equals("thief", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("fighter", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("magic-user", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("illusionist", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("assassin", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("monk", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("paladin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void ResolveLayOnHands(CombatSession session, Character paladin, CombatAction action, List<CombatEvent> events)
    {
        if (paladin.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{paladin.Name} is transformed by lycanthropy and cannot use Lay on Hands."));
            return;
        }

        if (!paladin.IsPaladin())
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot use Lay on Hands."));
            return;
        }

        if (paladin.LayOnHandsUsedToday)
        {
            events.Add(new CombatEvent($"{paladin.Name} has already used Lay on Hands today."));
            return;
        }

        var targetName = action.Target?.CharacterName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            events.Add(new CombatEvent($"{paladin.Name} has no Lay on Hands target."));
            return;
        }

        var target = session.Party.FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            events.Add(new CombatEvent($"{paladin.Name}'s Lay on Hands target is no longer present."));
            return;
        }

        if (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost))
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot heal {target.Name} with Lay on Hands."));
            return;
        }

        var healAmount = Math.Max(0, paladin.GetPaladinLevel()) * 2;
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;

        paladin.LayOnHandsUsedToday = true;

        events.Add(new CombatEvent(healed > 0
            ? $"{paladin.Name} lays on hands and heals {target.Name} for {healed} hit point(s)."
            : $"{paladin.Name} lays on hands on {target.Name}, but no healing is needed."));
    }

    private void ResolveMonkBodyHeal(CombatSession session, Character monk, List<CombatEvent> events)
    {
        if (!monk.IsMonk() || monk.GetMonkLevel() < 7)
        {
            events.Add(new CombatEvent($"{monk.Name} cannot use Body Heal."));
            return;
        }

        if (monk.MonkBodyHealUsedToday)
        {
            events.Add(new CombatEvent($"{monk.Name} has already used Body Heal today."));
            return;
        }

        if (monk.HasStatus(CharacterStatus.Dead) || monk.HasStatus(CharacterStatus.Ashes) || monk.HasStatus(CharacterStatus.Lost))
        {
            events.Add(new CombatEvent($"{monk.Name} cannot use Body Heal right now."));
            return;
        }

        var healAmount = monk.RollMonkBodyHealAmount();
        var before = monk.CurrentHitPoints;
        monk.CurrentHitPoints = Math.Min(monk.MaxHitPoints, monk.CurrentHitPoints + healAmount);
        var healed = monk.CurrentHitPoints - before;

        monk.MonkBodyHealUsedToday = true;

        events.Add(new CombatEvent(healed > 0
            ? $"{monk.Name} heals own wounds for {healed} hit point(s)."
            : $"{monk.Name} focuses inner discipline, but no healing is needed."));
    }

    private void ResolveDispellUndead(CombatSession session, Character actor, CombatAction action, List<CombatEvent> events)
    {
        if (_dualClassService.IsTurnUndeadFromOriginalClass(actor))
            _dualClassService.MarkOriginalClassFunctionUsed(actor);

        var effectiveClericLevel = GetEffectiveTurnUndeadLevel(actor);
        if (effectiveClericLevel < 1)
        {
            events.Add(new CombatEvent($"{actor.Name} cannot dispell undead."));
            return;
        }

        var targetMonsters = string.IsNullOrWhiteSpace(action.TargetGroupId)
            ? session.AliveMonsters.ToList()
            : session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();

        var undead = targetMonsters.Where(m => m.InstanceMonsterType == Adnd.Core.Monsters.MonsterType.Undead).ToList();
        if (undead.Count == 0)
        {
            events.Add(new CombatEvent($"{actor.Name} presents a holy symbol, but no undead are affected."));
            return;
        }

        events.Add(new CombatEvent($"{actor.Name} uses Dispell Undead!"));

        var table = LoadTurnUndeadTable();
        var row = table.FirstOrDefault(r => effectiveClericLevel >= r.MinLevel && effectiveClericLevel <= r.MaxLevel)
                  ?? table.OrderByDescending(r => r.MaxLevel).First();

        foreach (var monster in undead)
        {
            var key = ResolveTurnUndeadKey(monster.Name);
            if (!row.Results.TryGetValue(key, out var token) || string.IsNullOrWhiteSpace(token))
                token = row.Results.TryGetValue("Skeleton", out var fallback) ? fallback : "-";

            token = token.Trim();

            if (token == "-")
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists the turning attempt."));
                continue;
            }

            if (string.Equals(token, "D", StringComparison.OrdinalIgnoreCase))
            {
                monster.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{monster.DisplayName} is disintegrated by holy power!"));
                continue;
            }

            if (string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned for {rounds} round(s)!"));
                continue;
            }

            if (!int.TryParse(token, out var required))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is unaffected."));
                continue;
            }

            var roll = _dice.Roll(20);
            if (roll >= required)
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned ({roll} vs {required}) for {rounds} round(s)!"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists turning ({roll} vs {required})."));
            }
        }
    }

    private static int GetEffectiveTurnUndeadLevel(Character actor)
    {
        var clericLevel = actor.Classes.Contains(CharacterClass.Cleric)
            ? actor.GetClassLevel(CharacterClass.Cleric)
            : 0;

        var paladinLevel = actor.Classes.Contains(CharacterClass.Paladin)
            ? Math.Max(0, actor.GetClassLevel(CharacterClass.Paladin) - 2)
            : 0;

        if (!actor.Classes.Contains(CharacterClass.Cleric) && paladinLevel < 1)
            return 0;

        if (actor.Classes.Contains(CharacterClass.Paladin) && actor.GetClassLevel(CharacterClass.Paladin) < 3)
            paladinLevel = 0;

        return Math.Max(clericLevel, paladinLevel);
    }

    private static string ResolveTurnUndeadKey(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return "Skeleton";

        var n = monsterName.Trim().ToLowerInvariant();
        if (n.Contains("skeleton")) return "Skeleton";
        if (n.Contains("zombie")) return "Zombie";
        if (n.Contains("ghoul")) return "Ghoul";
        if (n.Contains("shadow")) return "Shadow";
        if (n.Contains("wight")) return "Wight";
        if (n.Contains("wraith")) return "Wraith";
        if (n.Contains("mummy")) return "Mummy";
        if (n.Contains("spectre") || n.Contains("specter")) return "Spectre";
        if (n.Contains("vampire")) return "Vampire";
        if (n.Contains("ghost")) return "Ghost";
        if (n.Contains("lich")) return "Lich";
        return "Skeleton";
    }

    private static List<TurnUndeadRow> LoadTurnUndeadTable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Characters", "Progression", "ClericTurnUndead.json");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Adnd.Core", "Characters", "Progression", "ClericTurnUndead.json"));

        if (!File.Exists(path))
            return new List<TurnUndeadRow>
            {
                new() { MinLevel = 1, MaxLevel = 2, Results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Skeleton"] = "10" } }
            };

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("TurnUndeadTable", out var tableEl) || tableEl.ValueKind != JsonValueKind.Array)
            return new List<TurnUndeadRow>();

        var rows = new List<TurnUndeadRow>();
        foreach (var rowEl in tableEl.EnumerateArray())
        {
            var range = rowEl.GetProperty("LevelRange").GetString() ?? "1-1";
            var parts = range.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var min = 1;
            var max = 1;
            if (parts.Length >= 1) int.TryParse(parts[0], out min);
            if (parts.Length >= 2) int.TryParse(parts[1], out max);
            else max = min;

            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rowEl.TryGetProperty("Results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in resultsEl.EnumerateObject())
                    results[p.Name] = p.Value.GetString() ?? "-";
            }

            rows.Add(new TurnUndeadRow { MinLevel = min, MaxLevel = max, Results = results });
        }

        return rows;
    }

    private sealed class TurnUndeadRow
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public Dictionary<string, string> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    private bool TryResolveLycanthropeCharacterAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        if (!member.HasStatus(CharacterStatus.LycanthropeTransformed))
            return false;

        MonsterInstance? target = null;

        if (!string.IsNullOrEmpty(action.TargetMonsterId))
        {
            var named = session.FindMonster(action.TargetMonsterId);
            if (named != null && named.IsAlive)
                target = named;
        }

        if (target is null && action.SpreadTargets)
        {
            var spreadable = (string.IsNullOrEmpty(action.TargetGroupId)
                    ? session.AliveMonsters
                    : session.GetAliveMonstersByGroup(action.TargetGroupId))
                .ToList();

            if (spreadable.Count > 0)
            {
                var at = ((session.SpreadCursor % spreadable.Count) + spreadable.Count) % spreadable.Count;
                target = spreadable[at];
                session.SpreadCursor = at + 1;
            }
        }

        if (target is null && !string.IsNullOrEmpty(action.TargetGroupId))
        {
            var groupMonsters = session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();
            if (groupMonsters.Count > 0)
                target = groupMonsters.First();
        }

        target ??= session.AliveMonsters.FirstOrDefault();
        if (target is null)
            return true;

        if (IsLycanthropeMonster(target))
        {
            events.Add(new CombatEvent($"{member.Name} is a lycanthrope and refuses to attack {target.DisplayName}."));
            return true;
        }

        var thac0Modifier = (session.IsBlessed(member.Name) ? 1 : 0) + ((session.IsChantActive || session.IsPrayerActive) ? 1 : 0);
        var attacks = 3;

        for (var attackIndex = 0; attackIndex < attacks; attackIndex++)
        {
            if (!target.IsAlive)
                break;

            var roll = _dice.Roll(20);
            var needed = (member.Thac0 - thac0Modifier) - target.ArmorClass;
            if (roll == 1 || (roll != 20 && roll < needed))
            {
                var attackNameMiss = attackIndex < 2 ? "claw" : "bite";
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName} with {attackNameMiss}."));
                continue;
            }

            var damageExpression = attackIndex < 2 ? "1d4" : "2d5";
            var before = target.CurrentHitPoints;
            var damage = RollDamage(damageExpression);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            var attackName = attackIndex < 2 ? "claw" : "bite";
            events.Add(new CombatEvent($"{member.Name} hits {target.DisplayName} with {attackName} ({damageExpression}) for {damage} damage. HP {before}->{target.CurrentHitPoints}."));
            WakeMonsterIfAsleepAfterDamage(target, actual, events);

            if (target.CurrentHitPoints <= 0)
            {
                events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                break;
            }
        }

        return true;
    }

    private static bool IsLycanthropeMonster(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        var name = monster.Template.Name ?? string.Empty;
        var typeName = monster.Template.TypeName ?? string.Empty;
        return name.Contains("Lycanthrope", StringComparison.OrdinalIgnoreCase)
               || typeName.Contains("Lycanthrope", StringComparison.OrdinalIgnoreCase)
               || HasAnySpecialAbility(monster, "Lycanthropy");
    }

    private void ResolvePartyAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        session.SwallowedCharacters.TryGetValue(member.Name, out var swallowed);
        if (swallowed != null)
        {
            if (!member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var cuttingWeapon)
                || cuttingWeapon?.Type != ItemType.Weapon
                || !(string.Equals(cuttingWeapon.DamageType, "Slashing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cuttingWeapon.DamageType, "Piercing", StringComparison.OrdinalIgnoreCase)))
            {
                LogPurpleWormRule(events, $"{member.Name} is swallowed and needs an equipped slashing or piercing weapon to cut the worm from inside.");
                return;
            }
            LogPurpleWormRule(events, $"{member.Name} cuts at {swallowed.Worm.DisplayName} from inside: AC 9; -{swallowed.RoundsInside(session.RoundNumber)} damage on each attack this round.");
        }
        var chantOrPrayerBonus = (session.IsChantActive || session.IsPrayerActive) ? 1 : 0;

        if (member.BreakRingInvisibilityOnHostileAction())
            events.Add(new CombatEvent($"{member.Name}'s ring invisibility breaks on attack."));

        if (swallowed == null && member.HasStatus(CharacterStatus.LycanthropeTransformed) && TryResolveLycanthropeCharacterAttack(session, member, action, events))
            return;

        var rangedWeapon = member.Equipment.TryGetValue(EquipmentSlot.Range, out var rw) ? rw : null;
        var ammo = member.Equipment.TryGetValue(EquipmentSlot.Ammo, out var am) ? am : null;
        var fightingRank = CombatFormation.GetFightingRank(session.Party, member, session);
        var isFrontRankPosition = fightingRank is >= 1 and <= 3;
        var useRanged = swallowed == null && rangedWeapon != null
                        && !isFrontRankPosition
                        && (!string.IsNullOrWhiteSpace(rangedWeapon.Range)
                            || !string.IsNullOrWhiteSpace(rangedWeapon.FireRate)
                            || rangedWeapon.RequiresAmmo
                            || !string.IsNullOrWhiteSpace(rangedWeapon.AmmoType));

        if (useRanged && rangedWeapon != null)
        {
            if (IsJavelinOfLightning(rangedWeapon) && rangedWeapon.Quantity <= 0)
            {
                if (member.Equipment.TryGetValue(EquipmentSlot.Range, out var equippedRange)
                    && ReferenceEquals(equippedRange, rangedWeapon))
                {
                    member.Equipment[EquipmentSlot.Range] = null;
                }

                member.Inventory.RemoveAll(item => IsJavelinOfLightning(item) && item.Quantity <= 0);
                events.Add(new CombatEvent($"{member.Name}'s Javelin of Lightning is depleted."));
                return;
            }

            if (IsHammerOfThunderbolts(rangedWeapon) && session.RoundNumber % 2 != 0)
            {
                events.Add(new CombatEvent($"{member.Name} steadies {rangedWeapon.Name} and cannot hurl it this round."));
                return;
            }

            if (string.Equals((rangedWeapon.FireRate ?? string.Empty).Trim(), "1/2", StringComparison.OrdinalIgnoreCase)
                && session.RoundNumber % 2 != 0)
            {
                events.Add(new CombatEvent($"{member.Name} reloads {rangedWeapon.Name} and cannot fire this round."));
                return;
            }

            if (rangedWeapon.RequiresAmmo)
            {
                if (ammo == null || ammo.Quantity <= 0)
                {
                    events.Add(new CombatEvent($"{member.Name} has no ammo for {rangedWeapon.Name}."));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(rangedWeapon.AmmoType)
                    && !string.Equals(rangedWeapon.AmmoType, ammo.AmmoType, StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new CombatEvent($"{member.Name} has incompatible ammo for {rangedWeapon.Name}."));
                    return;
                }
            }
        }

        var hasImprovedInvisibility = session.GetImprovedInvisibilityRounds(member.Name) > 0;
        if (!hasImprovedInvisibility
            && member.HasStatus(CharacterStatus.Invisible)
            && session.InvisiblyBuffedPartyMembers.Contains(member.Name))
        {
            member.RemoveStatus(CharacterStatus.Invisible);
            member.ArmorClass += 4;
            session.InvisiblyBuffedPartyMembers.Remove(member.Name);
            events.Add(new CombatEvent($"{member.Name} attacks and becomes visible."));
        }

        MonsterInstance? target = swallowed?.Worm;

        if (swallowed == null && !string.IsNullOrEmpty(action.TargetMonsterId))
        {
            var named = session.FindMonster(action.TargetMonsterId);
            if (named != null && named.IsAlive) target = named;
        }

        if (target is null && action.SpreadTargets)
        {
            var spreadable = (string.IsNullOrEmpty(action.TargetGroupId)
                    ? session.AliveMonsters
                    : session.GetAliveMonstersByGroup(action.TargetGroupId))
                .ToList();

            if (spreadable.Count > 0)
            {
                var at = ((session.SpreadCursor % spreadable.Count) + spreadable.Count) % spreadable.Count;
                target = spreadable[at];
                session.SpreadCursor = at + 1;
            }
        }

        if (target is null && !string.IsNullOrEmpty(action.TargetGroupId))
        {
            var groupMonsters = session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();
            if (groupMonsters.Count > 0)
                target = groupMonsters.First();
        }

        target ??= session.AliveMonsters.FirstOrDefault();
        if (target is null)
            return;

        int attacks = useRanged && rangedWeapon != null
            ? GetRangedAttacksPerRound(rangedWeapon.FireRate, session.RoundNumber)
            : GetAttacksThisRound(member.NumberOfAttacks, session.RoundNumber);

        if (session.RoundNumber == 1
            && session.PartySurprisedRound1
            && useRanged
            && IsCrossbowOfSpeed(rangedWeapon))
        {
            attacks = Math.Min(attacks, 1);
        }

        if (session.IsHasted(member.Name))
            attacks *= 2;
        if (session.IsPartySlowed(member.Name))
            attacks = Math.Max(1, attacks / 2);

        var targetArmorClass = swallowed == null ? target.ArmorClass : 9;
        var isBackstab = swallowed == null && IsThiefBackstabAttack(member, session);
        if (isBackstab && IsPurpleWorm(target))
        {
            if (!session.RearAttackers.TryGetValue(target, out var rearAttackers))
                session.RearAttackers[target] = rearAttackers = new HashSet<Character>();
            rearAttackers.Add(member);
        }
        var backstabMultiplier = isBackstab ? GetThiefBackstabMultiplierByLevel(member.GetClassLevel(CharacterClass.Thief)) : 1;

        if (isBackstab && _dualClassService.IsThiefBackstabFromOriginalClass(member))
            _dualClassService.MarkOriginalClassFunctionUsed(member);

        if (member.Class == CharacterClass.Assassin
            && session.RoundNumber == 1
            && session.MonstersSurprisedRound1
            && target.IsAlive)
        {
            if (_dualClassService.IsAssassinationFromOriginalClass(member))
                _dualClassService.MarkOriginalClassFunctionUsed(member);

            var assassination = new AssassinationService("Data/Assassination");
            var assassinLevel = Math.Max(1, member.GetClassLevel(CharacterClass.Assassin));
            int monsterLevel = target.Template.HitDice;
            var assassinationRoll = assassination.RollAssassination(assassinLevel, monsterLevel, _rng);

            RuleApplicationInfo.Publish(
                "DMG",
                "75Assassin",
                $"{member.Name} assassination attempt vs {target.DisplayName}",
                "Use assassin level vs victim level/hit dice table; roll 1d100 and succeed if roll is less than or equal to chance.",
                "1",
                "100",
                assassinationRoll.Roll.ToString(),
                $"Assassin level {assassinLevel}, target HD {monsterLevel}, chance {assassinationRoll.ChancePercent}% => {(assassinationRoll.Success ? "success" : "failure")}.");

            if (assassinationRoll.Success)
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{member.Name} assassinates {target.DisplayName} instantly!"));
                return;
            }
        }

        for (int i = 0; i < attacks; i++)
        {
            if (useRanged
                && (!member.Equipment.TryGetValue(EquipmentSlot.Range, out var currentRangedWeapon)
                    || !ReferenceEquals(currentRangedWeapon, rangedWeapon)))
            {
                useRanged = false;
                rangedWeapon = null;
            }

            var thac0Modifier = session.IsBlessed(member.Name) ? 1 : 0;
            thac0Modifier += chantOrPrayerBonus;
            if (isBackstab)
                thac0Modifier += 4;

            var weaponMagicToHitBonus = 0;

            Item? mainHand = null;
            if (member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) && equipped != null)
            {
                mainHand = equipped;
                weaponMagicToHitBonus = Math.Max(0, mainHand.ToHitBonus);
                thac0Modifier += weaponMagicToHitBonus;

                var dancingSwordToHitBonus = GetSwordOfDancingCurrentBonus(mainHand, session);
                if (dancingSwordToHitBonus > 0)
                    thac0Modifier += dancingSwordToHitBonus - Math.Max(0, mainHand.ToHitBonus);

                if (IsSwordPlusFourDefender(mainHand))
                {
                    var defenderAttackBonus = GetDefenderAttackBonusAllocation(mainHand);
                    var redirectedToDefense = Math.Max(0, 4 - defenderAttackBonus);
                    if (redirectedToDefense > 0)
                        thac0Modifier -= redirectedToDefense;
                }

                if (IsSwordPlusFiveHolyAvenger(mainHand) && !member.IsPaladin())
                    thac0Modifier -= 3;
            }

            if (useRanged && rangedWeapon != null)
            {
                mainHand = rangedWeapon;
                weaponMagicToHitBonus = Math.Max(0, rangedWeapon.ToHitBonus);
                thac0Modifier += weaponMagicToHitBonus;

                if (CanUseDwarvenThrowerFullPower(member, rangedWeapon))
                    thac0Modifier += 1;

                thac0Modifier += GetHammerOfThunderboltsToHitBonus(member, rangedWeapon);
            }

            var strengthToHitBonus = useRanged ? 0 : member.Thac0StrengthModifier;
            thac0Modifier += strengthToHitBonus;

            var nauseaPenaltyApplied = 0;
            if (HasAnySpecialAbility(target, "Nausea") && session.HasPartyNauseaAgainstGroup(member.Name, target.GroupId))
            {
                nauseaPenaltyApplied = 2;
                thac0Modifier -= nauseaPenaltyApplied;
                events.Add(new CombatEvent($"{member.Name} suffers -2 to hit {target.DisplayName} due to carrion stench nausea (group {target.GroupId}); the penalty lasts until battle ends."));
            }

            var dexPenaltyApplied = 0;
            if (useRanged
                && member.Abilities.Dexterity < 13
                && HasSpecialDefense(target, "Dexterity Penalty"))
            {
                dexPenaltyApplied = 3;
                thac0Modifier -= dexPenaltyApplied;
                RuleApplicationInfo.PublishLinked(
                    "MM",
                    "Giant Bat",
                    $"Dexterity Penalty: {member.Name} has Dexterity {member.Abilities.Dexterity} (<13), so ranged attacks vs {target.DisplayName} are at -3 to hit.");
            }

            var nonProficiencyPenalty = WeaponProficiencyRules.GetNonProficiencyPenalty(member, mainHand, useRanged);
            if (nonProficiencyPenalty > 0)
            {
                var neededBeforeNonProficiencyPenalty = (member.Thac0 - thac0Modifier) - target.ArmorClass;
                thac0Modifier -= nonProficiencyPenalty;
                var neededAfterNonProficiencyPenalty = (member.Thac0 - thac0Modifier) - target.ArmorClass;
                var profWeaponType = WeaponProficiencyRules.ResolveWeaponProficiencyKey(mainHand, useRanged, member.IsMonk());
                var profWeaponName = mainHand?.Name ?? (member.IsMonk() ? "Fist or Open Hand" : "Unarmed");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "37WeaponProficiency",
                    $"Weapon proficiency: {member.Name} attacks with {profWeaponName} ({profWeaponType}). Not proficient => to-hit penalty {nonProficiencyPenalty}. Needed roll changes {neededBeforeNonProficiencyPenalty} -> {neededAfterNonProficiencyPenalty}.");
            }

            var swordSituationalBonus = GetSituationalSwordBonusAgainstTarget(mainHand, target);
            if (swordSituationalBonus > 0 && mainHand != null)
            {
                thac0Modifier += swordSituationalBonus;
                var totalSwordBonus = weaponMagicToHitBonus + swordSituationalBonus;
                var bonusMessage = $"{member.Name} attacks {target.DisplayName} with {mainHand.Name}: +{totalSwordBonus} to hit and +{totalSwordBonus} damage against this creature (replaces normal +{weaponMagicToHitBonus}).";
                events.Add(new CombatEvent(bonusMessage));
                RuleApplicationInfo.Publish(bonusMessage);
            }

            var weaponVsAcKey = ResolveWeaponVsArmorTableKey(mainHand, useRanged);
            var weaponVsAcAdjustment = swallowed == null ? GetWeaponVsArmorClassAdjustment(mainHand, targetArmorClass, useRanged) : 0;
            if (!useRanged && mainHand == null && member.IsMonk())
            {
                weaponVsAcKey = null;
                weaponVsAcAdjustment = 0;
            }

            // Table values are expressed as weapon-vs-armor adjustments.
            // For needed-roll clarity: a negative table value increases needed roll.
            var neededAdjustmentFromWeaponVsAc = -weaponVsAcAdjustment;
            thac0Modifier -= neededAdjustmentFromWeaponVsAc;

            var incapacitatedMonsterReason = GetIncapacitatedTargetReason(target);
            if (!string.IsNullOrWhiteSpace(incapacitatedMonsterReason))
            {
                var incapacitatedToHitAdjustment = string.Equals(incapacitatedMonsterReason, "asleep", StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : 4;
                thac0Modifier += incapacitatedToHitAdjustment;
                var weaponType = !string.IsNullOrWhiteSpace(weaponVsAcKey) ? weaponVsAcKey : (mainHand?.Name ?? "fist or open hand");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "38WeaponTypeToHitAdjustment",
                    $"Weapon type to-hit adjustment: {member.Name} attacks {target.DisplayName}. Weapon type: {weaponType}. Adjustment: +{incapacitatedToHitAdjustment} because target is {incapacitatedMonsterReason}.");

                if (string.Equals(incapacitatedMonsterReason, "asleep", StringComparison.OrdinalIgnoreCase))
                    events.Add(new CombatEvent($"{member.Name} gains +3 to hit because {target.DisplayName} is asleep."));
            }

            if (target.ArmorClass >= 2 && target.ArmorClass <= 10)
            {
                var mode = useRanged ? "ranged" : "melee";
                var weaponNameForAdjustment = mainHand?.Name ?? "Fist or Open Hand";
                if (!string.IsNullOrWhiteSpace(weaponVsAcKey))
                {
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment ({mode}): {member.Name} uses {weaponNameForAdjustment}. Weapon type: {weaponVsAcKey}. Needed-roll adjustment vs AC {target.ArmorClass}: {neededAdjustmentFromWeaponVsAc:+#;-#;0}.");
                }
                else
                {
                    if (!useRanged && mainHand == null && member.IsMonk())
                    {
                        RuleApplicationInfo.PublishLinked(
                            "PHB",
                            "38WeaponTypeToHitAdjustment",
                            "Weapon vs AC adjustment is not applicable for kombinationen monks och Fist or Open Hand.");
                    }
                    else
                    {
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment ({mode}): {member.Name} uses {weaponNameForAdjustment}. Weapon type: unknown (no table row matched). Adjustment vs AC {target.ArmorClass}: 0.");
                    }
                }
            }

            int needed = (member.Thac0 - thac0Modifier) - targetArmorClass;
            var neededFromThac0AndAcOnly = member.Thac0 - targetArmorClass;
            int roll = _dice.Roll(20);
            var wasNaturalTwenty = roll == 20;

            if (TryResolveBlinkDefenseAgainstAttack(target, events))
            {
                events.Add(new CombatEvent($"{member.Name}'s attack misses {target.DisplayName} due to blink displacement."));
                continue;
            }

            if (GameRulesProvider.Current.ShowToHitRoll)
            {
                var showReferences = GameRulesProvider.Current.ShowReferencesInRuleAndDiceInfo;
                var weaponNameForToHit = mainHand?.Name ?? (useRanged ? "Ranged attack" : "Fist or Open Hand");
                var weaponAdjText = neededAdjustmentFromWeaponVsAc == 0
                    ? string.Empty
                    : $" Weapon vs AC adj ({weaponNameForToHit}): {neededAdjustmentFromWeaponVsAc}.";
                var dexPenaltyText = dexPenaltyApplied > 0
                    ? $" Dex penalty ({target.DisplayName}): -{dexPenaltyApplied}."
                    : string.Empty;
                var nauseaPenaltyText = nauseaPenaltyApplied > 0
                    ? $" Nausea penalty ({target.DisplayName}): -2."
                    : string.Empty;
                var nonProfText = nonProficiencyPenalty > 0
                    ? $" Non-proficiency penalty: {nonProficiencyPenalty}."
                    : string.Empty;
                var strengthNeededAdjustment = -strengthToHitBonus;
                var weaponMagicNeededAdjustment = -weaponMagicToHitBonus;
                var swordSituationalNeededAdjustment = -swordSituationalBonus;
                var baseBreakdown = showReferences
                    ? $"{neededFromThac0AndAcOnly}(base)"
                    : $"{neededFromThac0AndAcOnly}";
                var neededBreakdownText =
                    $" {baseBreakdown}" +
                    $"{(neededAdjustmentFromWeaponVsAc >= 0 ? "+" : string.Empty)}{neededAdjustmentFromWeaponVsAc}(ac adj)" +
                    $"{(strengthNeededAdjustment >= 0 ? "+" : string.Empty)}{strengthNeededAdjustment}(str adj)" +
                    $"{(weaponMagicNeededAdjustment >= 0 ? "+" : string.Empty)}{weaponMagicNeededAdjustment}(magic weapon)" +
                    $"{(swordSituationalNeededAdjustment >= 0 ? "+" : string.Empty)}{swordSituationalNeededAdjustment}(special weapon)" +
                    $"{(nonProficiencyPenalty >= 0 ? "+" : string.Empty)}{nonProficiencyPenalty}(non-prof)" +
                    $"{(nauseaPenaltyApplied > 0 ? $"+{nauseaPenaltyApplied}(nausea)" : string.Empty)}={needed}";

                var toHitMessage = $"TO-HIT: {member.Name} THAC0 {member.Thac0},{weaponAdjText}{nonProfText}{nauseaPenaltyText} {target.DisplayName} AC {targetArmorClass}, needs {needed}{neededBreakdownText} on 1d20, rolled {roll}.";
                events.Add(new CombatEvent(toHitMessage));
                RuleApplicationInfo.Publish(toHitMessage);

                if (showReferences)
                {
                    RuleApplicationInfo.PublishLinked(
                        "DMG",
                        "74ToHitTables",
                        $"{member.Name} THAC0 {member.Thac0} from class:{member.Class.ToDisplayString()}/level:{member.GetClassLevel(member.Class)}");

                    RuleApplicationInfo.PublishLinked(
                        "MM",
                        target.Template.Name ?? target.DisplayName,
                        $"{target.DisplayName} AC:{targetArmorClass}{(swallowed != null ? " (stomach interior)" : string.Empty)}.");

                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment: {weaponNameForToHit} vs AC {targetArmorClass} => {neededAdjustmentFromWeaponVsAc:+#;-#;0}.");

                    var strengthLabel = member.Abilities.Strength == 18 && member.ExceptionalStrengthPercentile.HasValue
                        ? $"18/{member.ExceptionalStrengthPercentile.Value:00}"
                        : member.Abilities.Strength.ToString();
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "9StrengthModifyers",
                        $"Strength {strengthLabel} gives {strengthToHitBonus:+#;-#;0} to hit bonus according to PHB 8, Ability Score Modifiers.");

                    RuleApplicationInfo.PublishLinked(
                        "DMG",
                        "165MagicWeapons",
                        $"Magic weapon bonus from {weaponNameForToHit}: {weaponMagicToHitBonus:+#;-#;0} to hit.");

                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "37WeaponProficiency",
                        $"Weapon proficiency adjustment for {weaponNameForToHit}: {nonProficiencyPenalty:+#;-#;0} (penalty)." );
                }
            }

            if (roll < needed)
            {
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName}."));
                continue;
            }

            if (HasOnlyAffectedByFireBasedAttacksDefense(target) && !IsFireBasedWeaponAttack(mainHand))
            {
                var blockedWeaponName = mainHand?.Name ?? "bare hands";
                events.Add(new CombatEvent($"{member.Name} hits {target.DisplayName} with {blockedWeaponName}, but it is affected only by fire-based attacks."));
                RuleApplicationInfo.Publish(
                    "AD&D",
                    "Affected only by fire based attacks",
                    $"{target.DisplayName} ignores non-fire attack",
                    "Creatures with this defense can only be harmed by fire-based attacks.",
                    null,
                    null,
                    null,
                    $"{member.Name}'s attack with {blockedWeaponName} deals no damage.");
                continue;
            }

            var mirrorImages = session.GetMonsterMirrorImageCount(target);
            if (mirrorImages > 0)
            {
                var imageHitRoll = _dice.Roll(mirrorImages + 1);
                if (imageHitRoll > 1)
                {
                    var remainingImages = session.RemoveOneMonsterMirrorImage(target);
                    events.Add(new CombatEvent($"{member.Name} hits a mirror image of {target.DisplayName}!"));
                    events.Add(new CombatEvent($"{target.DisplayName} has {remainingImages} mirror image(s) remaining."));
                    if (remainingImages <= 0)
                        events.Add(new CombatEvent($"{target.DisplayName} has no mirror images left."));
                    continue;
                }
            }

            if (wasNaturalTwenty
                && IsSwordPlusTwoNineLivesStealer(mainHand)
                && TryResolveNineLivesStealerEffect(mainHand, target, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;

                continue;
            }

            if (wasNaturalTwenty
                && IsSwordOfLifeStealing(mainHand))
            {
                TryResolveSwordOfLifeStealingEffect(member, target, events);
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (wasNaturalTwenty
                && IsDaggerOfVenom(mainHand))
            {
                TryResolveDaggerOfVenomEffect(mainHand, target, events);
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveMaceOfDisruptionEffect(member, mainHand, target, useRanged, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveSwordOfSharpnessSeverEffect(mainHand, target, roll, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveSwordVorpalSeverEffect(mainHand, target, roll, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            TryApplyHitOnMonsterDestroysMetal(member, target, mainHand, events);

            if (!CanWeaponHarmTargetByMagicRequirement(mainHand, target))
            {
                var blockedWeaponName = mainHand != null ? mainHand.Name : "bare hands";
                events.Add(new CombatEvent(
                    $"{member.Name} hits {target.DisplayName} with {blockedWeaponName}, but the attack cannot harm it (insufficient magical weapon bonus)."));

                RuleApplicationInfo.Publish(
                    $"AD&D special defense: {target.DisplayName} requires a magical weapon bonus to hit. " +
                    $"{member.Name}'s attack with {blockedWeaponName} does not meet the required bonus, so it deals no damage.");
                continue;
            }

            if (TryResolveMummyImprovisedWeaponDamage(session, member, target, mainHand, events))
                break;

            var damageExpression = ResolveWeaponDamageExpression(member, mainHand, target);
            var strengthDamageBonus = mainHand != null && mainHand.Type == ItemType.Weapon
                ? AbilitiesTables.StrengthDamageModifier(member.Abilities.Strength, member.ExceptionalStrengthPercentile)
                : 0;

            int damage = RollDamage(damageExpression) + strengthDamageBonus;
            if (IsSwordPlusFiveHolyAvenger(mainHand) && !member.IsPaladin())
                damage = Math.Max(1, damage - 3);

            var dancingSwordBonus = GetSwordOfDancingCurrentBonus(mainHand, session);
            if (dancingSwordBonus > 1)
                damage += dancingSwordBonus - 1;

            if (IsSwordPlusFiveHolyAvenger(mainHand)
                && member.IsPaladin()
                && IsTargetChaoticEvil(target))
            {
                damage += 10;
            }

            if (swordSituationalBonus > 0)
                damage += swordSituationalBonus;
            var partyDamageBonusApplied = chantOrPrayerBonus > 0;
            if (partyDamageBonusApplied)
                damage += 1;

            if (IsSwordPlusTwoGiantSlayer(mainHand) && IsTargetTrueGiantForGiantSlayer(target))
                damage *= 2;

            var dragonSlayerDamageMultiplier = GetDragonSlayerDamageMultiplier(mainHand, target);
            if (dragonSlayerDamageMultiplier > 1)
                damage *= dragonSlayerDamageMultiplier;

            var maceOfDisruptionMultiplier = GetMaceOfDisruptionDamageMultiplier(mainHand, target);
            if (maceOfDisruptionMultiplier > 1)
                damage *= maceOfDisruptionMultiplier;

            var coldIronVulnerabilityMultiplier = GetColdIronVulnerabilityDamageMultiplier(mainHand, target);
            if (coldIronVulnerabilityMultiplier > 1)
            {
                var beforeColdIron = damage;
                damage *= coldIronVulnerabilityMultiplier;
                var coldIronWeaponName = mainHand?.Name ?? "attack";
                events.Add(new CombatEvent($"{target.DisplayName} is vulnerable to cold iron: {coldIronWeaponName} damage doubled ({beforeColdIron}->{damage})."));
            }

            if (useRanged && CanUseDwarvenThrowerFullPower(member, mainHand))
                damage += 1;

            var hammerOfThunderboltsPowerBonus = GetHammerOfThunderboltsDamageBonus(member, mainHand, useRanged);
            if (hammerOfThunderboltsPowerBonus > 0)
                damage += hammerOfThunderboltsPowerBonus;

            var dwarvenThrowerDamageMultiplier = GetDwarvenThrowerDamageMultiplier(member, mainHand, target, useRanged);
            if (dwarvenThrowerDamageMultiplier > 1)
                damage *= dwarvenThrowerDamageMultiplier;

            var thunderboltsDamageDiceMultiplier = GetHammerOfThunderboltsDamageDiceMultiplier(member, mainHand);
            if (thunderboltsDamageDiceMultiplier > 1)
                damage *= thunderboltsDamageDiceMultiplier;

            var monkWeaponDamageBonus = 0;
            if (member.IsMonk() && mainHand?.Type == ItemType.Weapon)
                monkWeaponDamageBonus = Math.Max(0, member.GetMonkLevel() / 2);

            if (CanWieldHammerOfThunderboltsAsPlusFive(member, mainHand)
                && IsTargetGiantOrKinForHammerOfThunderbolts(target))
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{member.Name}'s Hammer of Thunderbolts slays {target.DisplayName} outright!"));
                continue;
            }

            if (isBackstab)
            {
                var beforeBackstab = damage;
                damage *= backstabMultiplier;
                events.Add(new CombatEvent($"{member.Name} backstabs! +4 to hit, damage x{backstabMultiplier} ({beforeBackstab}->{damage})."));
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "27Backstab",
                    $"{member.Name} uses backstab from surprise/behind: +4 to hit, damage multiplier x{backstabMultiplier} ({beforeBackstab}->{damage}).");
            }

            if (monkWeaponDamageBonus > 0)
            {
                damage += monkWeaponDamageBonus;
                var monkWeaponName = mainHand?.Name ?? "weapon";
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "30Monk",
                    $"Monk weapon damage bonus: {member.Name} L{member.GetMonkLevel()} uses {monkWeaponName}. Bonus +{monkWeaponDamageBonus} damage (level/2, rounded down)."
                );
            }

            if (IsHalfDamageFromSharpWeapons(target, mainHand))
            {
                var originalDamage = damage;
                damage = Math.Max(1, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName} has Half Damage from Sharp Weapons. Damage reduced from {originalDamage} to {damage}."));
            }

            if (HasMagicWeaponsOnlyHalfDamageDefense(target))
            {
                var originalDamage = damage;
                damage = Math.Max(0, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName}'s mummy defenses halve magical weapon damage ({originalDamage}->{damage}, fractions dropped)."));
            }

            if (swallowed != null)
            {
                var penalty = swallowed.RoundsInside(session.RoundNumber);
                var beforePenalty = damage;
                damage = Math.Max(0, damage - penalty);
                LogPurpleWormRule(events, $"{member.Name}'s stomach attack damage: {beforePenalty} - {penalty} (round {penalty} inside) = {damage}.");
            }
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actualDamageToTarget = before - target.CurrentHitPoints;

            TryRevealWillOWispLairTreasureOnLowHp(target, events);

            if (actualDamageToTarget > 0 && IsSwordOfWounding(mainHand))
            {
                session.AddMonsterWoundingWound(target, rounds: 10);
                session.AddMonsterWoundingUnhealableDamage(target, actualDamageToTarget);
                events.Add(new CombatEvent($"Sword of Wounding opens a lingering wound on {target.DisplayName} (10 rounds)."));
            }

            WakeMonsterIfAsleepAfterDamage(target, actualDamageToTarget, events);

            if (useRanged && mainHand != null)
                ResolveJavelinOfLightningStrike(member, mainHand, target, session, events);

            if (useRanged && rangedWeapon != null && rangedWeapon.RequiresAmmo && ammo != null && ammo.Quantity > 0)
            {
                ammo.Quantity = Math.Max(0, ammo.Quantity - 1);
                events.Add(new CombatEvent($"{member.Name} uses 1 {ammo.Name}. {ammo.Quantity} remaining."));
            }

            if (useRanged && IsHammerOfThunderbolts(mainHand))
            {
                var restTriggered = session.RegisterPartyThunderboltsThrow(member.Name, session.RoundNumber);
                if (restTriggered)
                    events.Add(new CombatEvent($"{member.Name} has overtaxed the Hammer of Thunderbolts and must rest for 10 rounds."));

                events.Add(new CombatEvent($"The hurled Hammer of Thunderbolts crashes like thunder! Nearby creatures are stunned for 1 round."));
                foreach (var monster in session.AliveMonsters.Where(m => !ReferenceEquals(m, target)).ToList())
                    monster.SetStatus(MonsterStatus.Stunned, 1);
            }

            var weaponName = mainHand != null ? mainHand.Name : "bare hands";
            var damageFormula = strengthDamageBonus == 0
                ? damageExpression
                : $"{damageExpression}+{strengthDamageBonus}";
            if (monkWeaponDamageBonus > 0)
                damageFormula += $"+{monkWeaponDamageBonus}(monk)";

            var partyDamageModText = partyDamageBonusApplied ? " +1 chant/prayer" : string.Empty;
            events.Add(new CombatEvent(
                $"{member.Name} hits {target.DisplayName} with {weaponName} ({damageFormula}{partyDamageModText}) for {damage}  damage. HP {before}->{target.CurrentHitPoints}."));

            var didSplitFromSlashing = actualDamageToTarget > 0
                && IsSlashingAttack(mainHand)
                && MonsterSplitResolver.TryResolveSplit(
                    session,
                    target,
                    actualDamageToTarget,
                    JellySplitTrigger.Slashing,
                    message => events.Add(new CombatEvent(message)));

            if (didSplitFromSlashing)
                break;

            if (actualDamageToTarget > 0 && TryResolveMonsterExplosionOnHit(target, session, events))
                break;

            if (target.CurrentHitPoints <= 0)
            {
                events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                break;
            }
        }
    }

    private static void TryRevealWillOWispLairTreasureOnLowHp(MonsterInstance target, List<CombatEvent> events)
    {
        if (target?.Template == null)
            return;

        if (!IsWillOWisp(target))
            return;

        if (!target.IsAlive)
            return;

        if (target.CurrentHitPoints > 5)
            return;

        if (target.HasRevealedLairTreasure)
            return;

        target.HasRevealedLairTreasure = true;
        target.IsInLair = true;

        events.Add(new CombatEvent($"{target.DisplayName} falters at {target.CurrentHitPoints} HP, reveals its lair, and gives over its treasure."));
    }

    private static int GetRangedAttacksPerRound(string? fireRate, int roundNumber)
    {
        var rate = (fireRate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rate))
            return 1;

        if (string.Equals(rate, "1/2", StringComparison.OrdinalIgnoreCase))
            return roundNumber % 2 == 0 ? 1 : 0;

        if (int.TryParse(rate, out var parsed) && parsed > 0)
            return parsed;

        return 1;
    }

    private static bool CanActDuringCompleteSurpriseWithCrossbowOfSpeed(Character member, CombatAction? action)
    {
        if (member?.Equipment == null)
            return false;

        if (action?.Type != CombatActionType.Fight)
            return false;

        if (!member.Equipment.TryGetValue(EquipmentSlot.Range, out var rangedWeapon))
            return false;

        return IsCrossbowOfSpeed(rangedWeapon);
    }

    private static bool IsCrossbowOfSpeed(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        return string.Equals(weapon.Name, "Crossbow of Speed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(weapon.Name, "Crossbow of Speed (Heavy Crossbow)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsThiefBackstabAttack(Character member, CombatSession session)
    {
        var isThief = member.Classes.Contains(CharacterClass.Thief)
                      || member.Class == CharacterClass.Thief;

        return isThief
               && session.RoundNumber == 1
               && session.MonstersSurprisedRound1;
    }

    private static int GetThiefBackstabMultiplierByLevel(int thiefLevel)
    {
        if (thiefLevel <= 4) return 2;
        if (thiefLevel <= 8) return 3;
        if (thiefLevel <= 12) return 4;
        return 5;
    }

    private bool ResolveLevel2MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var imageCount = _dice.Roll(4);
            var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
            session.SetMonsterMirrorImage(monster, imageCount, rounds);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
            return true;
        }

        var spellTargets = GetSpellAffectablePartyTargets(monster, session, events, "Melf's Acid Arrow");
        if (spellTargets.Count == 0)
            return true;

        var target = spellTargets[_dice.Roll(spellTargets.Count) - 1];
        var roundsAcid = _dice.Roll(3);
        session.SetPartyAcidArrow(target.Name, roundsAcid);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Melf's Acid Arrow! {target.Name} is hit by acid for {roundsAcid} round(s)."));
        return true;
    }

    private bool ResolveLevel3IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        // Requested behavior: 50% Improved Phantasmal Force, 50% Mirror Image.
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Phantasmal Force!"));
            ResolveIllusionistPhantasmalForce(monster, session, events);
            return true;
        }

        var imageCount = _dice.Roll(4);
        var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
        session.SetMonsterMirrorImage(monster, imageCount, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel3ClericSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Glyph of Warding");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = _dice.Roll(4) + _dice.Roll(4); // same glyph damage model used elsewhere
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
            var fireSaveBonus = GetFireResistanceSaveBonus(partyTarget);
        if (fireSaveBonus > 0)
            saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var preSaveDamage = ApplyFireProtectionDamageReduction(partyTarget, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
        var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);
        TryBurnAwayWebsFromFireDamage(partyTarget, "flame strike", session, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Glyph of Warding! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Glyph of Warding! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by glyph of warding!"));
        }

        return true;
    }

    private bool ResolveLevel5ClericSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8) + 3;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Critical Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Flame Strike");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(8);

        rolledDamage = ApplyBroochOfShieldingAgainstMagicMissile(partyTarget, rolledDamage, events);
        if (rolledDamage <= 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName}'s magic missiles are fully absorbed by {partyTarget.Name}'s Brooch of Shielding."));
            return true;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
        var fireSaveBonus = GetFireResistanceSaveBonus(partyTarget);
        if (fireSaveBonus > 0)
            saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var preSaveDamage = ApplyFireProtectionDamageReduction(partyTarget, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
        if (preSaveDamage <= 0)
        {
            events.Add(new CombatEvent($"{partyTarget.Name} is protected from flame strike damage."));
            return true;
        }

        var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Flame Strike! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Flame Strike! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by flame strike!"));
        }

        return true;
    }

    private bool ResolveLevel2DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);

        var canCastCureLightWounds = damagedAllies.Count > 0;
        var spellRoll = _dice.Roll(100);
        var castBarkskin = !canCastCureLightWounds || spellRoll <= 50;

        if (castBarkskin)
        {
            var barkskinCandidates = session.AliveMonsters
                .Where(m => session.GetMonsterBarkskinRounds(m) <= 0)
                .ToList();

            if (barkskinCandidates.Count == 0)
                barkskinCandidates = session.AliveMonsters.ToList();

            if (barkskinCandidates.Count == 0)
                return false;

            var target = barkskinCandidates[_dice.Roll(barkskinCandidates.Count) - 1];
            var bonus = 1;
            var rounds = 4 + Math.Max(1, monster.Template.HitDice);

            var existingBonus = session.GetMonsterBarkskinBonus(target);
            if (existingBonus > 0)
            {
                target.AdjustArmorClass(existingBonus);
                session.ClearMonsterBarkskin(target);
            }

            target.AdjustArmorClass(-bonus);
            session.SetMonsterBarkskin(target, bonus, rounds);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Barkskin on {target.DisplayName}. AC improves by {bonus} for {rounds} round(s)."));
            return true;
        }

        var cureTarget = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = cureTarget.CurrentHitPoints;
        cureTarget.CurrentHitPoints = Math.Min(cureTarget.MaxHitPoints, cureTarget.CurrentHitPoints + healRoll);
        ApplySwordOfWoundingHealingRestriction(session, cureTarget);
        var actual = cureTarget.CurrentHitPoints - before;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {cureTarget.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{cureTarget.CurrentHitPoints}."));
        return true;
    }

    private bool ResolveLevel3DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Call Lightning / Summon Insects");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Call Lightning!"));
            var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var rolledDamage = _dice.RollMany(6, 8);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (target.LightningProtectionSaveBonusVsLightning > 0)
                saveTarget = Math.Max(1, saveTarget - target.LightningProtectionSaveBonusVsLightning);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyLightningProtectionDamageReduction(target, rolledDamage, isMagicalLightning: true, isNormalLightning: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from call lightning damage."));
                return true;
            }

            var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            TryBurnAwayWebsFromFireDamage(target, "fireball", session, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half lightning damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} lightning damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by call lightning!"));
            }

            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Summon Insects!"));
        var insectTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rounds = Math.Max(1, monster.Template.HitDice);
        insectTarget.AddStatus(CharacterStatus.Paralyzed);
        session.SetPartyAsleep(insectTarget.Name, 0);
        events.Add(new CombatEvent($"{insectTarget.Name} is swarmed by summoned insects and cannot act for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel4DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Cause Serious Wounds");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = _dice.Roll(8) + _dice.Roll(8) + 1;
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var applied = saveRoll >= saveTarget ? Math.Max(1, rolledDamage / 2) : rolledDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Cause Serious Wounds! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Cause Serious Wounds! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by cause serious wounds!"));
        }

        return true;
    }

    private bool ResolveLevel5DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Wall of Fire / Insect Plague");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            // Wall of Fire: direct very-hot-fire exposure on one party target.
            var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var rounds = Math.Max(1, monster.Template.HitDice);
            session.SetPartyVeryHotFireExposure(partyTarget.Name, rounds, damagePerRound: 10);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Wall of Fire! {partyTarget.Name} is directly within very hot flames for {rounds} round(s)."));
            return true;
        }

        // Insect Plague: panic one party member for 2-6 rounds (save negates).
        var insectTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var panicRounds = _dice.Roll(5) + 1;
        var panicSaveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            insectTarget,
            _savingThrowService.GetSaveTarget(insectTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            panicSaveTarget = Math.Max(1, panicSaveTarget - 1);

        var panicSaveRoll = _dice.Roll(20);
        if (panicSaveRoll >= panicSaveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Insect Plague, but {insectTarget.Name} resists (save {panicSaveRoll} vs {panicSaveTarget})."));
            return true;
        }

        insectTarget.AddStatus(CharacterStatus.Confused);
        session.SetPartyConfused(insectTarget.Name, panicRounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Insect Plague! {insectTarget.Name} fails save ({panicSaveRoll} vs {panicSaveTarget}), panics, and cannot attack or cast for {panicRounds} round(s)."));
        return true;
    }

    private bool ResolveLevel5MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Cone of Cold / Feeblemind");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Cone of Cold!"));
            var damagePerTarget = (_dice.Roll(4) + 1) * Math.Max(1, monster.Template.HitDice);

            foreach (var target in aliveParty)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                var damageAfterResistance = target.HasActiveResistCold
                    ? Math.Max(1, damagePerTarget / 2)
                    : damagePerTarget;
                var applied = saveRoll >= saveTarget
                    ? Math.Max(1, damageAfterResistance / 2)
                    : damageAfterResistance;

                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
                var actual = before - target.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(target, actual, events);

                events.Add(new CombatEvent(saveRoll >= saveTarget
                    ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half cone of cold damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                    : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} cone of cold damage. HP {before}->{target.CurrentHitPoints}."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is frozen to death!"));
                }
            }

            return true;
        }

        var feeblemindTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var feeblemindSaveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            feeblemindTarget,
            _savingThrowService.GetSaveTarget(feeblemindTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            feeblemindSaveTarget = Math.Max(1, feeblemindSaveTarget - 1);
        var feeblemindSaveRoll = _dice.Roll(20);

        if (feeblemindSaveRoll >= feeblemindSaveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Feeblemind, but {feeblemindTarget.Name} resists (save {feeblemindSaveRoll} vs {feeblemindSaveTarget})."));
            return true;
        }

        feeblemindTarget.AddStatus(CharacterStatus.Feeblemind);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Feeblemind! {feeblemindTarget.Name} fails save ({feeblemindSaveRoll} vs {feeblemindSaveTarget}) and is feebleminded."));
        return true;
    }

    private bool ResolveLevel4MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Ice Storm / Wall of Fire");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Ice Storm!"));

            foreach (var target in aliveParty)
            {
                var rolledDamage = _dice.RollMany(4, 6);
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                var damageAfterResistance = ApplyColdResistanceDamageReduction(target, rolledDamage, isMagicalCold: true, isNormalCold: false);
                var applied = saveRoll >= saveTarget ? Math.Max(1, damageAfterResistance / 2) : damageAfterResistance;

                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
                var actual = before - target.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(target, actual, events);

                events.Add(new CombatEvent(saveRoll >= saveTarget
                    ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half ice storm damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                    : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} ice storm damage. HP {before}->{target.CurrentHitPoints}."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is slain by ice storm!"));
                }
            }

            return true;
        }

        var wallTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rounds = Math.Max(1, monster.Template.HitDice);
        session.SetPartyAcidArrow(wallTarget.Name, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Wall of Fire! {wallTarget.Name} is trapped in flames for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel5IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Chaos / Maze");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Chaos!"));

            var casterLevel = Math.Max(1, monster.Template.HitDice);
            var highestPartyLevel = aliveParty.Max(p => Math.Max(1, p.Level));
            var baseCount = _dice.Roll(4) + _dice.Roll(4); // 2-8
            var bonusCount = Math.Max(0, casterLevel - highestPartyLevel);
            var desiredCount = baseCount + bonusCount;

            var candidates = aliveParty
                .Where(p => !p.HasStatus(CharacterStatus.Dead)
                            && !p.HasStatus(CharacterStatus.Ashes)
                            && !p.HasStatus(CharacterStatus.Lost)
                            && p.CurrentHitPoints > 0)
                .ToList();

            if (candidates.Count == 0)
                return true;

            var affectedCount = Math.Min(candidates.Count, Math.Max(0, desiredCount));
            if (affectedCount <= 0)
            {
                events.Add(new CombatEvent("Chaos affects no party members this cast."));
                return true;
            }

            var affected = candidates
                .OrderBy(_ => _dice.Roll(100))
                .Take(affectedCount)
                .ToList();

            var rounds = casterLevel; // 1 round / caster level
            events.Add(new CombatEvent($"Chaos attempts to affect {desiredCount} target(s); {affected.Count} affected for up to {rounds} round(s)."));

            foreach (var target in affected)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Chaos (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                target.AddStatus(CharacterStatus.Confused);
                session.SetPartyConfused(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is confused for {rounds} round(s)."));
            }

            return true;
        }

        var mazeTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var intelligence = Math.Max(1, mazeTarget.Abilities.Intelligence);
        var roundsInMaze = RollMazeDurationRoundsByIntelligenceForParty(intelligence);
        var displayRounds = intelligence <= 5 ? Math.Max(1, roundsInMaze / 10) : roundsInMaze;
        var unitText = intelligence <= 5 ? "turn(s)" : "round(s)";

        mazeTarget.AddStatus(CharacterStatus.Paralyzed);
        session.SetPartyAsleep(mazeTarget.Name, roundsInMaze);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Maze! {mazeTarget.Name} (INT {intelligence}) is trapped for {displayRounds} {unitText}."));
        return true;
    }

    private bool ResolveLevel4IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Confusion!"));
            var spellTargets = GetSpellAffectablePartyTargets(monster, session, events, "Confusion");
            if (spellTargets.Count == 0)
                return true;

            var casterLevel = Math.Max(1, monster.Template.HitDice);
            var affectedCount = Math.Min(spellTargets.Count, Math.Max(1, casterLevel));
            var rounds = casterLevel;

            var targets = spellTargets
                .OrderBy(_ => _dice.Roll(100))
                .Take(affectedCount)
                .ToList();

            foreach (var target in targets)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Confusion (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                target.AddStatus(CharacterStatus.Confused);
                session.SetPartyConfused(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is confused for {rounds} round(s)."));
            }

            return true;
        }

        var imageCount = _dice.Roll(4) + 1;
        var invisRounds = Math.Max(1, monster.Template.HitDice);
        session.SetMonsterMirrorImage(monster, imageCount, invisRounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Invisibility on itself ({imageCount} mirror image proxy for {invisRounds} round(s))."));
        return true;
    }

    private int RollMazeDurationRoundsByIntelligenceForParty(int intelligence)
    {
        if (intelligence < 3)
        {
            var turns = _dice.Roll(7) + 1; // 2-8 turns
            return turns * 10;
        }

        if (intelligence <= 5)
        {
            var turns = _dice.Roll(4); // 1-4 turns
            return turns * 10;
        }

        if (intelligence <= 8)
            return _dice.Roll(16) + 4; // 5-20 rounds

        if (intelligence <= 11)
            return _dice.Roll(13) + 3; // 4-16 rounds

        if (intelligence <= 14)
            return _dice.Roll(10) + 2; // 3-12 rounds

        if (intelligence <= 17)
            return _dice.Roll(7) + 1; // 2-8 rounds

        return _dice.Roll(4); // 1-4 rounds
    }

    private bool ResolveLevel3MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Slow / Fireball");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var targetCount = Math.Min(Math.Max(1, monster.Template.HitDice), aliveParty.Count);
            var targets = aliveParty
                .OrderBy(_ => _dice.Roll(100))
                .Take(targetCount)
                .ToList();

            var rounds = 3 + Math.Max(1, monster.Template.HitDice);
            var affected = 0;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Slow!"));

            foreach (var target in targets)
            {
                if (target.IsMonkImmuneToDiseaseSlowHaste())
                {
                    events.Add(new CombatEvent($"{target.Name} is immune to Slow effects."));
                    continue;
                }

                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Slow (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                if (!session.IsPartySlowed(target.Name))
                {
                    session.SetPartySlow(target.Name, rounds, target.Move);
                    target.Move = Math.Max(1, target.Move / 2);
                }
                else
                {
                    session.SetPartySlow(target.Name, rounds, session.GetPartySlowOriginalMove(target.Name));
                }

                affected++;
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is slowed for {rounds} round(s)."));
            }

            events.Add(new CombatEvent($"Slow affects up to {Math.Max(1, monster.Template.HitDice)} target(s); {affected} affected."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Fireball!"));
        var fireballDamageDice = Math.Min(10, Math.Max(1, monster.Template.HitDice));
        foreach (var target in aliveParty)
        {
            var rolledDamage = _dice.RollMany(6, fireballDamageDice);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, rolledDamage, fireballDamageDice, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from fireball damage."));
                continue;
            }

            var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half fireball damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} fireball damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by the fireball!"));
            }
        }

        return true;
    }

    private bool ResolveLevel2IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Phantasmal Force!"));
            ResolveIllusionistPhantasmalForce(monster, session, events);
            return true;
        }

        var imageCount = _dice.Roll(4);
        var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
        session.SetMonsterMirrorImage(monster, imageCount, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
        return true;
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

    private void ResolveFigurineAnimalAllies(CombatSession session, List<CombatEvent> events)
    {
        foreach (var summoner in session.Party)
        {
            if (!IsAlive(summoner))
                continue;

            var activeFigurines = summoner.Inventory
                .Where(FigurineOfWondrousPower.IsActiveInAnimalForm)
                .ToList();

            foreach (var figurine in activeFigurines)
            {
                var kind = FigurineOfWondrousPower.GetKind(figurine);
                switch (kind)
                {
                    case FigurineKind.GoldenLions:
                        ResolveGoldenLionsAttacks(session, summoner, figurine, events);
                        break;
                    case FigurineKind.SerpentineOwl:
                        ResolveSerpentineOwlAttack(session, summoner, figurine, events);
                        break;
                    case FigurineKind.EbonyFly:
                        events.Add(new CombatEvent($"{summoner.Name}'s Ebony Fly circles overhead and can assist with travel, but makes no direct attacks."));
                        break;
                }
            }
        }
    }

    private void ResolveGoldenLionsAttacks(CombatSession session, Character summoner, Item figurine, List<CombatEvent> events)
    {
        for (int lion = 1; lion <= 2; lion++)
        {
            for (int attackIndex = 0; attackIndex < 3; attackIndex++)
            {
                var target = SelectFigurineTargetMonster(session);
                if (target == null)
                    return;

                var roll = _dice.Roll(20);
                const int needed = 11;
                if (roll < needed)
                {
                    events.Add(new CombatEvent($"{summoner.Name}'s Golden Lion #{lion} misses {target.DisplayName} (rolled {roll} vs {needed})."));
                    continue;
                }

                var damage = attackIndex == 2 ? RollDamage("1d8") : RollDamage("1d4");
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                var actual = before - target.CurrentHitPoints;
                events.Add(new CombatEvent($"{summoner.Name}'s Golden Lion #{lion} hits {target.DisplayName} for {actual} damage."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.CurrentHitPoints = 0;
                    events.Add(new CombatEvent($"{target.DisplayName} is slain by {summoner.Name}'s Golden Lion #{lion}."));
                }
            }
        }
    }

    private void ResolveSerpentineOwlAttack(CombatSession session, Character summoner, Item figurine, List<CombatEvent> events)
    {
        var isGiant = FigurineOfWondrousPower.IsSerpentineOwlGiantForm(figurine);
        var target = SelectFigurineTargetMonster(session);
        if (target == null)
            return;

        var attacks = isGiant ? 2 : 2;
        var damageExpr = isGiant ? "1d6" : "1d2";
        var needed = isGiant ? 11 : 14;

        for (int i = 0; i < attacks; i++)
        {
            var roll = _dice.Roll(20);
            if (roll < needed)
            {
                events.Add(new CombatEvent($"{summoner.Name}'s {(isGiant ? "Giant " : string.Empty)}Serpentine Owl misses {target.DisplayName} (rolled {roll} vs {needed})."));
                continue;
            }

            var damage = RollDamage(damageExpr);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            events.Add(new CombatEvent($"{summoner.Name}'s {(isGiant ? "Giant " : string.Empty)}Serpentine Owl hits {target.DisplayName} for {actual} damage."));

            if (target.CurrentHitPoints <= 0)
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{target.DisplayName} is slain by {summoner.Name}'s Serpentine Owl."));
                break;
            }
        }
    }

    private MonsterInstance? SelectFigurineTargetMonster(CombatSession session)
    {
        var aliveMonsters = session.AliveMonsters.ToList();
        if (aliveMonsters.Count == 0)
            return null;

        var index = _dice.Roll(aliveMonsters.Count) - 1;
        return aliveMonsters[index];
    }

    private void ResolveHornOfValhallaUse(CombatSession session, Character user, Item horn, List<CombatEvent> events)
    {
        var existingType = HornOfValhalla.GetType(horn);
        var typeRoll = _dice.Roll(20);
        var hornType = HornOfValhalla.GetOrAssignType(horn, typeRoll);
        var typeName = hornType.ToString();

        if (existingType == HornOfValhallaType.Unknown)
        {
            var aligned = _dice.Roll(100) <= 50;
            var assignedAlignment = aligned ? RollRandomAlignment() : (Alignment?)null;
            HornOfValhalla.EnsureAlignmentProfile(horn, aligned, assignedAlignment);
            if (aligned && assignedAlignment.HasValue)
                events.Add(new CombatEvent($"{horn.Name} reveals itself as a {typeName} horn aligned to {assignedAlignment.Value.ToDisplayString()}."));
            else
                events.Add(new CombatEvent($"{horn.Name} reveals itself as a {typeName} horn with no alignment binding."));
        }

        if (HornOfValhalla.IsOnCooldown(horn, session.DungeonDay))
        {
            var remaining = HornOfValhalla.GetCooldownDaysRemaining(horn, session.DungeonDay);
            events.Add(new CombatEvent($"{horn.Name} cannot be sounded yet. {remaining} day(s) remain before it can be used again."));
            return;
        }

        var fighterLevel = HornOfValhalla.GetFighterLevel(hornType);
        var berserkerCount = HornOfValhalla.RollBerserkerCount(hornType, _dice.Roll);
        var classAllowed = CanCharacterUseHornType(user, hornType);
        var hostileByClass = !classAllowed;
        var hostileByAlignment = false;
        if (HornOfValhalla.IsAligned(horn)
            && HornOfValhalla.TryGetAlignment(horn, out var hornAlignment)
            && IsRadicallyDifferentAlignment(user.Alignment, hornAlignment))
            hostileByAlignment = true;

        var hostile = hostileByClass || hostileByAlignment;

        HornOfValhalla.MarkUsed(horn, session.DungeonDay);

        var levelOrdinal = fighterLevel switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{fighterLevel}th"
        };

        RuleApplicationInfo.PublishLinked(
            "DMG",
            "Other items/DMG147HordOfValhalla",
            $"{user.Name} blows {horn.Name}: type roll {typeRoll} -> {typeName}, summons {berserkerCount} berserkers ({levelOrdinal} level).");

        if (hostile)
        {
            var hostileGroupId = SpawnHostileHornBerserkers(session, user, hornType, fighterLevel, berserkerCount);
            var reasons = new List<string>();
            if (hostileByClass)
                reasons.Add("blower class cannot use this horn type");
            if (hostileByAlignment)
                reasons.Add("radical alignment difference with horn alignment");

            events.Add(new CombatEvent(
                $"{user.Name} sounds {horn.Name}: {berserkerCount} {levelOrdinal}-level berserker(s) answer from Valhalla and turn hostile ({string.Join("; ", reasons)})."));
            events.Add(new CombatEvent(
                $"Hostile berserkers appear as a separate enemy group ({hostileGroupId}) and can be attacked by the party."));
            return;
        }

        session.ActiveHornOfValhallaSummons.Add(
            new HornOfValhallaSummon(user.Name, typeName, hostileToParty: false, fighterLevel, berserkerCount, roundsRemaining: 60));

        events.Add(new CombatEvent(
            $"{user.Name} sounds {horn.Name}: {berserkerCount} {levelOrdinal}-level berserker(s) answer from Valhalla and await commands."));
    }

    private void ResolveHornOfValhallaSummons(CombatSession session, List<CombatEvent> events)
    {
        ResolveHostileHornOfValhallaGroupExpiry(session, events);

        if (session.ActiveHornOfValhallaSummons.Count == 0)
            return;

        for (var i = session.ActiveHornOfValhallaSummons.Count - 1; i >= 0; i--)
        {
            var summon = session.ActiveHornOfValhallaSummons[i];
            if (summon.RoundsRemaining <= 0 || summon.BerserkerCount <= 0)
            {
                session.ActiveHornOfValhallaSummons.RemoveAt(i);
                continue;
            }

            for (var attacker = 0; attacker < summon.BerserkerCount; attacker++)
            {
                var target = SelectFigurineTargetMonster(session);
                if (target == null)
                    break;

                ResolveHornAttackAgainstMonster(summon, target, events);
            }

            summon.RoundsRemaining -= 1;
            if (summon.RoundsRemaining <= 0)
            {
                events.Add(new CombatEvent($"The berserkers summoned by {summon.SummonerName}'s {summon.HornTypeName} horn fade back to Valhalla."));
                session.ActiveHornOfValhallaSummons.RemoveAt(i);
            }
        }
    }

    private void ResolveHornAttackAgainstMonster(HornOfValhallaSummon summon, MonsterInstance target, List<CombatEvent> events)
    {
        var thac0 = Thac0Calculator.GetThac0(CharacterClass.Fighter, summon.FighterLevel);
        var needed = Math.Clamp(thac0 - target.ArmorClass, 2, 20);
        var roll = _dice.Roll(20);

        if (roll != 20 && (roll == 1 || roll < needed))
        {
            events.Add(new CombatEvent($"Valhalla berserker misses {target.DisplayName} (rolled {roll} vs {needed})."));
            return;
        }

        var damage = RollHornBerserkerDamage();
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;

        events.Add(new CombatEvent($"Valhalla berserker hits {target.DisplayName} for {actual} damage."));
        if (target.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{target.DisplayName} is slain by a Valhalla berserker."));
    }

    private int RollHornBerserkerDamage()
    {
        var styleRoll = _dice.Roll(100);
        var usingSwordOrAxe = styleRoll <= 50;
        return usingSwordOrAxe ? RollDamage("1d8") : RollDamage("1d6");
    }

    private string SpawnHostileHornBerserkers(CombatSession session, Character blower, HornOfValhallaType hornType, int fighterLevel, int berserkerCount)
    {
        var groupId = $"horn-valhalla-{blower.Name}-{session.RoundNumber}-{_dice.Roll(10000)}"
            .Replace(" ", string.Empty);

        var template = BuildHostileHornBerserkerTemplate(hornType, fighterLevel);
        var instances = new List<MonsterInstance>(berserkerCount);
        for (var i = 1; i <= berserkerCount; i++)
            instances.Add(new MonsterInstance(template, i, groupId));

        session.Monsters.AddRange(instances);
        session.HostileHornOfValhallaGroups[groupId] = 60;
        session.NoXpMonsterGroupIds.Add(groupId);
        return groupId;
    }

    private static Monster BuildHostileHornBerserkerTemplate(HornOfValhallaType hornType, int fighterLevel)
    {
        var styleName = Random.Shared.Next(1, 101) <= 50 ? "Sword+Spear" : "BattleAxe+Spear";
        var damage = styleName.StartsWith("BattleAxe", StringComparison.OrdinalIgnoreCase) ? "1d8" : "1d8";

        return new Monster
        {
            Name = $"Valhalla Berserker ({hornType})",
            Type = MonsterType.Humanoid,
            TypeName = "Humanoid",
            Alignment = "Any",
            NumberOfAppearancesMin = 1,
            NumberOfAppearancesMax = 1,
            ArmorClass = 4,
            HitDice = Math.Max(1, fighterLevel),
            HitDiceType = 1,
            ExtraHitPoints = Math.Max(0, fighterLevel * 5),
            THAC0 = Thac0Calculator.GetThac0(CharacterClass.Fighter, fighterLevel),
            NumberOfAttacks = 1,
            MovementRate = "12",
            Movement = new MonsterMovement { Walk = 120 },
            Morale = new MonsterMorale { Value = 12 },
            SavingThrows = new MonsterSavingThrows(),
            Attacks = new List<MonsterAttack>
            {
                new()
                {
                    Name = styleName,
                    NumberOfAttacks = 1,
                    Damage = damage
                }
            },
            BaseXPValue = 0,
            XPValuePerHitPoint = 0
        };
    }

    private void ResolveHostileHornOfValhallaGroupExpiry(CombatSession session, List<CombatEvent> events)
    {
        if (session.HostileHornOfValhallaGroups.Count == 0)
            return;

        foreach (var groupId in session.HostileHornOfValhallaGroups.Keys.ToList())
        {
            var aliveInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
            if (aliveInGroup.Count == 0)
            {
                session.HostileHornOfValhallaGroups.Remove(groupId);
                continue;
            }

            var roundsRemaining = session.HostileHornOfValhallaGroups[groupId] - 1;
            if (roundsRemaining > 0)
            {
                session.HostileHornOfValhallaGroups[groupId] = roundsRemaining;
                continue;
            }

            foreach (var spirit in aliveInGroup)
                spirit.CurrentHitPoints = 0;

            events.Add(new CombatEvent($"Hostile Valhalla berserkers of group {groupId} fade back to Valhalla after 6 turns."));
            session.HostileHornOfValhallaGroups.Remove(groupId);
        }
    }

    private static bool CanCharacterUseHornType(Character user, HornOfValhallaType type)
    {
        var hasCleric = user.Classes.Contains(CharacterClass.Cleric) || user.Class == CharacterClass.Cleric;
        var hasThief = user.Classes.Contains(CharacterClass.Thief) || user.Class == CharacterClass.Thief;

        return type switch
        {
            HornOfValhallaType.Silver => true,
            HornOfValhallaType.Brass => hasCleric || user.IsFighterClassed() || hasThief,
            HornOfValhallaType.Bronze => hasCleric || user.IsFighterClassed(),
            HornOfValhallaType.Iron => user.IsFighterClassed(),
            _ => false
        };
    }

    private static bool IsRadicallyDifferentAlignment(Alignment blowerAlignment, Alignment hornAlignment)
    {
        var blowerAxis = GetMoralAxis(blowerAlignment);
        var hornAxis = GetMoralAxis(hornAlignment);
        return blowerAxis != hornAxis;
    }

    private static int GetMoralAxis(Alignment alignment)
    {
        return alignment switch
        {
            Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood => 1,
            Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil => -1,
            _ => 0
        };
    }

    private Alignment RollRandomAlignment()
    {
        var roll = _dice.Roll(9);
        return roll switch
        {
            1 => Alignment.LawfulGood,
            2 => Alignment.NeutralGood,
            3 => Alignment.ChaoticGood,
            4 => Alignment.LawfulNeutral,
            5 => Alignment.TrueNeutral,
            6 => Alignment.ChaoticNeutral,
            7 => Alignment.LawfulEvil,
            8 => Alignment.NeutralEvil,
            _ => Alignment.ChaoticEvil
        };
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

    private void ResolveLevel1MageSpells(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Magic Missile / Sleep");
        if (aliveParty.Count == 0)
            return;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var damage = _dice.Roll(4) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent($"{monster.DisplayName} casts Magic Missile! {target.Name} takes {actual} damage (rolled {damage})."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }

            return;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Sleep!"));
        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                var saveMessage = $"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget}).";
                events.Add(new CombatEvent(saveMessage));
                RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", saveMessage);
                continue;
            }

            var rounds = _dice.Roll(3) + 1; // 2-4 rounds
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            var failedSaveMessage = $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s).";
            events.Add(new CombatEvent(failedSaveMessage));
            RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedSaveMessage);
        }
    }

    private static List<MonsterInstance> GetMonstersNeedingHealing(CombatSession session)
    {
        return session.AliveMonsters
            .Where(m => m.CurrentHitPoints < Math.Max(0, m.MaxHitPoints - session.GetMonsterWoundingUnhealableDamage(m)))
            .ToList();
    }

    private bool ResolveLevel1PriestSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (session.Level1PriestSpellCastsUsed >= 3)
            return false;

        var damagedAllies = GetMonstersNeedingHealing(session);

        if (damagedAllies.Count == 0)
            return false;

        var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
        ApplySwordOfWoundingHealingRestriction(session, target);
        var actual = target.CurrentHitPoints - before;

        session.Level1PriestSpellCastsUsed += 1;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private enum MonsterSpellTradition
    {
        MagicUser,
        Cleric,
        Druid,
        Illusionist
    }

    private sealed record MonsterLevelMagicUseChance(int ChancePercent, int SpellLevel, bool IsAgeRelatedDragonMagic);
    private sealed record MonsterLevelMagicUseDecision(bool ShouldUseMagic, int Roll, int ChancePercent, MonsterSpellTradition? SelectedTradition);

    private bool CanMonsterAttemptLevelSpell(
        MonsterInstance monster,
        int spellLevel,
        MonsterSpellTradition requestedTradition,
        List<CombatEvent> events,
        Dictionary<int, MonsterLevelMagicUseDecision> decisionCache,
        bool useDefaultFiftyPercentWhenNoConfiguredChance)
    {
        if (monster?.Template == null)
            return false;

        if (!MonsterHasSpellTraditionAtLevel(monster, spellLevel, requestedTradition))
            return false;

        if (decisionCache.TryGetValue(spellLevel, out var cachedDecision))
        {
            if (!cachedDecision.ShouldUseMagic)
                return false;

            return !cachedDecision.SelectedTradition.HasValue || cachedDecision.SelectedTradition.Value == requestedTradition;
        }

        var configuredChance = TryGetMonsterLevelMagicUseChance(monster, spellLevel);
        if (configuredChance == null && !useDefaultFiftyPercentWhenNoConfiguredChance)
            return true;

        var chancePercent = configuredChance?.ChancePercent ?? 50;
        var isAgeRelatedDragonMagic = configuredChance?.IsAgeRelatedDragonMagic == true;
        var roll = _dice.Roll(100);
        var shouldUseMagic = roll <= chancePercent;

        MonsterSpellTradition? selectedTradition = null;
        if (shouldUseMagic)
            selectedTradition = SelectSpellTraditionForLevel(monster, spellLevel);

        decisionCache[spellLevel] = new MonsterLevelMagicUseDecision(shouldUseMagic, roll, chancePercent, selectedTradition);

        var traditionText = selectedTradition.HasValue
            ? $" Will use {FormatTraditionName(selectedTradition.Value)} level {spellLevel} spells."
            : string.Empty;

        var sourceText = isAgeRelatedDragonMagic
            ? " Age-related dragon magic"
            : " Level magic use";

        events.Add(new CombatEvent(
            $"{monster.DisplayName}{sourceText}: chance {chancePercent}%, roll {roll}, outcome {(shouldUseMagic ? "use magic" : "no magic")}. Dragon can use level {spellLevel} magic.{traditionText}"));

        if (!shouldUseMagic)
            return false;

        return !selectedTradition.HasValue || selectedTradition.Value == requestedTradition;
    }

    private static bool MonsterHasSpellTraditionAtLevel(MonsterInstance monster, int spellLevel, MonsterSpellTradition tradition)
    {
        return HasSpellTierAbility(monster, spellLevel, tradition);
    }

    private MonsterSpellTradition? SelectSpellTraditionForLevel(MonsterInstance monster, int spellLevel)
    {
        var availableTraditions = new List<MonsterSpellTradition>();

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.MagicUser))
            availableTraditions.Add(MonsterSpellTradition.MagicUser);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Cleric))
            availableTraditions.Add(MonsterSpellTradition.Cleric);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Druid))
            availableTraditions.Add(MonsterSpellTradition.Druid);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Illusionist))
            availableTraditions.Add(MonsterSpellTradition.Illusionist);

        if (availableTraditions.Count == 0)
            return null;

        var picked = _dice.Roll(availableTraditions.Count) - 1;
        return availableTraditions[picked];
    }

    private static bool HasAnyMonsterSpellcastingAbility(MonsterInstance monster)
    {
        for (var level = 1; level <= 9; level++)
        {
            if (HasSpellTierAbility(monster, level, MonsterSpellTradition.MagicUser)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Cleric)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Druid)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Illusionist))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSpellTierAbility(MonsterInstance monster, int spellLevel, MonsterSpellTradition tradition)
    {
        if (spellLevel <= 0)
            return false;

        return tradition switch
        {
            MonsterSpellTradition.MagicUser => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Mage spells",
                $"Level {spellLevel} Magic-User spells",
                $"Lvl {spellLevel} Mage spells",
                $"Lvl {spellLevel} Magic-User spells"),
            MonsterSpellTradition.Cleric => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Priest spells",
                $"Level {spellLevel} Cleric spells",
                $"Lvl {spellLevel} Priest spells",
                $"Lvl {spellLevel} Cleric spells"),
            MonsterSpellTradition.Druid => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Druid spells",
                $"Lvl {spellLevel} Druid spells"),
            MonsterSpellTradition.Illusionist => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Illusionist spells",
                $"Lvl {spellLevel} Illusionist spells"),
            _ => false
        };
    }

    private static string FormatTraditionName(MonsterSpellTradition tradition)
    {
        return tradition switch
        {
            MonsterSpellTradition.MagicUser => "Magic-User",
            MonsterSpellTradition.Cleric => "Cleric",
            MonsterSpellTradition.Druid => "Druid",
            MonsterSpellTradition.Illusionist => "Illusionist",
            _ => "Unknown"
        };
    }

    private static MonsterLevelMagicUseChance? TryGetMonsterLevelMagicUseChance(MonsterInstance monster, int spellLevel)
    {
        if (monster?.Template?.SpecialAbilities == null || monster.Template.SpecialAbilities.Count == 0)
            return null;

        foreach (var ability in monster.Template.SpecialAbilities)
        {
            if (ability == null || string.IsNullOrWhiteSpace(ability.Name))
                continue;

            if (!TryParseMonsterLevelMagicUseAbility(monster, ability.Name, ability.Description, out var parsed))
                continue;

            if (parsed.SpellLevel == spellLevel)
                return parsed;
        }

        return null;
    }

    private static bool TryParseMonsterLevelMagicUseAbility(MonsterInstance monster, string abilityName, string? abilityDescription, out MonsterLevelMagicUseChance parsed)
    {
        var trimmed = abilityName?.Trim() ?? string.Empty;
        var match = Regex.Match(
            trimmed,
            @"^(?<chance>\d{1,3})\s*%\s*Level\s*(?<level>\d+)\s*Magic\s*Use$",
            RegexOptions.IgnoreCase);

        if (match.Success
            && int.TryParse(match.Groups["chance"].Value, out var chance)
            && int.TryParse(match.Groups["level"].Value, out var level)
            && level > 0)
        {
            var isAgeRelated = !string.IsNullOrWhiteSpace(abilityDescription)
                && abilityDescription.Contains("dragon age-based", StringComparison.OrdinalIgnoreCase);
            parsed = new MonsterLevelMagicUseChance(Math.Clamp(chance, 0, 100), level, isAgeRelated);
            return true;
        }

        var ageRelatedMatch = Regex.Match(
            trimmed,
            @"^(?<chance>\d{1,3})\s*%\s*Level\s*Age\s*related\s*Magic\s*Use\s*every\s*(?<step>\d+(?:[.,]\d+)?)\s*agelevels?$",
            RegexOptions.IgnoreCase);

        if (ageRelatedMatch.Success
            && int.TryParse(ageRelatedMatch.Groups["chance"].Value, out var ageChance)
            && TryParseAgeRelatedMagicUseStep(ageRelatedMatch.Groups["step"].Value, out var step))
        {
            var ageLevel = ResolveMonsterAgeLevel(monster);
            var spellLevel = Math.Max(1, (int)Math.Floor((ageLevel - 1d) / step) + 1);
            parsed = new MonsterLevelMagicUseChance(Math.Clamp(ageChance, 0, 100), spellLevel, true);
            return true;
        }

        parsed = null!;
        return false;
    }

    private static bool TryParseAgeRelatedMagicUseStep(string raw, out double step)
    {
        step = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out step))
            return false;

        return step > 0;
    }

    private static int ResolveMonsterAgeLevel(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return 1;

        var ageFromName = TryResolveAgeLevelFromText(monster.Template.Name);
        if (ageFromName.HasValue)
            return ageFromName.Value;

        if (monster.Template.Type == MonsterType.Dragon
            && monster.Template.HitDice > 0
            && monster.MaxHitPoints > 0)
        {
            var hpPerDie = monster.MaxHitPoints / (double)monster.Template.HitDice;
            return Math.Clamp((int)Math.Round(hpPerDie, MidpointRounding.AwayFromZero), 1, 8);
        }

        return 1;
    }

    private static int? TryResolveAgeLevelFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (normalized.Contains("Very young", StringComparison.OrdinalIgnoreCase)) return 1;
        if (normalized.Contains("Young adult", StringComparison.OrdinalIgnoreCase)) return 4;
        if (normalized.Contains("Sub-adult", StringComparison.OrdinalIgnoreCase)) return 3;
        if (normalized.Contains("Very old", StringComparison.OrdinalIgnoreCase)) return 7;
        if (normalized.Contains("Ancient", StringComparison.OrdinalIgnoreCase)) return 8;
        if (normalized.Contains("Young", StringComparison.OrdinalIgnoreCase)) return 2;
        if (normalized.Contains("Adult", StringComparison.OrdinalIgnoreCase)) return 5;
        if (normalized.Contains("Old", StringComparison.OrdinalIgnoreCase)) return 6;

        return null;
    }

    private bool ResolveLevel1DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Entangle / Faerie Fire");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Entangle!"));
            foreach (var target in aliveParty)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Entangle (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                var rounds = 5;
                target.AddStatus(CharacterStatus.Paralyzed);
                session.SetPartyAsleep(target.Name, 0);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is entangled for {rounds} round(s)."));
            }

            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Faerie Fire!"));
        var affected = 0;
        foreach (var target in aliveParty)
        {
            if (session.FaerieFiredPartyMembers.Contains(target.Name))
                continue;

            target.ArmorClass += 2;
            session.FaerieFiredPartyMembers.Add(target.Name);
            affected++;
            events.Add(new CombatEvent($"{target.Name} is outlined by faerie fire: AC worsens by 2."));
        }

        if (affected == 0)
            events.Add(new CombatEvent("No additional party members are affected by faerie fire."));

        return true;
    }

    private bool ResolveLevel1IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Color Spray!"));
            ResolveIllusionistColorSpray(monster, session, events);
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Phantasmal Force!"));
        ResolveIllusionistPhantasmalForce(monster, session, events);
        return true;
    }

    private void ResolveIllusionistColorSpray(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Color Spray");
        if (aliveParty.Count == 0)
            return;

        var maxAffected = Math.Min(aliveParty.Count, _dice.Roll(6));
        var targets = aliveParty.OrderBy(_ => _dice.Roll(100)).Take(maxAffected).ToList();
        var casterLevel = Math.Max(1, monster.Template.HitDice);

        foreach (var target in targets)
        {
            var hdDifference = Math.Max(1, target.Level) - casterLevel;
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (hdDifference <= 0)
            {
                var rounds = _dice.Roll(4) + _dice.Roll(4);
                target.AddStatus(CharacterStatus.Asleep);
                session.SetPartyAsleep(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} is overwhelmed by colors and falls unconscious for {rounds} round(s)."));
            }
            else if (hdDifference <= 2)
            {
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Color Spray (save {saveRoll} vs {saveTarget})."));
                }
                else
                {
                    var rounds = _dice.Roll(4);
                    target.AddStatus(CharacterStatus.Asleep);
                    session.SetPartyAsleep(target.Name, rounds);
                    events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is unconscious for {rounds} round(s)."));
                }
            }
            else
            {
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Color Spray (save {saveRoll} vs {saveTarget})."));
                }
                else
                {
                    if (target.IsMonkImmuneToDiseaseSlowHaste())
                    {
                        events.Add(new CombatEvent($"{target.Name} is immune to Slow effects."));
                        continue;
                    }

                    target.AddStatus(CharacterStatus.Slowed);
                    events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is stunned by the spray."));
                }
            }
        }
    }

    private void ResolveIllusionistPhantasmalForce(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Phantasmal Force");
        if (aliveParty.Count == 0)
            return;

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} disbelieves the phantasmal force (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rolledDamage = 0;
            for (int i = 0; i < 16; i++)
                rolledDamage += _dice.Roll(6);

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - rolledDamage);
            var actual = Math.Max(0, before - target.CurrentHitPoints);
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent($"{target.Name} believes the phantasmal force and takes {actual} damage (rolled {rolledDamage}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} dies from terror and shock."));
            }
        }
    }

    private static List<Character> GetSpellAffectablePartyTargets(
        MonsterInstance monster,
        CombatSession session,
        List<CombatEvent> events,
        string spellName)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return aliveParty;

        var blockedTargets = aliveParty
            .Where(member => member.HasActiveProtectionFromMagicScroll)
            .ToList();

        foreach (var blocked in blockedTargets)
            events.Add(new CombatEvent($"{blocked.Name} is inside an anti-magic globe; {spellName} cannot affect them."));

        var affectable = aliveParty
            .Where(member => !member.HasActiveProtectionFromMagicScroll)
            .ToList();

        if (affectable.Count == 0 && blockedTargets.Count > 0)
            events.Add(new CombatEvent($"{monster.DisplayName}'s {spellName} is blocked: anti-magic protection prevents magic from entering the sphere."));

        return affectable;
    }

    private bool ResolveHpDamageBreath(MonsterInstance monster, int maxDamage, CombatSession session, List<CombatEvent> events)
    {
        if (maxDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var rolledDamage = _dice.Roll(maxDamage);
        events.Add(new CombatEvent($"{monster.DisplayName} uses {maxDamage} HP damage breath (rolled {rolledDamage})!"));

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, rolledDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s breath."));
                continue;
            }

            var damage = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            TryBurnAwayWebsFromFireDamage(target, "fire breath", session, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds breath save ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails breath save ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private bool ResolveDragonBreathCurrentHp(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var breathDamage = Math.Max(0, monster.CurrentHitPoints);
        if (breathDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        events.Add(new CombatEvent($"{monster.DisplayName} uses Dragon breath for {breathDamage} damage!"));

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var dragonName = monster.Template.Name?.Trim() ?? string.Empty;
            var isBlueDragonLightningBreath = string.Equals(dragonName, "Blue Dragon", StringComparison.OrdinalIgnoreCase);
            var isWhiteDragonColdBreath = string.Equals(dragonName, "White Dragon", StringComparison.OrdinalIgnoreCase);
            var isSilverDragonColdBreath = string.Equals(dragonName, "Silver Dragon", StringComparison.OrdinalIgnoreCase);
            var isRedDragonFireBreath = string.Equals(dragonName, "Red Dragon", StringComparison.OrdinalIgnoreCase);
            var isGoldDragonFireBreath = string.Equals(dragonName, "Gold Dragon", StringComparison.OrdinalIgnoreCase);
            var isBrassDragonFireBreath = string.Equals(dragonName, "Brass Dragon", StringComparison.OrdinalIgnoreCase);
            var isColdDragonBreath = isWhiteDragonColdBreath || isSilverDragonColdBreath;
            var isFireDragonBreath = isRedDragonFireBreath || isGoldDragonFireBreath || isBrassDragonFireBreath;
            if (isBlueDragonLightningBreath && target.LightningProtectionSaveBonusVsLightning > 0)
                saveTarget = Math.Max(1, saveTarget - target.LightningProtectionSaveBonusVsLightning);
            if (isColdDragonBreath && target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
            if (isFireDragonBreath)
            {
                var fireSaveBonus = GetFireResistanceSaveBonus(target);
                if (fireSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            }
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = breathDamage;
            if (isFireDragonBreath)
                preSaveDamage = ApplyFireProtectionDamageReduction(target, preSaveDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (isBlueDragonLightningBreath)
                preSaveDamage = ApplyLightningProtectionDamageReduction(target, preSaveDamage, isMagicalLightning: true, isNormalLightning: false);
            if (isColdDragonBreath)
                preSaveDamage = ApplyColdResistanceDamageReduction(target, preSaveDamage, isMagicalCold: true, isNormalCold: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s dragon breath."));
                continue;
            }

            var applied = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds save vs Breath ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails save vs Breath ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by dragon breath!"));
            }
        }

        return true;
    }

    private static bool TryGetHpDamageBreathDamage(MonsterInstance monster, out int damage)
    {
        foreach (var ability in monster.Template.SpecialAbilities)
        {
            var name = ability.Name?.Trim() ?? string.Empty;
            var m = Regex.Match(name, @"^(?<damage>\d+)\s*hp damage breath$", RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;

            if (int.TryParse(m.Groups["damage"].Value, out damage) && damage > 0)
                return true;
        }

        damage = 0;
        return false;
    }

    private bool TryResolvePyroHydraBreath(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pyro hydra breath") || session.PyroHydraBreathUsed.Contains(monster))
            return false;

        var targets = session.ExposedParty.ToList();
        if (targets.Count == 0)
            return false;

        // Hydra Hit Dice represent the number of heads, not the number of bite attacks.
        var headCount = monster.Template.HitDice;
        if (headCount <= 0)
            throw new InvalidOperationException($"{monster.DisplayName} requires a positive head count for Pyro hydra breath.");

        var breathDamage = 8 * headCount;
        session.PyroHydraBreathUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} uses Pyro hydra breath: {headCount} heads deal {breathDamage} fire damage (8 per head; save vs. breath weapon for {4 * headCount}, 4 per head). This attack can be used only once per battle."));
        RuleApplicationInfo.PublishLinked("MM", "Hydra", $"{monster.DisplayName}: Pyro hydra breath deals 8 damage per head, or 4 per head on a successful save vs. breath weapon; once per battle.");
        ResolveFireBreathDamage(monster, targets, breathDamage, "Pyro hydra breath", events);
        return true;
    }

    private bool TryResolveHellHoundBreatheFire(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var isHellHound = string.Equals(monster.Template.Name?.Trim(), "Hell Hound", StringComparison.OrdinalIgnoreCase);
        if (!isHellHound)
            return false;

        if (!HasSpecialAbility(monster, "Hell Hound Breathe Fire")
            && !HasSpecialAbility(monster, "Breathe Fire"))
            return false;

        var breathDamage = Math.Max(0, monster.Template.HitDice);
        if (breathDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        events.Add(new CombatEvent($"{monster.DisplayName} uses Hell Hound Breathe Fire for {breathDamage} damage (1 HP per HD)!"));
        ResolveFireBreathDamage(monster, aliveParty, breathDamage, "Hell Hound fire breath", events);
        return true;
    }

    private void ResolveFireBreathDamage(MonsterInstance monster, IReadOnlyList<Character> targets, int breathDamage, string breathName, List<CombatEvent> events)
    {
        foreach (var target in targets)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);

            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, breathDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s fire breath."));
                continue;
            }

            var damage = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds save vs. breath weapon ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails save vs. breath weapon ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by {breathName}!"));
            }
        }
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

    private void TryApplyBoneHook(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Bone Hook") || !IsAlive(target))
            return;

        if (_boneHooks.Contains((monster, target)))
            return;

        var roll = _dice.Roll(100);
        var stuck = roll <= 50;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Bone Hook",
            $"{monster.DisplayName} bane hook on {target.Name}",
            "Any creature caught (hit) by the hook has a 50% chance of being stuck fast.",
            "1",
            "100",
            roll.ToString(),
            stuck ? "Stuck fast." : "Not stuck.");

        if (!stuck)
            return;

        _boneHooks.Add((monster, target));
        events.Add(new CombatEvent($"{monster.DisplayName}'s bone hook snares {target.Name}, who is stuck fast!"));
    }

    private bool TryResolveBoneDevilTail(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Bone Hook"))
            return false;

        var victims = _boneHooks
            .Where(h => ReferenceEquals(h.Monster, monster) && IsAlive(h.Victim) && h.Victim.CurrentHitPoints > 0)
            .Select(h => h.Victim)
            .ToList();
        _boneHooks.RemoveWhere(h => ReferenceEquals(h.Monster, monster) && !victims.Contains(h.Victim));

        if (victims.Count == 0)
            return false;

        var target = victims[_dice.Roll(victims.Count) - 1];
        var damage = _dice.Roll(4) + _dice.Roll(4);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);
        events.Add(new CombatEvent($"{monster.DisplayName} strikes the hooked {target.Name} with its tail for {actual} damage (2d4). HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
            return true;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the tail's strength drain (save vs poison {saveRoll} vs {saveTarget})."));
            return true;
        }

        var loss = _dice.Roll(4);
        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var actualLoss = Math.Min(loss, Math.Max(0, beforeStrength - 1));
        target.Abilities.Strength = beforeStrength - actualLoss;

        var existing = _boneDevilStrengthLoss.TryGetValue(target, out var current) ? current.Amount : 0;
        _boneDevilStrengthLoss[target] = (existing + actualLoss, 10);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Bone Devil Tail",
            $"{monster.DisplayName} tail strikes {target.Name}",
            "Tail does 2-8 damage and causes a loss of 1-4 Strength unless a save vs. poison is made; strength loss lasts 10 melee rounds.",
            "1",
            "4",
            loss.ToString(),
            $"Save {saveRoll} vs {saveTarget} failed; STR {beforeStrength}->{target.Abilities.Strength} for 10 rounds.");

        events.Add(new CombatEvent($"{target.Name} fails the save vs poison ({saveRoll} vs {saveTarget}) and loses {actualLoss} Strength for 10 rounds: STR {beforeStrength}->{target.Abilities.Strength}."));
        return true;
    }

    private void TickBoneDevilStrengthLoss(Character member, List<CombatEvent> events)
    {
        if (!_boneDevilStrengthLoss.TryGetValue(member, out var loss))
            return;

        var remaining = loss.RoundsRemaining - 1;
        if (remaining > 0)
        {
            _boneDevilStrengthLoss[member] = (loss.Amount, remaining);
            return;
        }

        member.Abilities.Strength += loss.Amount;
        _boneDevilStrengthLoss.Remove(member);
        events.Add(new CombatEvent($"{member.Name}'s Strength returns (+{loss.Amount})."));
    }

    private void ClearBoneDevilEffects()
    {
        foreach (var (character, loss) in _boneDevilStrengthLoss.ToList())
            character.Abilities.Strength += loss.Amount;

        _boneDevilStrengthLoss.Clear();
        _boneHooks.Clear();
        _confusingGazeResolved.Clear();
        _fearAuraResolved.Clear();
        _deathGroanUsed.Clear();
        _nightHagMagicMissileUses.Clear();
        _nightHagSleepUsed.Clear();
        _nightHagEnfeeblementUses.Clear();
        _nightHagGateAttempted.Clear();
        _dreamRidingVictims.Clear();
    }

    private void ApplyDreamRidingDrain(CombatSession session, List<CombatEvent> events)
    {
        // The hag rides the victims that resisted her sleep spell that night, so she must have survived the encounter.
        foreach (var (hag, victim) in _dreamRidingVictims.ToList())
        {
            if (!hag.IsAlive || !IsAlive(victim))
                continue;

            var before = victim.Abilities.Constitution;
            victim.Abilities.Constitution = Math.Max(0, before - 1);
            events.Add(new CombatEvent($"That night {hag.DisplayName} rides {victim.Name} in the dream realm: CON {before}->{victim.Abilities.Constitution} (permanent)."));

            if (victim.Abilities.Constitution <= 0)
            {
                victim.CurrentHitPoints = 0;
                victim.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{victim.Name} reaches 0 Constitution and dies; {hag.DisplayName} carries off the larval soul."));
            }
        }
    }

    private bool TryResolveNightHagGate(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Gate") || _nightHagGateAttempted.Contains(monster))
            return false;

        if (MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);

        var roll = _dice.Roll(100);
        var success = roll <= 50;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Gate",
            $"{monster.DisplayName} attempts to gate in an ally",
            "A night hag may attempt to gate in a Type I demon or a barbed devil (50% chance); one attempt per encounter.",
            "1",
            "100",
            roll.ToString(),
            success ? "Success." : "Failure.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in an ally but fails (roll {roll} vs 50%)."));
            return true;
        }

        var templateName = _dice.Roll(2) == 1 ? "Demon, Type I" : "Devil, Barbed";
        var template = MonsterTemplateLookup(templateName);
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var groupId = $"gate-{monster.GroupId}-{_dice.Roll(10000)}";
        var summoned = new MonsterInstance(template, 1, groupId);
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        return true;
    }

    private bool TryResolveNightHagStrangulation(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Strangulation"))
            return false;

        var sleepers = session.ExposedParty
            .Where(c => IsAlive(c) && c.HasStatus(CharacterStatus.Asleep))
            .ToList();
        if (sleepers.Count == 0)
            return false;

        var target = sleepers[_dice.Roll(sleepers.Count) - 1];
        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Strangulation",
            $"{monster.DisplayName} on {target.Name}",
            "The night hag strangles a sleeping victim; no save is allowed.",
            "0",
            "0",
            "-",
            "Sleeping victim is strangled.");

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{monster.DisplayName} strangles the sleeping {target.Name}, who dies!"));
        return true;
    }

    private bool TryResolveNightHagEnfeeblement(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Ray of Enfeeblement"))
            return false;

        _nightHagEnfeeblementUses.TryGetValue(monster, out var used);
        if (used >= 3 || _dice.Roll(100) > 50)
            return false;

        var targets = GetSpellAffectablePartyTargets(monster, session, events, "Ray of Enfeeblement");
        if (targets.Count == 0)
            return false;

        _nightHagEnfeeblementUses[monster] = used + 1;
        var target = targets[_dice.Roll(targets.Count) - 1];

        var casterLevel = Math.Max(1, monster.Template.HitDice);
        var percent = 25 + 2 * casterLevel;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Ray of Enfeeblement ({used + 1}/3) at {target.Name}, who resists (save {saveRoll} vs {saveTarget})."));
            return true;
        }

        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var loss = Math.Max(1, beforeStrength * percent / 100);
        var actualLoss = Math.Min(loss, Math.Max(0, beforeStrength - 1));
        target.Abilities.Strength = beforeStrength - actualLoss;

        var existing = _boneDevilStrengthLoss.TryGetValue(target, out var current) ? current.Amount : 0;
        _boneDevilStrengthLoss[target] = (existing + actualLoss, casterLevel);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Ray of Enfeeblement",
            $"{monster.DisplayName} on {target.Name}",
            "Ray of enfeeblement reduces Strength by 25% + 2% per caster level (hag counts as level 8 = 41%); save vs. spell negates. Lasts 1 round per level.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget} failed; {percent}% reduction, STR {beforeStrength}->{target.Abilities.Strength} for {casterLevel} rounds.");

        events.Add(new CombatEvent($"{monster.DisplayName} casts Ray of Enfeeblement ({used + 1}/3)! {target.Name} fails the save ({saveRoll} vs {saveTarget}) and Strength drops {percent}%: STR {beforeStrength}->{target.Abilities.Strength} for {casterLevel} round(s)."));
        return true;
    }

    private bool TryResolveNightHagSleep(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Sleep Magic") || _nightHagSleepUsed.Contains(monster))
            return false;

        var targets = GetSpellAffectablePartyTargets(monster, session, events, "Sleep")
            .Where(t => !t.HasStatus(CharacterStatus.Asleep))
            .ToList();
        if (targets.Count == 0)
            return false;

        _nightHagSleepUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} casts a powerful Sleep spell that affects even high-level characters!"));

        foreach (var target in targets)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Night Hag Sleep Magic",
                $"{monster.DisplayName} on {target.Name}",
                "Sleep affects even 12th-level humans (no hit-dice limit) unless a save vs. magic is made.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; falls asleep.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget})."));
                if (HasSpecialAbility(monster, "Dream Riding") && _dreamRidingVictims.Add((monster, target)))
                    events.Add(new CombatEvent($"{monster.DisplayName} marks {target.Name} to be ridden in dreams."));
                continue;
            }

            var rounds = _dice.Roll(4) + _dice.Roll(4);
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s)."));
        }

        return true;
    }

    private bool TryResolveNightHagMagicMissile(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Magic missile"))
            return false;

        _nightHagMagicMissileUses.TryGetValue(monster, out var used);
        if (used >= 3 || _dice.Roll(100) > 50)
            return false;

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Magic Missile");
        if (aliveParty.Count == 0)
            return false;

        _nightHagMagicMissileUses[monster] = used + 1;
        var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolled = _dice.Roll(8) + _dice.Roll(8);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Magic Missile",
            $"{monster.DisplayName} on {target.Name}",
            "Magic missile, 3 times per day, 2-16 damage, never misses.",
            "2",
            "8",
            rolled.ToString(),
            $"Cast {used + 1}/3");

        var damage = ApplyBroochOfShieldingAgainstMagicMissile(target, rolled, events);
        if (damage <= 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName}'s magic missile is fully absorbed by {target.Name}'s Brooch of Shielding."));
            return true;
        }

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Magic Missile ({used + 1}/3)! {target.Name} takes {actual} damage (rolled {rolled}). HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private void ApplyFearAura(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Fear Aura"))
            return;

        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            if (!_fearAuraResolved.Add((monster, target)))
                continue;

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} sees {monster.DisplayName} but resists fear (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(4);
            target.AddStatus(CharacterStatus.Confused);
            session.SetPartyConfused(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and is paralyzed with fear at the sight of {monster.DisplayName} for {rounds} round(s)!"));
        }
    }

    private bool TryResolveDeathGroan(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Death Groan") || _deathGroanUsed.Contains(monster))
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        // Combat takes place underground, so it is always treated as darkness; usable once per day.
        _deathGroanUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} lets out a terrible keening wail!"));

        foreach (var target in party)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Death Groan",
                $"{monster.DisplayName} wail on {target.Name}",
                "Hearers within 3\" must save vs. magic or die on the spot. Usable once per day, only during darkness.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; dies.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists the wail (save vs magic {saveRoll} vs {saveTarget})."));
                continue;
            }

            target.CurrentHitPoints = 0;
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and dies from the wail!"));
        }

        return true;
    }

    private void ApplyConfusingGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Confusing Gaze"))
            return;

        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            if (!_confusingGazeResolved.Add((monster, target)))
                continue;

            if (target.HasStatus(CharacterStatus.Confused))
                continue;

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} meets {monster.DisplayName}'s gaze but resists confusion (save vs magic {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(4) + _dice.Roll(4) + _dice.Roll(4);
            target.AddStatus(CharacterStatus.Confused);
            session.SetPartyConfused(target.Name, rounds);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Confusing Gaze",
                $"{monster.DisplayName} confusing gaze on {target.Name}",
                "Any intelligent creature viewing the four eyes must save vs. magic or be confused for 3-12 melee rounds.",
                "3",
                "4",
                rounds.ToString(),
                $"Save {saveRoll} vs {saveTarget} failed; confused for {rounds} round(s).");

            events.Add(new CombatEvent($"{target.Name} fails the save vs magic ({saveRoll} vs {saveTarget}) and is confused by {monster.DisplayName}'s gaze for {rounds} round(s)!"));
        }
    }

    private bool TryResolveBoneDevilSummon(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Summon Bone Devil"))
            return false;

        // One summoning attempt in roughly four rounds; total devils of this kind are capped to keep encounters bounded.
        if (_dice.Roll(4) != 1)
            return false;

        const int maxDevils = 4;
        if (session.AliveMonsters.Count(m => string.Equals(m.Template.Name, monster.Template.Name, StringComparison.OrdinalIgnoreCase)) >= maxDevils)
            return false;

        var roll = _dice.Roll(100);
        var success = roll <= 40;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Devil Summoning",
            $"{monster.DisplayName} attempts to summon another {monster.Template.Name}",
            "A bone devil can summon another bone devil (40% chance of success), once per round.",
            "1",
            "100",
            roll.ToString(),
            success ? "Success." : "Failure.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to summon another {monster.Template.Name} but fails (roll {roll} vs 40%)."));
            return true;
        }

        var nextIndex = session.Monsters
            .Where(m => string.Equals(m.GroupId, monster.GroupId, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Index)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var summoned = new MonsterInstance(monster.Template, nextIndex, monster.GroupId) { IsInLair = monster.IsInLair };
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} summons another devil: {summoned.DisplayName} appears!"));
        return true;
    }

    private bool TryResolveMonsterAcidSpit(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Acid Spit"))
            return false;

        var aliveParty = session.ExposedParty.Where(IsAlive).ToList();
        if (aliveParty.Count == 0)
            return false;

        var isFirstSpit = !_acidSpitRangeInches.ContainsKey(monster);
        if (!isFirstSpit && _dice.Roll(3) != 1)
            return false;

        // Range is determined on the first spit and kept for later spits.
        if (isFirstSpit)
            _acidSpitRangeInches[monster] = _dice.Roll(8);
        var rangeInches = _acidSpitRangeInches[monster];

        var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];

        // Base 50% at 6"; +10% per inch closer, -10% per inch beyond 6". First spit is always inaccurate (10%).
        var hitChance = isFirstSpit ? 10 : Math.Clamp(50 + (6 - rangeInches) * 10, 5, 100);
        var hitRoll = _dice.Roll(100);
        if (hitRoll > hitChance)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} spits acid at {target.Name} from {rangeInches}\" but misses (roll {hitRoll} vs {hitChance}%{(isFirstSpit ? ", first spit" : string.Empty)})."));
            return true;
        }

        var damage = _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8);
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;
        var applied = saved ? damage / 2 : damage;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Acid Spit",
            $"{monster.DisplayName} spits acid at {target.Name}",
            "Acid spit deals 4d8 damage; save vs. breath weapon for half damage. The first spit has only a 10% chance to hit.",
            "4",
            "8",
            damage.ToString(),
            $"Save {saveRoll} vs {saveTarget} => {(saved ? "half damage" : "full damage")}: {applied}.");

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);

        events.Add(new CombatEvent(saved
            ? $"{monster.DisplayName} spits acid at {target.Name}; save {saveRoll} vs {saveTarget} succeeds, half damage: {actual}. HP {before}->{target.CurrentHitPoints}."
            : $"{monster.DisplayName} spits acid at {target.Name}; save {saveRoll} vs {saveTarget} fails, {actual} damage. HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private bool TryResolveMonsterPickPockets(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pick pockets"))
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var chancePercent = Math.Clamp(25 + Math.Max(0, monster.Template.HitDice) * 5, 0, 100);
        var roll = _dice.Roll(100);
        var success = roll <= chancePercent;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Pick Pockets",
            $"{monster.DisplayName} pick pockets attempt (Player's Handbook p.28)",
            "Roll 1d100. Chance = 25% + Hit Dice x 5% (probability progression reference: PHB page 28).",
            "1",
            "100",
            roll.ToString(),
            $"Chance {chancePercent}%. {(success ? "Success" : "Failure")}.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to pick pockets but fails (roll {roll} vs {chancePercent}%)."));
            return false;
        }

        var victim = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var coinsStolen = TryStealCoins(victim, out var coinSummary);
        var itemStolen = TryStealInventoryItem(victim, out var stolenItemName);

        if (!coinsStolen && !itemStolen)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} succeeds at pick pockets but {victim.Name} has nothing to steal."));
            return false;
        }

        var effects = new List<string>();
        if (coinsStolen && !string.IsNullOrWhiteSpace(coinSummary))
            effects.Add(coinSummary!);
        if (itemStolen && !string.IsNullOrWhiteSpace(stolenItemName))
            effects.Add(stolenItemName!);

        events.Add(new CombatEvent($"{monster.DisplayName} steals from {victim.Name}: {string.Join("; ", effects)}."));

        var fleeingGroupId = monster.GroupId;
        session.NoXpMonsterGroupIds.Add(fleeingGroupId);
        var removedCount = session.Monsters.RemoveAll(m => string.Equals(m.GroupId, fleeingGroupId, StringComparison.OrdinalIgnoreCase));

        RuleApplicationInfo.Publish(
            "AD&D",
            "Pick Pockets Outcome",
            $"{monster.DisplayName} theft result",
            "A successful theft causes the encounter group to run away; no XP is awarded for that group.",
            null,
            null,
            null,
            $"Group {fleeingGroupId} fled after stealing: {string.Join(", ", effects)}.");

        events.Add(new CombatEvent($"Group {fleeingGroupId} runs away after the theft and leaves the battle ({removedCount} monster(s) gone)."));
        return true;
    }

    private bool TryResolveMonsterColdRadiation(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Cold Radiation"))
            return false;

        if (!session.CanMonsterUseColdRadiation(monster, session.RoundNumber))
            return false;

        var targets = SelectRandomPartyMembersInOneInchRange(session);
        if (targets.Count == 0)
            return false;

        session.MarkMonsterColdRadiationUsed(monster, session.RoundNumber);
        events.Add(new CombatEvent($"{monster.DisplayName} releases Cold Radiation! All within 1\" are affected ({targets.Count} random party member(s))."));

        foreach (var target in targets)
        {
            var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
            var appliedDamage = ApplyColdResistanceDamageReduction(target, rolledDamage, isMagicalCold: false, isNormalCold: true);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - appliedDamage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent(
                appliedDamage <= 0
                    ? $"{target.Name} is unaffected by the cold radiation due to Resist Cold (rolled {rolledDamage})."
                    : $"{target.Name} suffers {actual} cold radiation damage (rolled {rolledDamage}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0 && !target.HasStatus(CharacterStatus.Dead))
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by {monster.DisplayName}'s cold radiation!"));
            }
        }

        RuleApplicationInfo.Publish(
            "AD&D",
            "Cold Radiation",
            $"{monster.DisplayName} cold radiation burst",
            "All within 1” suffer 3-18 damage; in this project 1” affects 1d3 random party members. The attack can be used once every other melee round.",
            "3",
            "18",
            null,
            $"Affected {targets.Count} target(s): {string.Join(", ", targets.Select(t => t.Name))}.");

        return true;
    }

    private bool TryStealCoins(Character victim, out string? summary)
    {
        summary = null;

        var currencyGetters = new List<(string Label, Func<Character, int> Get, Action<Character, int> Set)>
        {
            ("gp", c => c.GoldPieces, (c, v) => c.GoldPieces = v),
            ("pp", c => c.PlatinumPieces, (c, v) => c.PlatinumPieces = v),
            ("ep", c => c.ElectrumPieces, (c, v) => c.ElectrumPieces = v),
            ("sp", c => c.SilverPieces, (c, v) => c.SilverPieces = v),
            ("cp", c => c.CopperPieces, (c, v) => c.CopperPieces = v)
        };

        var available = currencyGetters.Where(x => x.Get(victim) > 0).ToList();
        if (available.Count == 0)
            return false;

        var picked = available[_dice.Roll(available.Count) - 1];
        var current = picked.Get(victim);
        var amount = Math.Max(1, _dice.Roll(Math.Max(1, current)));
        amount = Math.Min(amount, current);
        picked.Set(victim, current - amount);
        summary = $"{amount} {picked.Label}";
        return true;
    }

    private bool TryStealInventoryItem(Character victim, out string? itemName)
    {
        itemName = null;
        if (victim.Inventory == null || victim.Inventory.Count == 0)
            return false;

        var idx = _dice.Roll(victim.Inventory.Count) - 1;
        var item = victim.Inventory[idx];
        victim.Inventory.RemoveAt(idx);
        itemName = item.Name;
        return true;
    }

    private static bool TryResolveMonsterLayOnHands(CombatSession session, MonsterInstance caster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(caster, "Lay on Hands"))
            return false;

        if (session.HasMonsterUsedLayOnHands(caster))
            return false;

        var target = session.Monsters
            .Where(m => m.IsAlive && m.CurrentHitPoints < m.MaxHitPoints)
            .OrderBy(m => m.CurrentHitPoints)
            .ThenBy(m => m.MaxHitPoints)
            .FirstOrDefault();

        if (target == null)
            return false;

        var healAmount = Math.Max(0, caster.Template.HitDice) * 2;
        if (healAmount <= 0)
            return false;

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;
        if (healed <= 0)
            return false;

        session.MarkMonsterLayOnHandsUsed(caster);
        events.Add(new CombatEvent($"{caster.DisplayName} uses Lay on Hands on {target.DisplayName} and heals {healed} HP. HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private bool TryResolveMonsterBodyHeal(CombatSession session, MonsterInstance monk, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monk, "BodyHeal"))
            return false;

        if (session.HasMonsterUsedBodyHeal(monk))
            return false;

        var monkLevel = Math.Max(0, monk.Template.HitDice);
        if (monkLevel < 7)
            return false;

        if (!monk.IsAlive || monk.CurrentHitPoints >= monk.MaxHitPoints)
            return false;

        var bonus = Math.Max(1, monkLevel - 6);
        var rolled = _dice.Roll(4);
        var healAmount = rolled + bonus;
        var before = monk.CurrentHitPoints;
        monk.CurrentHitPoints = Math.Min(monk.MaxHitPoints, monk.CurrentHitPoints + healAmount);
        var healed = monk.CurrentHitPoints - before;
        if (healed <= 0)
            return false;

        session.MarkMonsterBodyHealUsed(monk);
        events.Add(new CombatEvent(
            $"{monk.DisplayName} uses Body Heal and restores {healed} HP (1d4+{bonus}, rolled {rolled}). HP {before}->{monk.CurrentHitPoints}."));
        return true;
    }

    private static bool HasAnySpecialAbility(MonsterInstance monster, params string[] abilityNames)
    {
        return GetMonsterAbilityNames(monster)
            .Any(name => abilityNames.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ApplyMonsterRegeneration(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Regeneration"))
            return;

        if (session != null && session.GetMonsterWoundingActiveCount(monster) > 0)
            return;

        if (!monster.IsAlive || monster.CurrentHitPoints >= monster.MaxHitPoints)
            return;

        var perRound = GetMonsterRegenerationPerRound(monster);
        if (perRound <= 0)
            perRound = 1;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Min(monster.MaxHitPoints, monster.CurrentHitPoints + perRound);
        var healed = monster.CurrentHitPoints - before;

        if (healed > 0)
            events.Add(new CombatEvent($"{monster.DisplayName} regenerates {healed} HP. HP {before}->{monster.CurrentHitPoints}."));
    }

    private static int GetMonsterRegenerationPerRound(MonsterInstance monster)
    {
        if (monster?.Template?.SpecialDefenses == null)
            return 1;

        foreach (var defense in monster.Template.SpecialDefenses)
        {
            if (!string.Equals(defense?.Name?.Trim(), "Regeneration", StringComparison.OrdinalIgnoreCase))
                continue;

            var description = defense.Description ?? string.Empty;
            var m = Regex.Match(description, @"regenerates\s+(?<n>\d+)\s*(?:hit\s*points|hp)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups["n"].Value, out var parsed) && parsed > 0)
                return parsed;
        }

        return 1;
    }

    private static void ApplySwordOfWoundingOngoingDamage(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session == null || monster?.Template == null || !monster.IsAlive)
            return;

        var activeWounds = session.GetMonsterWoundingActiveCount(monster);
        if (activeWounds <= 0)
            return;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - activeWounds);
        var actual = before - monster.CurrentHitPoints;

        session.AddMonsterWoundingUnhealableDamage(monster, actual);
        var remainingWounds = session.TickMonsterWounding(monster);

        events.Add(new CombatEvent($"{monster.DisplayName} bleeds from Sword of Wounding for {actual} damage ({activeWounds} wound stack(s)). HP {before}->{monster.CurrentHitPoints}."));

        if (remainingWounds <= 0)
            events.Add(new CombatEvent($"Sword of Wounding bleed effect ends on {monster.DisplayName}."));

        if (monster.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{monster.DisplayName} succumbs to Sword of Wounding blood loss."));
    }

    private bool TryResolveConfusedMonsterTurn(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var roundsRemaining = monster.GetStatusRounds(MonsterStatus.Confused);
        if (roundsRemaining <= 0)
            return false;

        // Save vs spell each round at -2.
        var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = _dice.Roll(20);
        var effectiveRoll = Math.Max(1, saveRoll - 2);

        if (saveTarget > 0 && effectiveRoll >= saveTarget)
        {
            monster.SetStatus(MonsterStatus.Confused, 0);
            events.Add(new CombatEvent($"{monster.DisplayName} shakes off confusion (save {saveRoll}-2={effectiveRoll} vs {saveTarget})."));
            return false;
        }

        var actionRoll = _dice.Roll(100);
        if (actionRoll <= 10)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} wanders away in confusion and cannot act."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 60)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} stands confused and does nothing."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 80)
        {
            ResolveConfusedAttackNearestCreature(session, monster, events);
            TickConfusedDuration(monster, events);
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} turns on the druid's side in confusion!"));
        TickConfusedDuration(monster, events);
        return false;
    }

    private void TickConfusedDuration(MonsterInstance monster, List<CombatEvent> events)
    {
        var remaining = monster.TickStatus(MonsterStatus.Confused);
        if (remaining <= 0)
            events.Add(new CombatEvent($"{monster.DisplayName} is no longer confused."));
    }

    private void ResolveConfusedAttackNearestCreature(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var targetMonster = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, monster))
            .OrderBy(m => string.Equals(m.GroupId, monster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .FirstOrDefault();

        if (targetMonster == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} lashes out wildly but finds no nearby creature."));
            return;
        }

        ResolveMonsterAttackAgainstMonster(
            attacker: monster,
            target: targetMonster,
            events: events,
            missText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion but misses.",
            hitText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion",
            slainText: $"{targetMonster.DisplayName} is slain by the confused attack!");
    }

    private void ResolveCharmedMonsterTurn(CombatSession session, MonsterInstance charmedMonster, List<CombatEvent> events)
    {
        var hostileMonsters = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, charmedMonster))
            .Where(m => !m.HasStatus(MonsterStatus.Charmed))
            .OrderBy(m => string.Equals(m.GroupId, charmedMonster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .ToList();

        if (hostileMonsters.Count == 0)
        {
            events.Add(new CombatEvent($"{charmedMonster.DisplayName} is charmed and does nothing."));
            return;
        }

        var target = hostileMonsters[0];
        ResolveMonsterAttackAgainstMonster(
            attacker: charmedMonster,
            target: target,
            events: events,
            missText: $"{charmedMonster.DisplayName} fights for the Bard and misses {target.DisplayName}.",
            hitText: $"{charmedMonster.DisplayName} fights for the Bard and hits {target.DisplayName}",
            slainText: $"{target.DisplayName} is slain by the charmed ally!");
    }

    private void ResolveCharmedCharacterTurn(CombatSession session, Character charmedCharacter, List<CombatEvent> events)
    {
        var partyTargets = session.ExposedParty
            .Where(IsAlive)
            .Where(c => !ReferenceEquals(c, charmedCharacter))
            .ToList();

        if (partyTargets.Count == 0)
        {
            events.Add(new CombatEvent($"{charmedCharacter.Name} is charmed and has no ally to attack."));
            return;
        }

        var target = partyTargets[_dice.Roll(partyTargets.Count) - 1];
        ResolveCharacterAttackAgainstCharacter(
            attacker: charmedCharacter,
            target: target,
            events: events);
    }

    private void ResolveCharacterAttackAgainstCharacter(Character attacker, Character target, List<CombatEvent> events)
    {
        var mainHand = attacker.Equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) ? equipped : null;
        var attackName = mainHand?.Name ?? "Unarmed strike";
        var damageExpression = !string.IsNullOrWhiteSpace(mainHand?.Damage) ? mainHand!.Damage : "1d2";

        var thac0Modifier = attacker.Thac0StrengthModifier;
        if (mainHand != null)
            thac0Modifier += Math.Max(0, mainHand.ToHitBonus);

        var needed = Math.Clamp((attacker.Thac0 - thac0Modifier) - target.ArmorClass, 1, 20);
        var roll = _dice.Roll(20);
        if (roll < needed)
        {
            events.Add(new CombatEvent($"{attacker.Name} is charmed and attacks {target.Name} with {attackName}, but misses (rolled {roll} vs {needed})."));
            return;
        }

        var damage = RollDamage(damageExpression)
                     + attacker.DamageStrengthModifier
                     + (mainHand == null ? 0 : attacker.MainHandItemDamageModifier);
        damage = Math.Max(1, damage);

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actualDamage = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actualDamage, events);

        events.Add(new CombatEvent($"{attacker.Name} is charmed and attacks {target.Name} with {attackName} for {actualDamage} damage. HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0 && !target.HasStatus(CharacterStatus.Dead))
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain by the charmed attack!"));
        }
    }

    private void ResolveMonsterAttackAgainstMonster(
        MonsterInstance attacker,
        MonsterInstance target,
        List<CombatEvent> events,
        string missText,
        string hitText,
        string slainText)
    {
        var attack = attacker.Template.Attacks.FirstOrDefault()
                     ?? new MonsterAttack { Name = "Claw", NumberOfAttacks = 1, Damage = "1d4" };

        var thac0 = GetMonsterThac0(attacker);
        var needed = Math.Clamp(thac0 - target.ArmorClass, 1, 20);
        var roll = _dice.Roll(20);

        if (roll < needed)
        {
            events.Add(new CombatEvent(missText));
            return;
        }

        var damage = RollDamage(string.IsNullOrWhiteSpace(attack.Damage) ? "1d4" : attack.Damage);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeMonsterIfAsleepAfterDamage(target, actual, events);

        events.Add(new CombatEvent($"{hitText} for {actual} damage. HP {before}->{target.CurrentHitPoints}."));
        if (!target.IsAlive)
            events.Add(new CombatEvent(slainText));
    }

    private bool TryResolveMonsterAssassination(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session.RoundNumber != 1 || !session.PartySurprisedRound1)
            return false;

        var isAssassinByName = monster.Template.Name?.IndexOf("assassin", StringComparison.OrdinalIgnoreCase) >= 0;
        var isAssassinByAbility = HasAnySpecialAbility(monster, "Assassination", "Assassin", "Assassin Attack");
        if (!isAssassinByName && !isAssassinByAbility)
            return false;

        var target = SelectMonsterTarget(session);
        if (target == null)
            return false;

        var assassination = new AssassinationService("Data/Assassination");
        var assassinLevel = Math.Max(1, monster.Template.HitDice);
        var victimLevel = Math.Max(1, target.Level);
        var assassinationRoll = assassination.RollAssassination(assassinLevel, victimLevel, _rng);

        RuleApplicationInfo.Publish(
            "DMG",
            "75Assassin",
            $"{monster.DisplayName} assassination attempt vs {target.Name}",
            "Use assassin level vs victim level/hit dice table; roll 1d100 and succeed if roll is less than or equal to chance.",
            "1",
            "100",
            assassinationRoll.Roll.ToString(),
            $"Assassin level {assassinLevel}, target level {victimLevel}, chance {assassinationRoll.ChancePercent}% => {(assassinationRoll.Success ? "success" : "failure")}.");

        if (!assassinationRoll.Success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} attempts to assassinate {target.Name} but fails."));
            return true;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{monster.DisplayName} assassinates {target.Name} instantly!"));
        return true;
    }

    private void TryApplyMonsterParalyzation(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Monster Paralyzation",
            $"{monster.DisplayName} paralyzation attack on {target.Name}",
            "Target rolls saving throw vs Paralyzation/Poison/Death. On failed save, paralysis duration is 30-120 rounds (until dungeon exit if still active).",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists paralysis (save {saveRoll} vs {saveTarget})."));
            return;
        }

        if (target.HasStatus(CharacterStatus.Paralyzed))
        {
            events.Add(new CombatEvent($"{target.Name} is already paralyzed by {monster.DisplayName}."));
            return;
        }

        var rounds = _dice.Roll(91) + 29;
        target.ApplyParalysis(rounds);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Paralyzation Duration",
            $"{target.Name} paralysis duration",
            "Roll determines paralysis duration in rounds (30-120). Every combat round and each dungeon step counts as one round.",
            "30",
            "120",
            rounds.ToString(),
            "Character is paralyzed until duration expires or they leave the dungeon.");

        events.Add(new CombatEvent($"{target.Name} is paralyzed by {monster.DisplayName} for {rounds} round(s)! (save {saveRoll} vs {saveTarget})"));
    }

    private static int GetWebEscapeRoundsFromStrength(Character target)
    {
        var strength = Math.Clamp(target.Abilities.Strength, 1, 18);
        return Math.Clamp(19 - strength, 1, 18);
    }

    private static void TryBurnAwayWebsFromFireDamage(Character target, string source, CombatSession session, List<CombatEvent> events)
    {
        if (!target.HasStatus(CharacterStatus.Webbed))
            return;

        target.RemoveStatus(CharacterStatus.Webbed);
        session.SetPartyWebbed(target.Name, 0);
        events.Add(new CombatEvent($"{target.Name}'s webs burn away from {source}."));
    }

    private static void TryApplyWebsSpecialAttack(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Webs"))
            return;

        if (target.HasStatus(CharacterStatus.Webbed))
            return;

        var rounds = GetWebEscapeRoundsFromStrength(target);
        target.AddStatus(CharacterStatus.Webbed);
        session.SetPartyWebbed(target.Name, rounds);

        events.Add(new CombatEvent($"{monster.DisplayName} entangles {target.Name} in sticky webs. STR {Math.Clamp(target.Abilities.Strength, 1, 18)} breaks free in {rounds} round(s)."));
    }

    private void TryApplyPetrificationFromSpecialAttacks(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Petrifying Gaze", "Petrification Touch"))
            return;

        if (target.HasStatus(CharacterStatus.Petrified))
            return;

        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.PetrificationPolymorph);
        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        var attackName = HasAnySpecialAbility(monster, "Petrification Touch") ? "Petrification Touch" : "Petrifying Gaze";

        RuleApplicationInfo.Publish(
            "AD&D",
            "Petrification",
            $"{monster.DisplayName} {attackName} on {target.Name}",
            "Target rolls saving throw vs Petrification/Polymorph. On failed save, target turns to stone until cured.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists petrification (save {saveRoll} vs {saveTarget})."));
            return;
        }

        target.AddStatus(CharacterStatus.Petrified);
        events.Add(new CombatEvent($"{target.Name} is petrified by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})"));
    }

    private bool TryApplyPoisonSpit(MonsterInstance monster, Character target, List<CombatEvent> events, bool chantActiveAtRoundStart, bool prayerActiveAtRoundStart)
    {
        if (!HasSpecialAbility(monster, "Poison spit"))
            return false;

        if (!IsAlive(target))
            return false;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to poison spit."));
            return false;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        var saveMessage = $"{monster.DisplayName} poison spit vs {target.Name}: save {saveRoll} vs {saveTarget} => {(failedSave ? "failed" : "succeeded")}.";
        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", saveMessage);

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poison spit (save {saveRoll} vs {saveTarget})."));
            return false;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{target.Name} is killed by {monster.DisplayName}'s poison spit! (save {saveRoll} vs {saveTarget})"));
        return true;
    }

    private bool TryApplyConstrictionDamage(CombatSession session, Character member, List<CombatEvent> events)
    {
        var effects = session.GetConstrictionsForVictim(member.Name);
        if (effects.Count == 0)
            return false;

        var strongHelpers = session.ExposedParty
            .Where(IsAlive)
            .Where(c => !string.Equals(c.Name, member.Name, StringComparison.OrdinalIgnoreCase))
            .Count(c => c.Abilities.Strength >= 16);

        foreach (var effect in effects.ToList())
        {
            if (!effect.Constrictor.IsAlive || effect.Constrictor.HasFled)
            {
                session.RemoveConstriction(effect.Constrictor, effect.Victim);
                events.Add(new CombatEvent($"{member.Name} is freed as {effect.Constrictor.DisplayName} can no longer maintain constriction."));
                continue;
            }

            var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - rolledDamage);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);
            events.Add(new CombatEvent($"{effect.Constrictor.DisplayName} constricts {member.Name} for {actual} damage (2d4). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} is crushed to death by constriction!"));
                return true;
            }

            if (strongHelpers >= 4)
            {
                effect.UncoilRoundsRemaining ??= _dice.Roll(4) + 1;
                effect.UncoilRoundsRemaining = Math.Max(0, effect.UncoilRoundsRemaining.Value - 1);
                var remaining = effect.UncoilRoundsRemaining.Value;
                if (remaining <= 0)
                {
                    session.RemoveConstriction(effect.Constrictor, effect.Victim);
                    events.Add(new CombatEvent($"{member.Name} is uncoiled by strong allies after sustained effort."));
                }
                else
                {
                    events.Add(new CombatEvent($"Strong allies are uncoiling {effect.Constrictor.DisplayName}; {remaining} round(s) remain."));
                }
            }
        }

        return false;
    }

    private void TryApplyConstrictionOnHit(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Constriction"))
            return;

        if (!IsAlive(target))
            return;

        if (session.HasActiveConstriction(monster, target))
            return;

        session.StartConstriction(monster, target);
        events.Add(new CombatEvent($"{monster.DisplayName} wraps around {target.Name} and begins constriction (2-8 damage each melee round)."));
        RuleApplicationInfo.Publish(
            "AD&D",
            "Constriction",
            $"{monster.DisplayName} constricts {target.Name}",
            "Constriction inflicts 2-8 damage per melee round. If at least four strong creatures (e.g., Str 16+) grasp head and tail, they can uncoil it in 2-5 melee rounds.",
            "2",
            "8",
            null,
            $"{target.Name} is now constricted.");
    }

    private bool TryApplyPoisonSpores(MonsterInstance monster, Character target, List<CombatEvent> events, bool chantActiveAtRoundStart, bool prayerActiveAtRoundStart)
    {
        if (!HasSpecialAbility(monster, "Poison Spores"))
            return false;

        if (!IsAlive(target))
            return false;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to poison spores."));
            return false;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Poison Spores",
            $"{monster.DisplayName} poison spores vs {target.Name}",
            "Victim must save vs poison or die. Victims killed by poison spores require Cure Disease and Resurrection/Raise Dead within 24 hours.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poison spores (save {saveRoll} vs {saveTarget})."));
            return false;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        target.ApplyPoisonSporesDeathCurse(daysUntilRaiseImpossible: 1);
        events.Add(new CombatEvent($"{target.Name} is killed by {monster.DisplayName}'s poison spores! Cure Disease and Resurrection/Raise Dead must both be completed within 24 hours. (save {saveRoll} vs {saveTarget})"));
        return true;
    }

    private void TryApplyGiantRatDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        var isGiantRat = string.Equals(monster.Template.Name, "Giant Rat", StringComparison.OrdinalIgnoreCase);
        if (!isGiantRat && !HasAnySpecialAbility(monster, "Giant Rat Disease"))
            return;

        var diseaseRoll = _dice.Roll(100);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Giant Rat Disease",
            $"{monster.DisplayName} disease check after hit on {target.Name}",
            "Roll 1d100. Only 1-5 causes disease (5% chance).",
            "1",
            "100",
            diseaseRoll.ToString(),
            diseaseRoll <= 5
                ? "Result is within 1-5, disease is contracted."
                : "Result is outside 1-5, no disease.");

        if (diseaseRoll <= 5)
        {
            if (!target.HasStatus(CharacterStatus.Diseased))
            {
                target.ApplyDisease();
                events.Add(new CombatEvent($"{target.Name} is diseased by {monster.DisplayName}! (1d100 roll: {diseaseRoll}, needs 1-5)"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} carries Giant Rat Disease, but {target.Name} is already diseased. (1d100 roll: {diseaseRoll}, needs 1-5)"));
            }

            return;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} carries Giant Rat Disease. Infection roll: {diseaseRoll} on 1d100 (needs 1-5)."));
    }

    private static void TryApplyRotGrubExposure(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!string.Equals(monster.Template.Name, "Rot Grub", StringComparison.OrdinalIgnoreCase))
            return;

        if (target.RotGrubFlamePromptPending || target.RotGrubDeathRoundsRemaining > 0)
            return;

        target.MarkRotGrubExposurePendingFlame();
        events.Add(new CombatEvent($"ROT_GRUB_PROMPT::{target.Name}"));
    }

    private static void TryApplyEarSeekerDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!string.Equals(monster.Template.Name, "Ear Seeker", StringComparison.OrdinalIgnoreCase))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}."));
            return;
        }

        if (target.EarSeekerDeathOnNextDungeonEntry)
            return;

        target.ApplyEarSeekerDisease();
        events.Add(new CombatEvent($"{target.Name} contracts Ear Seeker disease from {monster.DisplayName}! They will die upon next dungeon entry unless cured."));
    }

    private static void TryApplyInfestation(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Infestation"))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to infestation disease from {monster.DisplayName}."));
            return;
        }

        if (target.InfestationDeathDaysRemaining > 0)
            return;

        target.ApplyInfestationDisease();
        events.Add(new CombatEvent($"{target.Name} suffers infestation by {monster.DisplayName}: rhizomes penetrate flesh. Cure Disease is required within 24 hours or death will occur."));
    }

    private void TryApplyTickDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Tick Disease"))
            return;

        var diseaseRoll = _dice.Roll(100);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Tick Disease",
            $"{monster.DisplayName} tick disease check after hit on {target.Name}",
            "Roll 1d100. Result 1-50 means fatal tick disease is contracted.",
            "1",
            "100",
            diseaseRoll.ToString(),
            diseaseRoll <= 50
                ? "Result is within 1-50, tick disease is contracted."
                : "Result is outside 1-50, no tick disease.");

        if (diseaseRoll > 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} carries Tick Disease. Infection roll: {diseaseRoll} on 1d100 (needs 1-50)."));
            return;
        }

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}. (Tick Disease roll: {diseaseRoll} on 1d100, needs 1-50)"));
            return;
        }

        if (target.TickDiseaseDeathDaysRemaining > 0)
        {
            events.Add(new CombatEvent($"{target.Name} is hit by {monster.DisplayName}, but Tick Disease is already active ({target.TickDiseaseDeathDaysRemaining} day(s) until death unless cured)."));
            return;
        }

        var deathInDays = _dice.Roll(7) + 1;
        target.ApplyTickDisease(deathInDays);
        events.Add(new CombatEvent($"{target.Name} contracts Tick Disease from {monster.DisplayName}! Fatal disease will kill in {deathInDays} day(s) unless cured (infection roll: {diseaseRoll} on 1d100, needs 1-50)."));
    }

    private void TryApplyRottingDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Rotting Disease"))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}."));
            return;
        }

        if (target.RottingDiseaseMonthsRemaining > 0)
        {
            events.Add(new CombatEvent($"{target.Name} is hit by {monster.DisplayName}, but Rotting Disease is already active ({target.RottingDiseaseMonthsRemaining} month(s) until death unless cured)."));
            return;
        }

        var deathInMonths = _dice.Roll(6);
        target.ApplyRottingDisease(deathInMonths);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Rotting Disease",
            $"{monster.DisplayName} rotting disease check after hit on {target.Name}",
            "On a successful hit, victim contracts rotting disease and dies in 1-6 months unless Cure Disease is applied.",
            "1",
            "6",
            deathInMonths.ToString(),
            $"Victim loses 2 CHA per month and dies in {deathInMonths} month(s) unless cured.");
        events.Add(new CombatEvent($"{target.Name} contracts Rotting Disease from {monster.DisplayName}: loses 2 CHA per month and dies in {deathInMonths} month(s) unless cured."));
    }

    private static void TryApplyMummyRotOnDeath(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!SpellDamageSaveHelper.IsMummy(monster))
            return;

        target.ApplyMummyRotDeathCurse(roundsUntilRaiseImpossible: 60);
        events.Add(new CombatEvent($"{target.Name}'s corpse begins to rot from mummy contagion. Cure Disease and Raise Dead must both be completed within 6 turns or raising becomes impossible."));
    }

    private static void ApplyMummyRotRaiseWindowRoundProgress(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (member.MummyRotRaiseWindowRoundsRemaining <= 0)
                continue;

            if (!member.HasStatus(CharacterStatus.Dead)
                && !member.HasStatus(CharacterStatus.Ashes)
                && member.CurrentHitPoints > 0)
            {
                continue;
            }

            var remaining = member.TickMummyRotRaiseWindowRound();
            if (remaining == 0)
            {
                events.Add(new CombatEvent($"{member.Name}'s mummy-rot revival window closes. Raise Dead can no longer restore this character."));
            }
        }
    }

    private void TryApplyLycanthropy(MonsterInstance monster, Character target, string? attackName, int biteDamage, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Lycanthropy"))
            return;

        if (string.IsNullOrWhiteSpace(attackName)
            || !attackName.Contains("bite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isHumanoidVictim = target.Race is Race.Human or Race.Elf or Race.Dwarf or Race.Gnome or Race.Halfling or Race.HalfElf or Race.HalfOrc;
        if (!isHumanoidVictim)
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to lycanthropy disease from {monster.DisplayName}."));
            return;
        }

        var hpThreshold = (int)Math.Ceiling(Math.Max(1, target.MaxHitPoints) * 0.5);
        if (biteDamage < hpThreshold)
            return;

        if (target.LycanthropyInfected)
        {
            events.Add(new CombatEvent($"{target.Name} is bitten by {monster.DisplayName}, but lycanthropy infection is already active."));
            return;
        }

        var onsetDays = _dice.Roll(8) + 6; // 7-14
        var sourceType = string.IsNullOrWhiteSpace(monster.Template.Name) ? "Lycanthrope" : monster.Template.Name;
        var sourceAlignment = ParseAlignmentOrFallback(monster.Template.Alignment, target.Alignment);
        target.InfectWithLycanthropy(sourceType, sourceAlignment, onsetDays);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Lycanthropy",
            $"{monster.DisplayName} lycanthropy check after bite on {target.Name}",
            "A humanoid bitten for at least 50% of max HP is infected. Cure Disease from cleric level 12+ must occur within 3 days. Otherwise infection becomes permanent, and onset occurs in 7-14 days.",
            hpThreshold.ToString(),
            target.MaxHitPoints.ToString(),
            biteDamage.ToString(),
            $"Bite damage {biteDamage} >= threshold {hpThreshold}. Infection applied. Onset in {onsetDays} day(s). Cure window: 3 day(s)."
        );

        events.Add(new CombatEvent($"{target.Name} is infected with lycanthropy by {monster.DisplayName}! Bite damage {biteDamage} (threshold {hpThreshold}). Cure Disease from cleric level 12+ is required within 3 days or it becomes permanent. Onset in {onsetDays} day(s)."));
    }

    private static Alignment ParseAlignmentOrFallback(string? text, Alignment fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        var normalized = text.Trim().ToLowerInvariant();
        return normalized switch
        {
            "lawful good" => Alignment.LawfulGood,
            "neutral good" => Alignment.NeutralGood,
            "chaotic good" => Alignment.ChaoticGood,
            "lawful neutral" => Alignment.LawfulNeutral,
            "true neutral" or "neutral" => Alignment.TrueNeutral,
            "chaotic neutral" => Alignment.ChaoticNeutral,
            "lawful evil" => Alignment.LawfulEvil,
            "neutral evil" => Alignment.NeutralEvil,
            "chaotic evil" => Alignment.ChaoticEvil,
            _ => fallback
        };
    }

    private void TryApplyEnergyDrain(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Energy Drain"))
            return;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Energy Drain",
            $"{monster.DisplayName} energy drain attack on {target.Name}",
            "Target rolls saving throw vs Death. On failed save, target loses 1 level.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save: lose 1 level." : "Successful save: no level loss.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists energy drain (save {saveRoll} vs {saveTarget})."));
            return;
        }

        if (!TryDrainOneLevel(target, out var oldLevel, out var newLevel))
        {
            events.Add(new CombatEvent($"{target.Name} fails save vs energy drain ({saveRoll} vs {saveTarget}) but cannot lose levels below 1."));
            return;
        }

        events.Add(new CombatEvent($"{target.Name} fails save vs energy drain ({saveRoll} vs {saveTarget}) and loses 1 level ({oldLevel}->{newLevel})."));
    }

    private static bool TryDrainOneLevel(Character target, out int oldLevel, out int newLevel)
    {
        target.EnsureClassProgressions();

        var primaryClass = target.Classes.Count > 0 ? target.Classes[0] : target.Class;
        var progression = target.ClassProgressions.FirstOrDefault(p => p.Class == primaryClass);
        if (progression == null)
        {
            oldLevel = target.Level;
            newLevel = target.Level;
            return false;
        }

        oldLevel = Math.Max(1, progression.Level);
        if (oldLevel <= 1)
        {
            newLevel = oldLevel;
            return false;
        }

        newLevel = oldLevel - 1;
        progression.Level = newLevel;
        progression.Experience = Math.Min(
            progression.Experience,
            Math.Max(0, ExperienceTable.GetThresholdForLevel(primaryClass, newLevel)));

        if (target.Classes.Count <= 1)
        {
            target.Level = newLevel;
            target.Experience = progression.Experience;
        }
        else
        {
            target.Level = Math.Max(1, target.GetClassLevel(primaryClass));
            target.Experience = Math.Max(0, target.ClassProgressions.Sum(x => x.Experience));
        }

        ApplyBasicWarriorAttackProgressionAfterLevelDrain(target, newLevel);
        target.RefreshMoveFromArmorAndClass();
        target.RefreshMonkProgressionStats();
        if (!GameRulesProvider.Current.PlayerSelectsWeaponProficiencies)
            WeaponProficiencyRules.EnsureAutoProficiencies(target);

        return true;
    }

    private void TryApplyStrengthDrain(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Strength Drain"))
            return;

        if (!IsAlive(target) || target.CurrentHitPoints <= 0)
            return;

        var drainAmount = 1;
        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var actualDrain = target.ApplyStrengthDrainUntilDungeonExit(drainAmount);
        var afterStrength = Math.Max(0, target.Abilities.Strength);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Strength Drain",
            $"{monster.DisplayName} strength drain on {target.Name}",
            "On hit, victim loses 1 Strength. If Strength reaches 0, victim dies. Original Strength returns on dungeon exit.",
            "1",
            "1",
            drainAmount.ToString(),
            $"Strength {beforeStrength}->{afterStrength} (drained {actualDrain}).");

        events.Add(new CombatEvent($"{target.Name} suffers Strength Drain from {monster.DisplayName}: STR {beforeStrength}->{afterStrength} (drained {actualDrain})."));

        if (target.Abilities.Strength > 0)
            return;

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{target.Name} is reduced to 0 Strength and dies!"));
    }

    private void TryApplyDestroyMetalOnMonsterHit(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Destroy Metal"))
            return;

        var equipmentSlots = new[]
        {
            EquipmentSlot.MainHand,
            EquipmentSlot.OffHand,
            EquipmentSlot.Body,
            EquipmentSlot.Head,
            EquipmentSlot.Hands
        };

        var metalTargets = equipmentSlots
            .Select(slot => (slot, item: target.Equipment.TryGetValue(slot, out var i) ? i : null))
            .Where(x => x.item != null && IsMetalItem(x.item))
            .ToList();

        if (metalTargets.Count == 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} brushes {target.Name} with rusting antennae, but no exposed metal is affected."));
            return;
        }

        var selected = metalTargets[_dice.Roll(metalTargets.Count) - 1];
        var item = selected.item!;

        if (IsMagicalItem(item))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{item.Name} saving throw vs rust/corrosion",
                "Magical metal item touched by rust effect gets a saving throw; success resists corrosion.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); item resists corrosion."
                    : $"Save failed ({saveRoll} vs {saveTarget}); item corrodes.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name}'s {item.Name} resists corrosion (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(target, item, events, $"{monster.DisplayName} corrodes {target.Name}'s {item.Name}; it rusts away instantly!");
    }

    private void TryApplyDissolveWoodOnMonsterHit(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Dissolve Wood"))
            return;

        var woodenItems = new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Range }
            .Select(slot => target.Equipment.TryGetValue(slot, out var i) ? i : null)
            .Where(i => i != null && IsWoodenItem(i))
            .Distinct()
            .ToList();

        if (woodenItems.Count == 0)
            return;

        var item = woodenItems[_dice.Roll(woodenItems.Count) - 1]!;

        if (IsMagicalItem(item))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{item.Name} saving throw vs dissolving",
                "Magical wooden item touched by a wood-dissolving creature gets a saving throw; success resists.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); item resists dissolving."
                    : $"Save failed ({saveRoll} vs {saveTarget}); item dissolves.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name}'s {item.Name} resists dissolving (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(target, item, events, $"{monster.DisplayName} dissolves {target.Name}'s {item.Name}!");
    }

    private static readonly string[] WoodenItemKeywords =
    {
        "wood", "staff", "stick", "club", "spear", "javelin", "lance", "pike", "trident",
        "halberd", "bardiche", "bec de corbin", "guisarme", "fauchard", "fork", "glaive",
        "voulge", "partisan", "ranseur", "spetum", "lucern", "pole",
        "bow", "crossbow", "sling", "axe", "hammer", "flail", "morning star", "pick", "quarterstaff"
    };

    private static bool IsWoodenItem(Item? item)
    {
        if (item == null)
            return false;

        if (item.Type == ItemType.Shield)
            return $"{item.Name} {item.Description}".Contains("wood", StringComparison.OrdinalIgnoreCase);

        if (item.Type != ItemType.Weapon)
            return false;

        var name = item.Name.ToLowerInvariant();
        if (name.Contains("sword", StringComparison.Ordinal) || name.Contains("dagger", StringComparison.Ordinal))
            return false;

        if (item.Description.Contains("wood", StringComparison.OrdinalIgnoreCase))
            return true;

        return WoodenItemKeywords.Any(k => name.Contains(k, StringComparison.Ordinal));
    }

    private void TryApplyHitOnMonsterDestroysMetal(Character attacker, MonsterInstance target, Item? strikingWeapon, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(target, "Hit on Monster Destroys Metal"))
            return;

        if (strikingWeapon == null || !IsMetalItem(strikingWeapon))
            return;

        if (IsMagicalItem(strikingWeapon))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{strikingWeapon.Name} saving throw vs rusting hide",
                "Magical metal weapon that strikes a rusting monster gets a saving throw to avoid corrosion.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); weapon resists corrosion."
                    : $"Save failed ({saveRoll} vs {saveTarget}); weapon corrodes.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{attacker.Name}'s {strikingWeapon.Name} resists corrosion (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(attacker, strikingWeapon, events,
            $"{attacker.Name}'s {strikingWeapon.Name} corrodes away after striking {target.DisplayName}!");
    }

    private static bool IsMagicalItem(Item item)
    {
        if (item == null)
            return false;

        if (item.MagicBonus > 0 || item.ToHitBonus > 0)
            return true;

        return item.SpecialAbilities != null && item.SpecialAbilities.Count > 0;
    }

    private static bool IsMetalItem(Item? item)
    {
        if (item == null)
            return false;

        var type = item.Type;
        if (type != ItemType.Weapon && type != ItemType.Armor && type != ItemType.Shield)
            return false;

        var text = $"{item.Name} {item.Description}".ToLowerInvariant();
        if (text.Contains("wood", StringComparison.Ordinal)
            || text.Contains("leather", StringComparison.Ordinal)
            || text.Contains("cloth", StringComparison.Ordinal)
            || text.Contains("bone", StringComparison.Ordinal)
            || text.Contains("stone", StringComparison.Ordinal)
            || text.Contains("hide", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static void DestroyEquippedItem(Character target, Item item, List<CombatEvent> events, string message)
    {
        foreach (var slot in target.Equipment
                     .Where(kv => kv.Value != null && ReferenceEquals(kv.Value, item))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            EquipmentManager.Unequip(target, slot);
        }

        target.Inventory.RemoveAll(i => ReferenceEquals(i, item));

        events.Add(new CombatEvent(message));
    }

    private static void ApplyBasicWarriorAttackProgressionAfterLevelDrain(Character target, int effectiveLevel)
    {
        if (target.Classes.Count == 0)
            return;

        var primary = target.Classes[0];
        if (primary is CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger)
        {
            if (effectiveLevel < 7)
                target.NumberOfAttacks = 1f;
            else if (effectiveLevel < 13)
                target.NumberOfAttacks = 1.5f;
            else
                target.NumberOfAttacks = 2f;

            return;
        }

        if (!target.IsMonk())
            target.NumberOfAttacks = 1f;
    }

    private static void TryApplyDrainBlood(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!(HasAnySpecialAbility(monster, "Stirge Blood drain") || HasAnySpecialAbility(monster, "Tick Blood Drain")))
            return;

        if (!IsAlive(target) || target.CurrentHitPoints <= 0)
            return;

        var remaining = session.GetPartyDrainBloodRemaining(target.Name);
        if (remaining <= 0)
        {
            var isTickDrain = HasAnySpecialAbility(monster, "Tick Blood Drain");
            var effectName = isTickDrain ? "Tick Blood Drain" : "Stirge Blood drain";
            var dieSides = isTickDrain ? 6 : 4;
            var totalToDrain = isTickDrain ? Math.Max(1, monster.MaxHitPoints) : 12;

            session.SetPartyDrainBlood(target.Name, totalToDrain, dieSides, effectName);
            events.Add(new CombatEvent($"{target.Name} is afflicted by {effectName} from {monster.DisplayName}! {totalToDrain} HP will be drained over subsequent rounds (1-{dieSides} per round)."));
            return;
        }

        var currentEffectName = session.GetPartyDrainBloodEffectName(target.Name);
        events.Add(new CombatEvent($"{target.Name} is hit again by {monster.DisplayName}, but {currentEffectName} is already active ({remaining} HP remaining to drain)."));
    }

    private static bool IsShrieker(MonsterInstance monster)
    {
        return string.Equals(monster.Template.Name, "Shrieker", StringComparison.OrdinalIgnoreCase)
               || HasSpecialAbility(monster, "Shriek");
    }

    private static bool IsPiercer(MonsterInstance monster)
    {
        return monster.Template.Name.StartsWith("Piercer", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveMonsterExplosionOnHit(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Explosion"))
            return false;

        if (!monster.IsAlive)
            return false;

        var isGasSpore = string.Equals(monster.Template.Name, "Gas Spore", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(monster.Template.TypeName, "Gas Spore", StringComparison.OrdinalIgnoreCase);

        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(6);

        monster.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{monster.DisplayName} explodes violently!"));

        foreach (var member in session.ExposedParty.ToList())
        {
            var saveThrowType = isGasSpore ? SaveThrowType.RodStaffWand : SaveThrowType.Spell;
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                member,
                _savingThrowService.GetSaveTarget(member, saveThrowType));
            var fireSaveBonus = GetFireResistanceSaveBonus(member);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);

            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(member, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{member.Name} is protected from the fiery explosion."));
                continue;
            }

            var applied = saveRoll >= saveTarget
                ? (isGasSpore ? (preSaveDamage / 2) : Math.Max(1, preSaveDamage / 2))
                : preSaveDamage;

            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - applied);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{member.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half explosion damage: {actual}. HP {before}->{member.CurrentHitPoints}."
                : $"{member.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} explosion damage. HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} is slain by the explosion!"));
            }
        }

        return true;
    }

    private static bool HasRingOfFireResistance(Character target)
    {
        if (target.Equipment == null)
            return false;

        static bool IsRingOfFireResistance(Item? item)
            => item != null
               && string.Equals(item.Name, "Ring of Fire Resistance", StringComparison.OrdinalIgnoreCase);

        target.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1);
        target.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2);
        var hasRing = IsRingOfFireResistance(ring1) || IsRingOfFireResistance(ring2);
        if (hasRing)
            return true;

        target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand);
        return IsSwordPlusThreeFrostBrand(mainHand);
    }

    private static int GetFireResistanceSaveBonus(Character target)
    {
        var saveBonus = Math.Max(target.FireProtectionSaveBonusVsFire, target.PotionFireResistanceSaveBonusVsFire);
        if (HasRingOfFireResistance(target))
            saveBonus = Math.Max(saveBonus, 4);

        return saveBonus;
    }

    private static int ApplyFireProtectionDamageReduction(Character target, int incomingDamage, int damageDiceCount, bool isMagicalFire, bool isNormalFire)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        var hasSpellProtection = target.HasActiveProtectionFromFire;
        var hasPotionProtection = target.HasActivePotionFireResistance;
        var hasRingProtection = HasRingOfFireResistance(target);
        if (!hasSpellProtection && !hasPotionProtection && !hasRingProtection)
            return damage;

        var normalFireImmunity = (hasSpellProtection && target.FireProtectionNormalFireImmunity)
            || (hasPotionProtection && target.PotionFireResistanceNormalFireImmunity)
            || hasRingProtection;
        if (isNormalFire && normalFireImmunity)
            return 0;

        if (isMagicalFire && hasSpellProtection && target.FireProtectionHalfDamageFromMagicalFire)
            damage = (int)Math.Ceiling(damage * 0.5);

        var fireReductionPerDie = 0;
        if (isMagicalFire && hasPotionProtection && target.PotionFireResistanceDamageReductionPerDie > 0)
            fireReductionPerDie = Math.Max(fireReductionPerDie, target.PotionFireResistanceDamageReductionPerDie);
        if (isMagicalFire && hasRingProtection)
            fireReductionPerDie = Math.Max(fireReductionPerDie, 2);

        if (isMagicalFire && fireReductionPerDie > 0 && damageDiceCount > 0)
        {
            var reduction = fireReductionPerDie * damageDiceCount;
            damage = Math.Max(damageDiceCount, damage - reduction);
        }

        if (isMagicalFire && hasSpellProtection && target.FireProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.FireProtectionAbsorptionRemaining, damage);
            target.FireProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyColdResistanceDamageReduction(Character target, int incomingDamage, bool isMagicalCold, bool isNormalCold)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveResistCold)
            return damage;

        // Resist Cold inures normal cold and halves magical cold before saving throw.
        if (isNormalCold)
            return 0;

        if (isMagicalCold)
            damage = Math.Max(1, damage / 2);

        return Math.Max(0, damage);
    }

    private static int ApplyLightningProtectionDamageReduction(Character target, int incomingDamage, bool isMagicalLightning, bool isNormalLightning)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveProtectionFromLightning)
            return damage;

        if (isNormalLightning && target.LightningProtectionNormalLightningImmunity)
            return 0;

        if (isMagicalLightning && target.LightningProtectionHalfDamageFromMagicalLightning)
            damage = (int)Math.Ceiling(damage * 0.5);

        if (isMagicalLightning && target.LightningProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.LightningProtectionAbsorptionRemaining, damage);
            target.LightningProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyUniversalPotionInvulnerabilitySaveBonus(Character target, int saveTarget)
    {
        var adjusted = saveTarget;

        if (target.HasActivePotionInvulnerability && target.PotionInvulnerabilitySaveBonus > 0)
            adjusted = Math.Max(1, adjusted - target.PotionInvulnerabilitySaveBonus);

        var ringProtectionSaveBonus = Math.Max(target.RingProtectionSelfSaveBonus, target.RingProtectionReceivedAuraSaveBonus);
        if (ringProtectionSaveBonus > 0)
            adjusted = Math.Max(1, adjusted - ringProtectionSaveBonus);

        var luckBladeSaveBonus = GetLuckBladeSaveBonus(target);
        if (luckBladeSaveBonus > 0)
            adjusted = Math.Max(1, adjusted - luckBladeSaveBonus);

        if (HasEquippedCloakOfDisplacement(target))
            adjusted = Math.Max(1, adjusted - 2);

        return adjusted;
    }

    private static int GetLuckBladeSaveBonus(Character target)
    {
        if (target?.Equipment == null)
            return 0;

        static bool IsLuckBlade(Item? item)
        {
            if (item == null || item.Type != ItemType.Weapon)
                return false;

            if (!string.IsNullOrWhiteSpace(item.Name)
                && item.Name.Contains("Luck Blade", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return item.SpecialAbilities != null
                   && item.SpecialAbilities.Any(a =>
                       !string.IsNullOrWhiteSpace(a)
                       && a.Contains("Luck Blade", StringComparison.OrdinalIgnoreCase));
        }

        var hasEquippedLuckBlade = (target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var main) && IsLuckBlade(main))
                                   || (target.Equipment.TryGetValue(EquipmentSlot.OffHand, out var off) && IsLuckBlade(off));

        if (hasEquippedLuckBlade)
            return 1;

        return target.Inventory != null && target.Inventory.Any(IsLuckBlade) ? 1 : 0;
    }

    private static void ApplyRingProtectionAuras(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
        {
            member.RefreshRingProtectionEffects();
            member.SetRingProtectionReceivedAuraSaveBonus(0);
        }

        var strongestAura = session.Party
            .Where(m => m.CurrentHitPoints > 0 && !m.HasStatus(CharacterStatus.Dead))
            .Select(m => Math.Max(0, m.RingProtectionAuraSaveBonus))
            .DefaultIfEmpty(0)
            .Max();

        if (strongestAura <= 0)
            return;

        foreach (var member in session.Party)
            member.SetRingProtectionReceivedAuraSaveBonus(strongestAura);
    }

    private static void ApplyRingWizardryEffects(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
            member.RefreshRingWizardryEffects();
    }

    private void ApplyPoisonDamageDuringCombat(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (member.IsMonkImmuneToPoison())
            {
                if (member.HasStatus(CharacterStatus.Poisoned))
                {
                    member.RemoveStatus(CharacterStatus.Poisoned);
                    events.Add(new CombatEvent($"{member.Name}'s poison is negated by monk immunity."));
                }
                continue;
            }

            if (!IsAlive(member) || !member.HasStatus(CharacterStatus.Poisoned))
                continue;

            var poisonTickRoll = _dice.Roll(100);
            if (poisonTickRoll > 50)
                continue;

            var damage = _dice.Roll(3);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);

            events.Add(new CombatEvent($"Poison harms {member.Name} for {actual} (rolled {damage}). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} dies from poison!"));
            }
        }
    }

    private static void WakeMonsterIfAsleepAfterDamage(MonsterInstance monster, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (!monster.IsAlive)
            return;

        var wokeFromAsleep = monster.HasStatus(MonsterStatus.Asleep);
        var wokeFromUnconscious = monster.HasStatus(MonsterStatus.Unconscious);

        if (!wokeFromAsleep && !wokeFromUnconscious)
            return;

        if (wokeFromAsleep)
            monster.SetStatus(MonsterStatus.Asleep, 0);

        if (wokeFromUnconscious)
            monster.SetStatus(MonsterStatus.Unconscious, 0);

        events.Add(new CombatEvent($"{monster.DisplayName} wakes up from taking damage!"));
    }

    private static void WakeCharacterIfAsleepAfterDamage(Character member, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (member.CurrentHitPoints <= 0)
            return;

        if (!member.HasStatus(CharacterStatus.Asleep))
            return;

        member.RemoveStatus(CharacterStatus.Asleep);
        events.Add(new CombatEvent($"{member.Name} wakes up from taking damage!"));
    }

    private static int GetEquippedRegeneration(Character member)
    {
        var hasRegeneration = member.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));

        if (hasRegeneration)
            return 1;

        var hasRegenerationFromConsumable = member.Inventory
            .Any(item => ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (1)")
                         || ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (Potion)"));

        return hasRegenerationFromConsumable ? 1 : 0;
    }

    private static bool IsAlive(Character c) => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead);

    private static string ResolveWeaponDamageExpression(Character member, Item? mainHand, MonsterInstance target)
    {
        if (mainHand != null && mainHand.Type == ItemType.Weapon)
        {
            if (target.Template.Size == MonsterSize.Large && !string.IsNullOrWhiteSpace(mainHand.DamageVsLarge))
                return mainHand.DamageVsLarge;

            if (!string.IsNullOrWhiteSpace(mainHand.Damage))
                return mainHand.Damage;
        }

        return string.IsNullOrWhiteSpace(member.Damage) ? "1d2" : member.Damage;
    }

    private int GetDragonSlayerDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        if (!IsSwordPlusTwoDragonSlayer(weapon))
            return 1;

        if (!IsTargetEligibleForDragonSlayerBonus(target))
            return 1;

        if (weapon == null)
            return 1;

        var chosenDragonType = GetOrAssignDragonSlayerType(weapon);
        return IsTargetSpecificDragonType(target, chosenDragonType) ? 3 : 1;
    }

    private int GetSwordOfDancingCurrentBonus(Item? weapon, CombatSession session)
    {
        if (!IsSwordOfDancing(weapon) || session == null)
            return 0;

        var phase = ((Math.Max(1, session.RoundNumber) - 1) % 4) + 1;
        return phase;
    }

    private static bool IsSwordOfDancing(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Dancing", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dancing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dancing", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusFourDefender(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Sword +4, Defender", StringComparison.OrdinalIgnoreCase)
               || (weapon.SpecialAbilities != null && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Defender", StringComparison.OrdinalIgnoreCase)));
    }

    private static int GetDefenderAttackBonusAllocation(Item weapon)
    {
        if (weapon.SpecialAbilities == null)
            return 4;

        var profile = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("DefenderAttackBonus:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(profile)
            && int.TryParse(profile[(profile.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Clamp(parsed, 0, 4);
        }

        return 4;
    }

    private static int GetDefenderArmorClassBonus(Character target, Adnd.Core.Monsters.MonsterAttack attack)
    {
        if (target?.Equipment == null)
            return 0;

        if (!IsMonsterAttackFromHandheldWeapon(attack))
            return 0;

        if (!target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand)
            || !IsSwordPlusFourDefender(mainHand))
        {
            return 0;
        }

        var attackBonusAllocation = GetDefenderAttackBonusAllocation(mainHand!);
        return Math.Max(0, 4 - attackBonusAllocation);
    }

    private static bool ShouldApplyCloakOfDisplacementAutoMiss(CombatSession session, Character target)
    {
        if (session == null || target == null)
            return false;

        if (!HasEquippedCloakOfDisplacement(target))
            return false;

        return session.CloakOfDisplacementFirstMissUsed.Add(target.Name);
    }

    private static bool HasEquippedCloakOfDisplacement(Character target)
    {
        if (target?.Equipment == null)
            return false;

        if (!target.Equipment.TryGetValue(EquipmentSlot.Back, out var back)
            || back == null)
        {
            return false;
        }

        if (string.Equals(back.Name, "Cloak of Displacement", StringComparison.OrdinalIgnoreCase))
            return true;

        return ItemSpecialAbilityParser.HasSpecialAbility(back, "Displacement");
    }

    private static bool IsMonsterAttackFromHandheldWeapon(Adnd.Core.Monsters.MonsterAttack attack)
    {
        var name = (attack?.Name ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("hurled") || name.Contains("thrown") || name.Contains("missile"))
            return false;

        return name.Contains("sword")
               || name.Contains("dagger")
               || name.Contains("mace")
               || name.Contains("spear")
               || name.Contains("axe")
               || name.Contains("club")
               || name.Contains("hammer")
               || name.Contains("staff")
               || name.Contains("flail")
               || name.Contains("halberd")
               || name.Contains("morning star")
               || name.Contains("trident");
    }

    private static bool IsSwordPlusFiveHolyAvenger(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Sword +5, Holy Avenger", StringComparison.OrdinalIgnoreCase)
               || (weapon.SpecialAbilities != null && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Holy Avenger", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsTargetChaoticEvil(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        return string.Equals(target.Template.Alignment?.Trim(), "Chaotic Evil", StringComparison.OrdinalIgnoreCase)
               || string.Equals(target.Template.Alignment?.Trim(), "CE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaladinHolyAvengerAuraActive(Character member, Item? mainHand)
    {
        return member != null
               && member.IsPaladin()
               && IsSwordPlusFiveHolyAvenger(mainHand)
               && member.CurrentHitPoints > 0
               && !member.HasStatus(CharacterStatus.Dead);
    }

    private static bool IsWithinHolyAvengerAura(Character paladin, Character target)
    {
        // Current combat model has no tactical map distance; treat active allies as within 5' aura.
        return paladin != null
               && target != null
               && target.CurrentHitPoints > 0
               && !target.HasStatus(CharacterStatus.Dead);
    }

    private bool TrySuppressMonsterSpellByHolyAvengerAura(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session?.Party == null || monster?.Template == null)
            return false;

        foreach (var member in session.Party)
        {
            if (member?.Equipment == null)
                continue;

            member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand);
            if (!IsPaladinHolyAvengerAuraActive(member, mainHand))
                continue;

            var chance = 50;
            var roll = _rng.Next(1, 101);
            if (roll <= chance)
            {
                events.Add(new CombatEvent($"{member.Name}'s Holy Avenger aura disrupts {monster.DisplayName}'s spellcasting (MR {chance}%, roll {roll})."));
                return true;
            }
        }

        return false;
    }

    private void ApplyHolyAvengerPartyAuras(CombatSession session)
    {
        // Character currently has no persisted magic-resistance stat.
        // Holy Avenger party aura is handled dynamically via spell suppression checks.
    }

    private bool TryExtinguishFireWithFrostBrand(Character member, CombatSession session, List<CombatEvent> events)
    {
        if (member?.Equipment == null)
            return false;

        if (!member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand)
            || !IsSwordPlusThreeFrostBrand(mainHand))
        {
            return false;
        }

        var roll = _dice.Roll(100);
        if (roll > 50)
            return false;

        session.SetPartyVeryHotFireExposure(member.Name, 0, 0);
        events.Add(new CombatEvent($"{member.Name} thrusts Frost Brand into the flames and extinguishes nearby fire (50% chance)."));
        return true;
    }

    private string GetOrAssignDragonSlayerType(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("DragonSlayerType:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing))
            return existing[(existing.IndexOf(':') + 1)..].Trim();

        var rolledType = _dice.Roll(10) switch
        {
            1 => "Black",
            2 => "Blue",
            3 => "Brass",
            4 => "Bronze",
            5 => "Copper",
            6 => "Gold",
            7 => "Green",
            8 => "Red",
            9 => "Silver",
            _ => "White"
        };

        weapon.SpecialAbilities.Add($"DragonSlayerType:{rolledType}");
        return rolledType;
    }

    private static bool IsTargetSpecificDragonType(MonsterInstance target, string dragonType)
    {
        if (target?.Template == null || string.IsNullOrWhiteSpace(dragonType))
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        var type = dragonType.ToLowerInvariant();

        return name.Contains("dragon") && name.Contains(type);
    }

    private static bool IsHalfDamageFromSharpWeapons(MonsterInstance target, Item? mainHand)
    {
        if (mainHand == null || mainHand.Type != ItemType.Weapon)
            return false;

        var damageType = mainHand.DamageType?.Trim();
        if (string.IsNullOrWhiteSpace(damageType))
            return false;

        var isSharp = damageType.Equals("Piercing", StringComparison.OrdinalIgnoreCase)
            || damageType.Equals("Slashing", StringComparison.OrdinalIgnoreCase);

        if (!isSharp)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "Half Damage from Sharp Weapons", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSlashingAttack(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        return string.Equals(weapon.DamageType?.Trim(), "Slashing", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSwordPlusOnePlusTwoVsMagicUsingAndEnchantedCreatures(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Replace('&', ' ').ToLowerInvariant();
        if (normalized.Contains("sword +1, +2 vs. magic-using", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +2 vs magic-using", StringComparison.Ordinal)
            || normalized.Contains("sword +1, +2 vs. magic using", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+2 vs. magic-using and enchanted creatures", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusThreeVsLycanthropesAndShapeChangers(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Replace('&', ' ').ToLowerInvariant();
        if (normalized.Contains("sword +1, +3 vs. lycanthropes", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +3 vs lycanthropes", StringComparison.Ordinal)
            || normalized.Contains("sword +1, +3 vs. lycanthropes shape changers", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+3 vs. lycanthropes and shape changers", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusThreeVsRegeneratingCreatures(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +1, +3 vs. regenerating creatures", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +3 vs regenerating creatures", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+3 vs. regenerating creatures", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusFourVsReptiles(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +1, +4 vs. reptiles", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +4 vs reptiles", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+4 vs. reptiles", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOneFlameTongue(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Flame Tongue", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Flame Tongue", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusTwoGiantSlayer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, giant slayer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 giant slayer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Giant Slayer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusTwoDragonSlayer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, dragon slayer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 dragon slayer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dragon Slayer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDwarvenThrower(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Dwarven Thrower", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dwarven Thrower", StringComparison.OrdinalIgnoreCase));
    }

    private void TryApplyMaceOfDisruptionEvilBacklash(CombatSession session, Character member, List<CombatEvent> events)
    {
        if (session.MaceOfDisruptionBacklashApplied.Contains(member.Name))
            return;

        if (!IsCharacterEvil(member.Alignment))
            return;

        var touching = member.Equipment.Values.Any(IsMaceOfDisruption)
                       || member.Inventory.Any(IsMaceOfDisruption);
        if (!touching)
            return;

        session.MaceOfDisruptionBacklashApplied.Add(member.Name);

        const int damage = 100;
        var before = member.CurrentHitPoints;
        member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
        var actual = before - member.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(member, actual, events);

        events.Add(new CombatEvent($"{member.Name} is blasted by Mace of Disruption's holy backlash for {actual} damage."));
        if (member.CurrentHitPoints <= 0)
        {
            member.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{member.Name} is slain by Mace of Disruption backlash."));
        }
    }

    private bool TryResolveMaceOfDisruptionEffect(Character wielder, Item? weapon, MonsterInstance target, bool useRanged, List<CombatEvent> events)
    {
        if (wielder == null || target?.Template == null || weapon == null)
            return false;

        if (useRanged || !IsMaceOfDisruption(weapon) || !IsMaceOfDisruptionAffectedTarget(target))
            return false;

        var saveChance = GetMaceOfDisruptionSaveChancePercent(target);
        var roll = _dice.Roll(100);
        if (roll > saveChance)
        {
            target.CurrentHitPoints = 0;
            events.Add(new CombatEvent($"Mace of Disruption disrupts {target.DisplayName} utterly ({roll} vs save {saveChance}%)."));
            return true;
        }

        events.Add(new CombatEvent($"{target.DisplayName} resists disruption ({roll} vs save {saveChance}%)."));
        return false;
    }

    private static bool IsMaceOfDisruption(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Mace of Disruption", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Mace of Disruption", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetMaceOfDisruptionDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        return IsMaceOfDisruption(weapon) && IsMaceOfDisruptionAffectedTarget(target)
            ? 2
            : 1;
    }

    private static bool IsMaceOfDisruptionAffectedTarget(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Undead)
            return true;

        if (target.InstanceMonsterType is MonsterType.Demon or MonsterType.Devil)
            return true;

        return IsTargetEvilAlignment(target.Template.Alignment);
    }

    private static int GetMaceOfDisruptionSaveChancePercent(MonsterInstance target)
    {
        if (target?.Template == null)
            return 95;

        var n = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("skeleton")
            || n.Contains("zombie")
            || n.Contains("ghoul")
            || n.Contains("shadow")
            || n.Contains("wight")
            || n.Contains("wraith"))
            return 0;
        if (n.Contains("mummy")) return 20;
        if (n.Contains("spectre") || n.Contains("specter")) return 35;
        if (n.Contains("vampire")) return 50;
        if (n.Contains("ghost")) return 65;
        if (n.Contains("lich")) return 60;
        return 95;
    }

    private static bool IsCharacterEvil(Alignment alignment)
    {
        return alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;
    }

    private static bool IsTargetEvilAlignment(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return false;

        var text = alignment.Trim();
        return text.Equals("Lawful Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Neutral Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Chaotic Evil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHammerOfThunderbolts(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Hammer of Thunderbolts", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Hammer of Thunderbolts", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJavelinOfLightning(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Javelin of Lightning", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Javelin of Lightning", StringComparison.OrdinalIgnoreCase));
    }

    private void ResolveJavelinOfLightningStrike(Character thrower, Item weapon, MonsterInstance impactTarget, CombatSession session, List<CombatEvent> events)
    {
        if (!IsJavelinOfLightning(weapon) || impactTarget?.Template == null)
            return;

        var baseName = Regex.Replace(weapon.Name ?? "Javelin of Lightning", @"\s*\(\d+\)\s*$", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Javelin of Lightning";

        var primaryBefore = impactTarget.CurrentHitPoints;
        impactTarget.CurrentHitPoints = Math.Max(0, impactTarget.CurrentHitPoints - 20);
        var primaryLightningDamage = primaryBefore - impactTarget.CurrentHitPoints;
        if (primaryLightningDamage > 0)
        {
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning discharges for 20 electrical damage on {impactTarget.DisplayName}. HP {primaryBefore}->{impactTarget.CurrentHitPoints}."));
            WakeMonsterIfAsleepAfterDamage(impactTarget, primaryLightningDamage, events);
            MonsterSplitResolver.TryResolveSplit(
                session,
                impactTarget,
                primaryLightningDamage,
                JellySplitTrigger.Lightning,
                message => events.Add(new CombatEvent(message)));
        }

        var backStrokeTargets = session.GetAliveMonstersByGroup(impactTarget.GroupId)
            .Where(m => !ReferenceEquals(m, impactTarget))
            .ToList();

        foreach (var secondary in backStrokeTargets)
        {
            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(secondary, 20);
            var saveRoll = _dice.Roll(20);
            var boltDamage = saveRoll >= saveTarget ? 10 : 20;

            var beforeSecondary = secondary.CurrentHitPoints;
            secondary.CurrentHitPoints = Math.Max(0, secondary.CurrentHitPoints - boltDamage);
            var actualSecondary = beforeSecondary - secondary.CurrentHitPoints;

            if (actualSecondary <= 0)
                continue;

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"Backstroke lightning grazes {secondary.DisplayName}: save {saveRoll} vs {saveTarget}, takes 10 electrical damage. HP {beforeSecondary}->{secondary.CurrentHitPoints}."
                : $"Backstroke lightning rakes {secondary.DisplayName}: save {saveRoll} vs {saveTarget}, takes 20 electrical damage. HP {beforeSecondary}->{secondary.CurrentHitPoints}."));

            WakeMonsterIfAsleepAfterDamage(secondary, actualSecondary, events);
            var splitFromLightning = MonsterSplitResolver.TryResolveSplit(
                session,
                secondary,
                actualSecondary,
                JellySplitTrigger.Lightning,
                message => events.Add(new CombatEvent(message)));
            if (!splitFromLightning && secondary.CurrentHitPoints <= 0)
                events.Add(new CombatEvent($"{secondary.DisplayName} is destroyed by the lightning backstroke."));
        }

        weapon.Quantity = Math.Max(0, weapon.Quantity - 1);
        if (weapon.Quantity <= 0)
        {
            if (thrower.Equipment.TryGetValue(EquipmentSlot.Range, out var equippedRange)
                && ReferenceEquals(equippedRange, weapon))
            {
                thrower.Equipment[EquipmentSlot.Range] = null;
            }

            thrower.Inventory.RemoveAll(item => ReferenceEquals(item, weapon) || (IsJavelinOfLightning(item) && item.Quantity <= 0));
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning is consumed in the discharge."));
        }
        else
        {
            weapon.Name = $"{baseName} ({weapon.Quantity})";
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning crackles out. {weapon.Quantity} remaining."));
        }
    }

    private static bool MeetsHammerOfThunderboltsBaselineStrength(Character wielder)
    {
        if (wielder == null)
            return false;

        if (wielder.Abilities.Strength > 18)
            return true;

        return wielder.Abilities.Strength == 18
               && (wielder.ExceptionalStrengthPercentile ?? 0) >= 1;
    }

    private static bool IsGauntletsOfOgrePower(Item? item)
    {
        var name = item?.Name?.Trim() ?? string.Empty;
        return name.Contains("Gauntlets of Ogre Power", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGirdleOfGiantStrength(Item? item)
    {
        var name = item?.Name?.Trim() ?? string.Empty;
        return name.Contains("Girdle of Giant Strength", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHammerOfThunderboltsTrueName(Item? weapon)
    {
        if (weapon?.SpecialAbilities == null)
            return false;

        return weapon.SpecialAbilities.Any(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.Contains("TrueNameKnown", StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanWieldHammerOfThunderboltsAsPlusFive(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return false;

        if (!HasHammerOfThunderboltsTrueName(weapon))
            return false;

        var hasGauntlets = wielder.Equipment.TryGetValue(EquipmentSlot.Hands, out var hands)
                           && IsGauntletsOfOgrePower(hands);
        var hasGirdle = wielder.Equipment.TryGetValue(EquipmentSlot.Belt, out var belt)
                        && IsGirdleOfGiantStrength(belt);

        return hasGauntlets && hasGirdle;
    }

    private static int GetHammerOfThunderboltsToHitBonus(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 0;

        if (CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 5;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) ? 3 : 0;
    }

    private static int GetHammerOfThunderboltsDamageBonus(Character wielder, Item? weapon, bool useRanged)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 0;

        if (!useRanged && !MeetsHammerOfThunderboltsBaselineStrength(wielder) && !CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 0;

        if (CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 5;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) ? 3 : 0;
    }

    private static int GetHammerOfThunderboltsDamageDiceMultiplier(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 1;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) || CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon)
            ? 2
            : 1;
    }

    private static bool IsTargetGiantOrKinForHammerOfThunderbolts(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        return name.Contains("giant")
               || name.Contains("ogre")
               || name.Contains("ogre mage")
               || name.Contains("troll")
               || name.Contains("ettin");
    }

    private static bool CanUseDwarvenThrowerFullPower(Character wielder, Item? weapon)
    {
        if (!IsDwarvenThrower(weapon) || wielder == null)
            return false;

        return wielder.Race == Race.Dwarf
               && (wielder.Classes.Contains(CharacterClass.Fighter)
                   || wielder.Class == CharacterClass.Fighter);
    }

    private static int GetDwarvenThrowerDamageMultiplier(Character wielder, Item? weapon, MonsterInstance target, bool useRanged)
    {
        if (!useRanged || !CanUseDwarvenThrowerFullPower(wielder, weapon) || target?.Template == null)
            return 1;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("giant")
            || name.Contains("ogre")
            || name.Contains("ogre mage")
            || name.Contains("troll")
            || name.Contains("ettin"))
        {
            return 3;
        }

        return 2;
    }

    private static bool IsSwordPlusTwoNineLivesStealer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, nine lives stealer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 nine lives stealer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Nine Lives Stealer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfWounding(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Wounding", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Wounding", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfLifeStealing(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Life Stealing", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Life Stealing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Life Stealing", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfSharpness(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Sharpness", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Sharpness", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordVorpalWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Vorpal", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Vorpal", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveSwordOfSharpnessSeverEffect(Item? weapon, MonsterInstance target, int naturalRoll, List<CombatEvent> events)
    {
        if (!IsSwordOfSharpness(weapon) || target?.Template == null)
            return false;

        // AD&D note: modified sever score considers only sword bonus (+1 for Sword of Sharpness).
        var severScore = naturalRoll + 1;
        var needed = GetSwordOfSharpnessSeverScoreNeeded(target);
        if (severScore < needed)
            return false;

        var locationRoll = Random.Shared.Next(1, 7);
        var location = locationRoll switch
        {
            1 => "arm",
            2 => "leg",
            3 => "tail",
            4 => "tentacle",
            5 => "neck",
            _ => "other extremity"
        };

        if (string.Equals(location, "neck", StringComparison.OrdinalIgnoreCase))
        {
            target.CurrentHitPoints = 0;
            events.Add(new CombatEvent($"Sword of Sharpness severs {target.DisplayName}'s neck! {target.DisplayName} is slain instantly."));
            return true;
        }

        var before = target.CurrentHitPoints;
        var extra = Math.Max(4, Math.Max(1, target.MaxHitPoints / 4));
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - extra);
        var applied = before - target.CurrentHitPoints;
        events.Add(new CombatEvent($"Sword of Sharpness severs {target.DisplayName}'s {location} (score {severScore} vs {needed}), causing {applied} additional damage."));
        return true;
    }

    private static bool TryResolveSwordVorpalSeverEffect(Item? weapon, MonsterInstance target, int naturalRoll, List<CombatEvent> events)
    {
        if (!IsSwordVorpalWeapon(weapon) || target?.Template == null)
            return false;

        // AD&D note: modified sever score considers only sword bonus (+3 for Vorpal).
        var severScore = naturalRoll + 3;
        var needed = GetSwordVorpalSeverScoreNeeded(target);
        if (severScore < needed)
            return false;

        if (!CanTargetBeDecapitated(target))
        {
            events.Add(new CombatEvent($"Vorpal strike lands on {target.DisplayName} (score {severScore} vs {needed}), but decapitation is impossible due to form."));
            return true;
        }

        if (SurvivesDecapitation(target))
        {
            var before = target.CurrentHitPoints;
            var extra = Math.Max(6, Math.Max(1, target.MaxHitPoints / 3));
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - extra);
            var applied = before - target.CurrentHitPoints;
            events.Add(new CombatEvent($"Vorpal strike severs {target.DisplayName}'s head, but it survives; extra damage {applied}."));
            return true;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"Vorpal strike severs {target.DisplayName}'s head! {target.DisplayName} is slain instantly."));
        return true;
    }

    private static int GetSwordOfSharpnessSeverScoreNeeded(MonsterInstance target)
    {
        if (target?.Template == null)
            return 99;

        if (IsSolidMetalOrStoneTarget(target))
            return 21;

        return target.Template.Size == MonsterSize.Large ? 20 : 19;
    }

    private static int GetSwordVorpalSeverScoreNeeded(MonsterInstance target)
    {
        if (target?.Template == null)
            return 99;

        if (IsSolidMetalOrStoneTarget(target))
            return 22;

        return target.Template.Size == MonsterSize.Large ? 21 : 20;
    }

    private static bool IsSolidMetalOrStoneTarget(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("stone")
               || composite.Contains("rock")
               || composite.Contains("metal")
               || composite.Contains("iron")
               || composite.Contains("steel")
               || composite.Contains("golem");
    }

    private static bool CanTargetBeDecapitated(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        if (composite.Contains("ooze")
            || composite.Contains("slime")
            || composite.Contains("jelly")
            || composite.Contains("amorph")
            || composite.Contains("formless")
            || composite.Contains("gaseous")
            || composite.Contains("swarm")
            || composite.Contains("elemental"))
        {
            return false;
        }

        if (!composite.Contains("head")
            && !composite.Contains("neck")
            && (target.InstanceMonsterType == MonsterType.Ooze || target.InstanceMonsterType == MonsterType.Elemental))
        {
            return false;
        }

        return true;
    }

    private static bool SurvivesDecapitation(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("doppelganger")
               || composite.Contains("elemental")
               || composite.Contains("golem");
    }

    private static bool IsBluntWeapon(Item weapon)
    {
        if (!string.IsNullOrWhiteSpace(weapon.DamageType))
            return weapon.DamageType.Contains("blunt", StringComparison.OrdinalIgnoreCase);

        var name = weapon.Name.ToLowerInvariant();
        return new[] { "mace", "staff", "club", "hammer", "flail", "morning star", "bo stick", "jo stick" }
            .Any(k => name.Contains(k, StringComparison.Ordinal));
    }

    private static bool CanWeaponHarmTargetByMagicRequirement(Item? weapon, MonsterInstance target)
    {
        if (HasSpecialDefense(target, "Blunt Weapon Immunity")
            && (weapon == null || IsBluntWeapon(weapon))
            && GetEffectiveWeaponBonusForHitRequirement(weapon) < 1)
        {
            return false;
        }

        if (HasSpecialDefense(target, "Weapon Immunity")
            && target.Template.SpecialDefenses.Any(d => $"{d.Name} {d.Description}".Contains("silver, iron", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsSilverWeapon(weapon) || IsIronWeapon(weapon))
                return true;

            return GetEffectiveWeaponBonusForHitRequirement(weapon) >= 3;
        }

        if (HasMagicOrColdIronWeaponsRequirement(target))
        {
            if (IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
                return true;

            if (IsColdIronWeapon(weapon))
                return true;

            var effectiveBonusForColdIronOrMagicRequirement = GetEffectiveWeaponBonusForHitRequirement(weapon);
            return effectiveBonusForColdIronOrMagicRequirement >= 1;
        }

        if (HasImmunityToNormalWeapons(target))
        {
            if (IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
                return true;

            if (IsSilverWeapon(weapon))
                return true;

            var effectiveBonusForNormalWeaponImmunity = GetEffectiveWeaponBonusForHitRequirement(weapon);
            return effectiveBonusForNormalWeaponImmunity >= 1;
        }

        var requiredBonus = GetRequiredWeaponBonusToHit(target);
        if (requiredBonus <= 0)
            return true;

        var effectiveWeaponBonus = Math.Max(GetEffectiveWeaponBonusForHitRequirement(weapon),
            Math.Max(0, weapon?.ToHitBonus ?? 0) + GetSituationalSwordBonusAgainstTarget(weapon, target));
        return effectiveWeaponBonus >= requiredBonus;
    }

    private static bool HasMagicOrColdIronWeaponsRequirement(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Magic or Cold Iron Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Magic or Cold Iron Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Only harmed by magic or cold iron weapons.", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("only harmed by magic", StringComparison.Ordinal)
                       && merged.Contains("cold iron", StringComparison.Ordinal));
        });
    }

    private static bool CanMonsterHarmLycanthropeForm(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        if (monster.Template.Name?.Contains("silver", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var text = string.Join(" ",
            new[]
            {
                monster.Template.Name,
                monster.Template.TypeName,
                string.Join(" ", monster.Template.SpecialAbilities.Select(a => $"{a.Name} {a.Description}")),
                string.Join(" ", monster.Template.SpecialAttacks.Select(a => $"{a.Name} {a.Description}")),
                string.Join(" ", monster.Template.SpecialDefenses.Select(a => $"{a.Name} {a.Description}"))
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return text.Contains("magic weapon", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+1", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+2", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+3", StringComparison.OrdinalIgnoreCase)
               || text.Contains("enchanted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasImmunityToNormalWeapons(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Hit only by magic weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Hit only by magic weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Hit Only by Silver or Magic Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Hit Only by Silver or Magic Weapons", StringComparison.OrdinalIgnoreCase)
                   || merged.Contains("hit only by magic weapon", StringComparison.Ordinal)
                   || merged.Contains("hit only by silver or magic weapon", StringComparison.Ordinal)
                   || merged.Contains("only be harmed by magic weapon", StringComparison.Ordinal)
                   || (merged.Contains("can only be hit by silver", StringComparison.Ordinal)
                       && merged.Contains("+1", StringComparison.Ordinal));
        });
    }

    private static bool HasMagicWeaponsOnlyHalfDamageDefense(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var merged = $"{d.Name} {d.Description}".ToLowerInvariant();
            return merged.Contains("magic weapons only", StringComparison.Ordinal)
                   && merged.Contains("half damage", StringComparison.Ordinal)
                   && merged.Contains("magical weapon", StringComparison.Ordinal);
        });
    }

    private bool TryResolveMummyImprovisedWeaponDamage(CombatSession session, Character attacker, MonsterInstance target, Item? weapon, List<CombatEvent> events)
    {
        if (!IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
            return false;

        var before = target.CurrentHitPoints;
        var damage = 0;

        if (IsHolyWaterWeapon(weapon))
        {
            damage = _dice.Roll(4) + _dice.Roll(4);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{attacker.Name} splashes holy water on {target.DisplayName} for {damage} damage (2d4). HP {before}->{target.CurrentHitPoints}."));
        }
        else if (IsBurningOilWeapon(weapon))
        {
            var firstRoundDamage = _dice.Roll(8);
            var secondRoundDamage = firstRoundDamage * 2;
            damage = firstRoundDamage;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - firstRoundDamage);
            session.SetMummyBurningOilSecondRoundDamage(target, secondRoundDamage);
            events.Add(new CombatEvent($"{attacker.Name} splashes burning oil on {target.DisplayName} for {firstRoundDamage} damage this round and {secondRoundDamage} damage next round. HP {before}->{target.CurrentHitPoints}."));
        }
        else if (IsTorchWeapon(weapon))
        {
            damage = _dice.Roll(3);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{attacker.Name} strikes {target.DisplayName} with a torch for {damage} damage (1d3). HP {before}->{target.CurrentHitPoints}."));
        }

        var actual = Math.Max(0, before - target.CurrentHitPoints);
        WakeMonsterIfAsleepAfterDamage(target, actual, events);

        if (target.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));

        return true;
    }

    private static bool TryApplyMummyBurningOilSecondRoundDamage(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var pendingDamage = session.ConsumeMummyBurningOilSecondRoundDamage(monster);
        if (pendingDamage <= 0)
            return false;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - pendingDamage);
        var actual = Math.Max(0, before - monster.CurrentHitPoints);

        if (actual > 0)
            WakeMonsterIfAsleepAfterDamage(monster, actual, events);

        events.Add(new CombatEvent($"Burning oil continues to burn {monster.DisplayName} for {actual} damage in the second round. HP {before}->{monster.CurrentHitPoints}."));
        if (!monster.IsAlive)
            events.Add(new CombatEvent($"{monster.DisplayName} is destroyed by burning oil."));

        return !monster.IsAlive;
    }

    private static bool IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(MonsterInstance target, Item? weapon)
    {
        return SpellDamageSaveHelper.IsMummy(target)
               && (IsTorchWeapon(weapon) || IsBurningOilWeapon(weapon) || IsHolyWaterWeapon(weapon));
    }

    private static bool IsTorchWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("torch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBurningOilWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("burning oil", StringComparison.OrdinalIgnoreCase)
               || name.Contains("flask of oil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHolyWaterWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("holy water", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSpecialDefense(MonsterInstance target, string defenseName)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d?.Name?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasOnlyAffectedByFireBasedAttacksDefense(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var name = d?.Name?.Trim() ?? string.Empty;
            var description = d?.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}";
            return string.Equals(name, "Affected only by fire based attacks", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Affected only by fire based attacks", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("affected only by fire", StringComparison.OrdinalIgnoreCase)
                       && merged.Contains("attack", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static bool IsFireBasedWeaponAttack(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("fire", StringComparison.OrdinalIgnoreCase)
               || text.Contains("flame", StringComparison.OrdinalIgnoreCase)
               || text.Contains("burn", StringComparison.OrdinalIgnoreCase)
               || text.Contains("torch", StringComparison.OrdinalIgnoreCase)
               || text.Contains("oil", StringComparison.OrdinalIgnoreCase)
               || text.Contains("incendiary", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanMonsterLocateTarget(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (monster == null || target == null)
            return false;

        if (!target.HasStatus(CharacterStatus.Invisible))
            return true;

        if (!HasSpecialDefense(monster, "Exceptional Senses"))
            return true;

        var roll = _dice.Roll(100);
        var located = roll <= 50;
        if (located)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} uses Exceptional Senses and locates invisible {target.Name} (roll {roll} <= 50)."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} uses Exceptional Senses but fails to locate invisible {target.Name} (roll {roll} > 50)."));
        return false;
    }

    private bool TryResolveBlinkDefenseAgainstAttack(MonsterInstance target, List<CombatEvent> events)
    {
        if (!IsBlinkDog(target))
            return false;

        var blinkRoll = _dice.Roll(100);
        if (blinkRoll > 50)
            return false;

        events.Add(new CombatEvent($"{target.DisplayName} blinks aside; 50% displacement causes the hit to miss (roll {blinkRoll} <= 50)."));
        return true;
    }

    private bool TryResolveBlinkDogTeleportBehind(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!IsBlinkDog(monster))
            return false;

        var blinkTriggerRoll = _dice.Roll(12);
        if (blinkTriggerRoll < 7)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} fails blink reposition check (1d12={blinkTriggerRoll}, needs 7+)."));
            return false;
        }

        var locationRoll = _dice.Roll(12);
        var location = locationRoll switch
        {
            1 => "in front of",
            2 => "at the shielded (left) front flank of",
            3 => "at the unshielded (right) front flank of",
            _ => "behind"
        };

        var isBehind = locationRoll >= 4;
        events.Add(new CombatEvent($"{monster.DisplayName} blinks and reappears {location} {target.Name} (location roll {locationRoll})."));

        if (isBehind)
            events.Add(new CombatEvent($"{monster.DisplayName} gains rear-attack benefits this strike (75% behind chance represented by 4-12 on d12)."));

        return isBehind;
    }

    private bool TryResolveBlinkDogPackRetreat(CombatSession session, MonsterInstance actingMonster, List<CombatEvent> events)
    {
        if (!IsBlinkDog(actingMonster))
            return false;

        var allBlinkDogsInGroup = session.Monsters
            .Where(m => string.Equals(m.GroupId, actingMonster.GroupId, StringComparison.OrdinalIgnoreCase) && IsBlinkDog(m))
            .ToList();
        if (allBlinkDogsInGroup.Count == 0)
            return false;

        var aliveBlinkDogsInGroup = allBlinkDogsInGroup.Where(m => m.IsAlive).ToList();
        if (aliveBlinkDogsInGroup.Count == 0)
            return false;

        var losses = allBlinkDogsInGroup.Count(m => !m.IsAlive);
        var lossesPercent = (losses * 100.0) / allBlinkDogsInGroup.Count;
        var seriouslyThreatened = actingMonster.CurrentHitPoints <= Math.Max(1, actingMonster.MaxHitPoints / 2);
        var sustainedQuarterLosses = lossesPercent >= 25.0;

        if (!seriouslyThreatened && !sustainedQuarterLosses)
            return false;

        foreach (var dog in aliveBlinkDogsInGroup)
            dog.CurrentHitPoints = 0;

        var reason = seriouslyThreatened && sustainedQuarterLosses
            ? "serious threat and 25%+ losses"
            : seriouslyThreatened
                ? "serious threat"
                : "25%+ losses";

        events.Add(new CombatEvent($"{actingMonster.Name} pack blinks out due to {reason} and does not return."));
        return true;
    }

    private static bool IsBlinkDog(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        var name = monster.Template.Name?.Trim() ?? string.Empty;
        return string.Equals(name, "Blink Dog", StringComparison.OrdinalIgnoreCase);
    }

    private void TryApplyNauseaFromCarrionStench(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Nausea") || !session.NauseaExposedMonsterGroups.Add(monster.GroupId))
            return;

        var nauseated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        session.NauseatedPartyByMonsterGroup[monster.GroupId] = nauseated;
        foreach (var member in session.ExposedParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                member,
                _savingThrowService.GetSaveTarget(member, SaveThrowType.ParalyzationPoisonDeath));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);

            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{member.Name} resists {monster.Name} group {monster.GroupId}'s carrion stench nausea (save vs. poison {saveRoll} vs {saveTarget}); no further saves against this group this battle."));
                continue;
            }

            nauseated.Add(member.Name);
            events.Add(new CombatEvent($"{member.Name} is nauseated by {monster.Name} group {monster.GroupId}'s carrion stench (save vs. poison {saveRoll} vs {saveTarget}) and suffers -2 to hit its nausea-causing monsters until battle ends."));
        }
    }

    private static bool IsSilverWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("silver", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIronWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        if (text.Contains("iron", StringComparison.OrdinalIgnoreCase) || text.Contains("steel", StringComparison.OrdinalIgnoreCase))
            return true;

        // Ordinary weapons are iron/steel unless made of wood or only a blunt improvised item.
        return !IsWoodenItem(weapon) && !IsBluntWeapon(weapon);
    }

    private static bool IsColdIronWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("cold iron", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetColdIronVulnerabilityDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return 1;

        var hasVulnerability = target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "Cold Iron Vulnerability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Description?.Trim(), "Cold Iron Vulnerability", StringComparison.OrdinalIgnoreCase)
            || ((d.Name ?? string.Empty) + " " + (d.Description ?? string.Empty))
                .Contains("cold iron", StringComparison.OrdinalIgnoreCase));

        if (!hasVulnerability)
            return 1;

        return IsColdIronWeapon(weapon) ? 2 : 1;
    }

    private static int GetWeaponVsArmorClassAdjustment(Item? weapon, int targetArmorClass, bool isRanged)
    {
        if (targetArmorClass < 2 || targetArmorClass > 10)
            return 0;

        var key = ResolveWeaponVsArmorTableKey(weapon, isRanged);
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        var table = isRanged ? RangedWeaponVsArmorAdjustments : MeleeWeaponVsArmorAdjustments;
        if (!table.TryGetValue(key, out var row) || row.Length < 9)
            return 0;

        return row[targetArmorClass - 2];
    }

    private static string? GetIncapacitatedTargetReason(MonsterInstance target)
    {
        if (target.HasStatus(MonsterStatus.Paralyzed))
            return "paralyzed";
        if (target.HasStatus(MonsterStatus.Stunned))
            return "stunned";
        if (target.HasStatus(MonsterStatus.Asleep))
            return "asleep";
        if (target.HasStatus(MonsterStatus.Unconscious))
            return "unable to move (unconscious)";
        if (target.HasStatus(MonsterStatus.Petrified))
            return "unable to move (petrified)";

        return null;
    }

    private static string? GetIncapacitatedTargetReason(Character target, CombatSession session)
    {
        if (target.HasStatus(CharacterStatus.Paralyzed))
            return "paralyzed";

        if (target.HasStatus(CharacterStatus.Asleep) || session.GetPartyAsleepRounds(target.Name) > 0)
            return "asleep";

        if (target.HasStatus(CharacterStatus.Petrified))
            return "unable to move (petrified)";

        return null;
    }

    private static string? ResolveWeaponVsArmorTableKey(Item? weapon, bool isRanged)
    {
        if (weapon == null)
            return isRanged ? null : "fist or open hand";

        var exactTable = isRanged ? ExactRangedWeaponNameToTableKey : ExactMeleeWeaponNameToTableKey;
        if (exactTable.TryGetValue(weapon.Name.Trim(), out var exactKey))
            return exactKey;

        var normalized = NormalizeWeaponName(weapon.Name);
        if (string.IsNullOrWhiteSpace(normalized))
            return isRanged ? null : "fist or open hand";

        if (isRanged)
        {
            if (normalized.Contains("composite") && normalized.Contains("long") && normalized.Contains("bow")) return "bow composite long";
            if (normalized.Contains("composite") && normalized.Contains("short") && normalized.Contains("bow")) return "bow composite short";
            if (normalized.Contains("long") && normalized.Contains("bow")) return "bow long";
            if (normalized.Contains("short") && normalized.Contains("bow")) return "bow short";
            if (normalized.Contains("crossbow") && normalized.Contains("heavy")) return "crossbow heavy";
            if (normalized.Contains("crossbow") && normalized.Contains("light")) return "crossbow light";
            if (normalized.Contains("sling") && normalized.Contains("bullet")) return "sling bullet";
            if (normalized.Contains("sling") && normalized.Contains("stone")) return "sling stone";
            if (normalized.Contains("sling")) return "sling stone";
            if (normalized.Contains("javelin")) return "javelin";
            if (normalized.Contains("dart")) return "dart";
            if (normalized.Contains("hand") && normalized.Contains("axe")) return "axe hand";
            if (normalized.Contains("dagger")) return "dagger";
            if (normalized.Contains("club")) return "club";
            if (normalized.Contains("hammer")) return "hammer";
            if (normalized.Contains("spear")) return "spear";
            return null;
        }

        if (normalized.Contains("battle") && normalized.Contains("axe")) return "axe battle";
        if (normalized.Contains("hand") && normalized.Contains("axe")) return "axe hand";
        if (normalized.Contains("bardiche")) return "bardiche";
        if (normalized.Contains("boc") && normalized.Contains("corbin")) return "boc de corbin";
        if (normalized.Contains("bill") && normalized.Contains("guisarme")) return "bill guisarme";
        if (normalized.Contains("bo stick")) return "bo stick";
        if (normalized.Contains("club")) return "club";
        if (normalized.Contains("dagger")) return "dagger";
        if (normalized.Contains("fauchard") && normalized.Contains("fork")) return "fauchard fork";
        if (normalized.Contains("fauchard")) return "fauchard";
        if (normalized.Contains("flail") && normalized.Contains("footman")) return "flail footman";
        if (normalized.Contains("flail") && normalized.Contains("horseman")) return "flail horseman";
        if (normalized.Contains("flail") && normalized.Contains("military")) return "flail military";
        if (normalized.Contains("glaive") && normalized.Contains("guisarme")) return "glaive guisarme";
        if (normalized.Equals("glaive")) return "glaive";
        if (normalized.Contains("guisarme") && normalized.Contains("voulge")) return "guisarme voulge";
        if (normalized.Equals("guisarme")) return "guisarme";
        if (normalized.Contains("halberd")) return "halberd";
        if (normalized.Contains("lucern") && normalized.Contains("hammer")) return "hammer lucern";
        if (normalized.Equals("hammer") || normalized.Contains("war hammer")) return "hammer";
        if (normalized.Contains("jo stick")) return "jo stick";
        if (normalized.Contains("lance") && normalized.Contains("heavy")) return "lance heavy horse";
        if (normalized.Contains("lance") && normalized.Contains("light")) return "lance light horse";
        if (normalized.Contains("lance") && normalized.Contains("medium")) return "lance medium horse";
        if (normalized.Contains("mace") && normalized.Contains("footman")) return "mace footman";
        if (normalized.Contains("mace") && normalized.Contains("horseman")) return "mace horseman";
        if (normalized.Contains("morning") && normalized.Contains("star")) return "morning star";
        if (normalized.Contains("partisan")) return "partisan";
        if (normalized.Contains("pick") && normalized.Contains("military") && normalized.Contains("footman")) return "pick military footman";
        if (normalized.Contains("pick") && normalized.Contains("military") && normalized.Contains("horseman")) return "pick military horseman";
        if (normalized.Contains("pike") && normalized.Contains("awl")) return "pike awl";
        if (normalized.Contains("ranseur")) return "ranseur";
        if (normalized.Contains("scimitar")) return "scimitar";
        if (normalized.Equals("spear")) return "spear";
        if (normalized.Contains("spetum")) return "spetum";
        if (normalized.Contains("quarter") && normalized.Contains("staff")) return "staff quarter";
        if (normalized.Contains("bastard") && normalized.Contains("sword")) return "sword bastard";
        if (normalized.Contains("broad") && normalized.Contains("sword")) return "sword broad";
        if (normalized.Contains("long") && normalized.Contains("sword")) return "sword long";
        if (normalized.Contains("short") && normalized.Contains("sword")) return "sword short";
        if (normalized.Contains("two") && normalized.Contains("handed") && normalized.Contains("sword")) return "sword two handed";
        if (normalized.Contains("trident")) return "trident";
        if (normalized.Contains("voulge")) return "voulge";

        return null;
    }

    private static string NormalizeWeaponName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return string.Empty;

        var text = weaponName.Trim().ToLowerInvariant();
        text = Regex.Replace(text, @"\+\d+", string.Empty);
        text = text.Replace("(", " ").Replace(")", " ").Replace(",", " ").Replace("-", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static int GetEffectiveWeaponBonusForHitRequirement(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return 0;

        var baseBonus = Math.Max(0, Math.Max(weapon.MagicBonus, weapon.ToHitBonus));
        if (IsSwordOfSharpness(weapon))
            return Math.Max(baseBonus, 3);
        if (IsSwordVorpalWeapon(weapon))
            return Math.Max(baseBonus, 3);

        return baseBonus;
    }

    private static int GetRequiredWeaponBonusToHit(MonsterInstance target)
    {
        if (target?.Template == null)
            return 0;

        var required = 0;
        foreach (var defense in target.Template.SpecialDefenses)
        {
            var text = defense?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var match = Regex.Match(text, @"\+(\d+)\s*or\s*better\s*weapons\s*to\s*hit", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                required = Math.Max(required, Math.Max(0, parsed));
        }

        return required;
    }

    private void TryResolveSwordOfLifeStealingEffect(Character wielder, MonsterInstance target, List<CombatEvent> events)
    {
        if (wielder == null || target?.Template == null || !target.IsAlive)
            return;

        var hitDieType = Math.Max(2, target.Template.HitDiceType <= 0 ? 8 : target.Template.HitDiceType);
        var drained = _dice.Roll(hitDieType);

        var beforeTargetHp = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - drained);
        var actualDrained = beforeTargetHp - target.CurrentHitPoints;
        if (actualDrained <= 0)
            return;

        // Approximation of one level/hit-die drain: reduce monster offense slightly.
        target.AdjustThac0(1);

        var beforeWielderHp = wielder.CurrentHitPoints;
        var missing = Math.Max(0, wielder.MaxHitPoints - wielder.CurrentHitPoints);
        var gained = Math.Min(missing, actualDrained);
        if (gained > 0)
            wielder.CurrentHitPoints = Math.Min(wielder.MaxHitPoints, wielder.CurrentHitPoints + gained);

        events.Add(new CombatEvent(
            gained > 0
                ? $"Sword of Life Stealing drains {actualDrained} life from {target.DisplayName} (natural 20). {wielder.Name} gains {gained} HP. HP {beforeWielderHp}->{wielder.CurrentHitPoints}."
                : $"Sword of Life Stealing drains {actualDrained} life from {target.DisplayName} (natural 20), but {wielder.Name} is already at full HP."));
    }

    private static bool IsSwordPlusThreeFrostBrand(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Frost Brand", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Frost Brand", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResolveNineLivesStealerEffect(Item? weapon, MonsterInstance target, List<CombatEvent> events)
    {
        if (weapon == null || target?.Template == null)
            return false;

        var charges = GetOrAssignNineLivesStealerCharges(weapon);
        if (charges <= 0)
            return false;

        var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.DisplayName} resists Nine Lives Stealer (save vs spell {saveRoll} vs {saveTarget})."));
            return false;
        }

        SetNineLivesStealerCharges(weapon, charges - 1);
        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"Nine Lives Stealer draws the life force from {target.DisplayName}! Charges remaining: {Math.Max(0, charges - 1)}."));
        return true;
    }

    private static int GetOrAssignNineLivesStealerCharges(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("NineLivesCharges:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing)
            && int.TryParse(existing[(existing.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Max(0, parsed);
        }

        weapon.SpecialAbilities.Add("NineLivesCharges:9");
        return 9;
    }

    private static void SetNineLivesStealerCharges(Item weapon, int charges)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var idx = weapon.SpecialAbilities.FindIndex(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.StartsWith("NineLivesCharges:", StringComparison.OrdinalIgnoreCase));

        var value = $"NineLivesCharges:{Math.Max(0, charges)}";
        if (idx >= 0)
            weapon.SpecialAbilities[idx] = value;
        else
            weapon.SpecialAbilities.Add(value);
    }

    private void TryResolveDaggerOfVenomEffect(Item? weapon, MonsterInstance target, List<CombatEvent> events)
    {
        if (weapon == null || target?.Template == null || !target.IsAlive)
            return;

        if (SpellDamageSaveHelper.HasPoisonOrParalysisImmunity(target))
        {
            events.Add(new CombatEvent($"{target.DisplayName} is immune to poison from {weapon.Name}."));
            return;
        }

        var doses = GetOrAssignDaggerOfVenomDoses(weapon);
        if (doses <= 0)
        {
            events.Add(new CombatEvent($"{weapon.Name} has no venom dose remaining."));
            return;
        }

        var remaining = Math.Max(0, doses - 1);
        SetDaggerOfVenomDoses(weapon, remaining);

        var saveTarget = SpellDamageSaveHelper.GetMonsterPoisonSaveTarget(target, fallbackTarget: 20);
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent(
                $"{target.DisplayName} resists {weapon.Name} poison (save {saveRoll} vs {saveTarget}). Doses remaining: {remaining}."));
            return;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent(
            $"{weapon.Name} injects fatal poison into {target.DisplayName} on a natural 20! (save {saveRoll} vs {saveTarget}, doses remaining: {remaining})."));
    }

    private static bool IsDaggerOfVenom(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Dagger of Venom", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dagger of Venom", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetOrAssignDaggerOfVenomDoses(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("VenomDoses:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing)
            && int.TryParse(existing[(existing.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Max(0, parsed);
        }

        weapon.SpecialAbilities.Add("VenomDoses:6");
        return 6;
    }

    private static void SetDaggerOfVenomDoses(Item weapon, int doses)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var idx = weapon.SpecialAbilities.FindIndex(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.StartsWith("VenomDoses:", StringComparison.OrdinalIgnoreCase));

        var value = $"VenomDoses:{Math.Max(0, doses)}";
        if (idx >= 0)
            weapon.SpecialAbilities[idx] = value;
        else
            weapon.SpecialAbilities.Add(value);
    }

    private static int GetSituationalSwordBonusAgainstTarget(Item? weapon, MonsterInstance target)
    {
        if (IsSwordPlusOneFlameTongue(weapon))
        {
            // Weapon is +1 normally, but:
            // +2 vs regenerating, +3 vs cold-using/inflammable/avian, +4 vs undead.
            if (target?.InstanceMonsterType == MonsterType.Undead)
                return 3;

            if (IsTargetColdUsingCreature(target)
                || IsTargetInflammableCreature(target)
                || IsTargetAvianCreature(target))
            {
                return 2;
            }

            if (IsTargetRegeneratingCreature(target))
                return 1;

            return 0;
        }

        if (IsSwordPlusTwoGiantSlayer(weapon)
            && IsTargetGiantKinForGiantSlayer(target))
        {
            // Weapon is +2 normally, but +3 vs giant-kind targets.
            return 1;
        }

        if (IsSwordPlusTwoDragonSlayer(weapon)
            && IsTargetEligibleForDragonSlayerBonus(target))
        {
            // Weapon is +2 normally, but +4 vs true dragons.
            return 2;
        }

        if (IsSwordPlusThreeFrostBrand(weapon)
            && IsTargetFireUsingOrDwellingCreature(target))
        {
            // Weapon is +3 normally, but +6 vs fire-using/dwelling creatures.
            return 3;
        }

        if (IsSwordPlusOnePlusTwoVsMagicUsingAndEnchantedCreatures(weapon)
            && IsTargetEligibleForSwordPlusTwoBonus(target))
        {
            // Weapon is +1 normally, but +2 vs magic-using/enchanted/conjured-created-gated-summoned creatures.
            return 1;
        }

        if (IsSwordPlusOnePlusThreeVsLycanthropesAndShapeChangers(weapon)
            && IsTargetLycanthropeOrShapeChanger(target))
        {
            // Weapon is +1 normally, but +3 vs lycanthropes and shape changers.
            return 2;
        }

        if (IsSwordPlusOnePlusThreeVsRegeneratingCreatures(weapon)
            && IsTargetRegeneratingCreature(target))
        {
            // Weapon is +1 normally, but +3 vs regenerating creatures.
            return 2;
        }

        if (IsSwordPlusOnePlusFourVsReptiles(weapon)
            && IsTargetReptile(target))
        {
            // Weapon is +1 normally, but +4 vs reptiles.
            return 3;
        }

        return 0;
    }

    private static bool IsTargetEligibleForSwordPlusTwoBonus(MonsterInstance target)
    {
        return IsMagicUsingMonster(target)
               || IsConjuredCreatedGatedOrSummoned(target)
               || IsEnchantedCreature(target);
    }

    private static bool IsMagicUsingMonster(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var lowerName = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (lowerName.Contains("magic-user")
            || lowerName.Contains("mage")
            || lowerName.Contains("wizard")
            || lowerName.Contains("illusionist")
            || lowerName.Contains("sorcerer")
            || lowerName.Contains("warlock"))
        {
            return true;
        }

        foreach (var ability in GetMonsterAbilityNames(target))
        {
            var lower = ability.ToLowerInvariant();
            if (lower.Contains("casts ")
                || lower.Contains("spellcasting")
                || lower.Contains("spell casting")
                || lower.Contains("spell use")
                || lower.Contains("magic use")
                || Regex.IsMatch(lower, @"\b(mage|magic-user|illusionist|cleric|priest|druid)\b.*\bspells?\b"))
            {
                return true;
            }
        }

        return target.Template.Attacks.Any(attack =>
            string.Equals(attack.Name?.Trim(), "Spell", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConjuredCreatedGatedOrSummoned(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("conjur")
               || composite.Contains("summon")
               || composite.Contains("gated")
               || composite.Contains("gate")
               || composite.Contains("created");
    }

    private static bool IsEnchantedCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("enchant");
    }

    private static bool IsTargetLycanthropeOrShapeChanger(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var lowerName = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (lowerName.Contains("lycanthrope")
            || lowerName.Contains("were")
            || lowerName.Contains("doppelganger")
            || lowerName.Contains("wolfwer")
            || lowerName.Contains("shape changer")
            || lowerName.Contains("shapechanger"))
        {
            return true;
        }

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("lycanthrope")
               || composite.Contains("shape changer")
               || composite.Contains("shapechanger")
               || composite.Contains("shapechange")
               || composite.Contains("shape change")
               || composite.Contains("shape-shift")
               || composite.Contains("shapeshift")
               || composite.Contains("polymorph");
    }

    private static bool IsTargetRegeneratingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (HasSpecialAbility(target, "Regeneration"))
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("regenerat");
    }

    private static bool IsTargetReptile(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Dragon)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return Regex.IsMatch(composite,
            @"\b(reptil\w*|lizard\w*|snake\w*|serpent\w*|dragons?|drakes?|wyverns?|crocodil\w*|alligators?|turtles?|tortoises?|dinosaurs?|basilisks?|hydras?|tuataras?|cobras?|vipers?|pythons?)\b");
    }

    private static bool IsTargetGiantKinForGiantSlayer(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Giant)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("ettin")
               || composite.Contains("ogre mage")
               || composite.Contains("ogre-mage")
               || composite.Contains("titan");
    }

    private static bool IsTargetEligibleForDragonSlayerBonus(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType != MonsterType.Dragon)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("bahamut") || name.Contains("tiamat"))
            return false;

        return name.Contains("dragon");
    }

    private static bool IsTargetFireUsingOrDwellingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("fire")
               || composite.Contains("flame")
               || composite.Contains("burn")
               || composite.Contains("heat")
               || composite.Contains("lava")
               || composite.Contains("magma")
               || composite.Contains("pyro");
    }

    private static void ApplySwordOfWoundingHealingRestriction(CombatSession session, MonsterInstance target)
    {
        if (session == null || target == null)
            return;

        var blockedHealing = session.GetMonsterWoundingUnhealableDamage(target);
        if (blockedHealing <= 0)
            return;

        var cappedHp = Math.Max(0, target.MaxHitPoints - blockedHealing);
        if (target.CurrentHitPoints > cappedHp)
            target.CurrentHitPoints = cappedHp;
    }

    private static bool IsTargetTrueGiantForGiantSlayer(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();

        return name.Contains("hill giant")
               || name.Contains("stone giant")
               || name.Contains("frost giant")
               || name.Contains("fire giant")
               || name.Contains("cloud giant")
               || name.Contains("storm giant");
    }

    private static bool IsTargetColdUsingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("cold")
               || composite.Contains("frost")
               || composite.Contains("ice")
               || composite.Contains("winter");
    }

    private static bool IsTargetInflammableCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Plant)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("inflammable")
               || composite.Contains("flammable")
               || composite.Contains("combustible")
               || composite.Contains("burns easily");
    }

    private static bool IsTargetAvianCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("avian")
               || composite.Contains("bird")
               || composite.Contains("eagle")
               || composite.Contains("hawk")
               || composite.Contains("owl")
               || composite.Contains("vulture")
               || composite.Contains("raven")
               || composite.Contains("falcon")
               || composite.Contains("roc");
    }

    private static bool RequiresPlusOneWeaponToHit(MonsterInstance target)
    {
        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "+1 or better weapons to hit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWillOWisp(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var normalized = (target.Template.Name ?? string.Empty)
            .ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        return normalized.Contains("willowisp", StringComparison.Ordinal);
    }

    private static bool IsMagicalWeapon(Item? mainHand)
    {
        if (mainHand == null || mainHand.Type != ItemType.Weapon)
            return false;

        var hasPlusInName = !string.IsNullOrWhiteSpace(mainHand.Name)
            && mainHand.Name.Contains('+', StringComparison.Ordinal);
        var hasSpecialAbilities = mainHand.SpecialAbilities != null
            && mainHand.SpecialAbilities.Any(a => !string.IsNullOrWhiteSpace(a));

        return hasPlusInName || hasSpecialAbilities;
    }

    private static bool IsMonsterAttackConsideredMagical(MonsterInstance monster, Adnd.Core.Monsters.MonsterAttack attack)
    {
        static bool HasMagicalKeyword(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("magic", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("enchant", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("spell", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("elemental", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("demon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("devil", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("dragon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (HasMagicalKeyword(attack.Name))
            return true;

        if (monster.Template.SpecialAttacks.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialAbilities.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        return false;
    }

    private static bool MonsterHasMagicalProperties(MonsterInstance monster)
    {
        static bool HasMagicalKeyword(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("magic", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("enchant", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("spell", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("summon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("conjur", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("elemental", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("demon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("devil", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("dragon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("undead", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (monster.InstanceMonsterType == Adnd.Core.Monsters.MonsterType.Undead)
            return true;

        if (HasMagicalKeyword(monster.Template.Name))
            return true;

        if (monster.Template.SpecialAbilities.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialAttacks.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialDefenses.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        return false;
    }
}
