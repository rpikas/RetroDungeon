using Adnd.Core.Characters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Dices;
using Adnd.Core.Diagnostics;
using Adnd.Core.Experience;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Core.Treasure;
using Adnd.Data.Characters;
using Adnd.Data.Encounters.Factories;
using Adnd.Data.Items;
using Adnd.Data.Monsters;
using Adnd.Data.Party;
using Adnd.Data.Spells;
using Adnd.Data.Treasure;
using Adnd.Game.Viewer;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Adnd.Game.Combat;

public sealed class CombatCoordinator
{
    private readonly EncounterMonsterFactory _monsterFactory = new();
    private readonly CombatResolver _combatResolver;
    private readonly CharacterSavingThrowService _savingThrowService = new();
    private readonly PartyRepository _partyRepository = new();
    private readonly LevelUpService _levelUpService = new();
    private readonly SpellRepository _spellRepository = new("Data/Spells");
    private readonly SpellCastingService _spellCastingService;
    private readonly TreasureService _treasureService;
    private readonly ChestTrapTableProvider _chestTrapTableProvider;
    private readonly ItemRepository _itemRepository = new("Data/Items");
    private readonly MonsterRepository _monsterRepository = new();
    private readonly Random _random = new();
    private readonly IDice _dice = new SystemDice();

    /// <summary>
    /// Raised when an encounter is set up, before the first round is fought. Observers only —
    /// combat does not wait on them and nothing they do changes its outcome.
    /// </summary>
    public event Action<CombatSession>? EncounterStarted;

    /// <summary>Raised once the player has chosen this round's actions, before they resolve.</summary>
    public event Action<CombatSession, IReadOnlyDictionary<string, CombatAction>>? ActionsChosen;

    /// <summary>
    /// Raised after a round resolves. Carries only the session: what changed is read by comparing
    /// it against the previous state, because <see cref="CombatEvent"/> is a display string and
    /// nothing structured can be recovered from it.
    /// </summary>
    public event Action<CombatSession>? RoundResolved;

    /// <summary>
    /// Raised when the open choice inside a fight changes -- a different character is up, or the legal
    /// actions differ. Forwarded straight from the encounter dialog, so whoever publishes snapshots
    /// never needs a reference to that dialog. Null means there is nothing left to ask.
    /// </summary>
    public event Action<CombatSession, ViewerPrompt?>? ViewerPromptChanged;

    public CombatCoordinator()
    {
        var spellRepo = new SpellRepository("Data/Spells");
        var resolver = new SpellResolver(new ISpellEffectHandler[]
        {
            new CureLightWoundsHandler(),
            new BarkskinHandler(),
            new CureSeriousWoundsHandler(),
            new CureCriticalWoundsHandler(),
            new RemoveParalysisHandler(),
            new HealHandler(),
            new NeutralizePoisonHandler(),
            new CureDiseaseHandler(),
            new RaiseDeadHandler(),
            new ResurrectionHandler(),
            new SpiritualHammerHandler(),
            new GlyphOfWardingHandler(),
            new FlameStrikeHandler(),
            new InsectPlagueHandler(),
            new SummonInsectsHandler(),
            new SnareHandler(),
            new PyrotechnicsHandler(),
            new CallLightningHandler(),
            new EntangleHandler(),
            new Silence15RadiusHandler(),
            new FindTrapsHandler(),
            new FaerieFireHandler(),
            new BladeBarrierHandler(),
            new MagicMissileHandler(),
            new ChromaticOrbHandler(),
            new ShockingGraspHandler(),
            new MelfsAcidArrowHandler(),
            // Handles both cleric and magic-user Hold Person spell IDs.
            new HoldPersonHandler(),
            new HoldMonsterHandler(),
            new BlessHandler(),
            new SleepHandler(),
            new StrengthHandler(),
            new HasteHandler(),
            new SlowHandler(),
            new MirrorImageHandler(),
            new InvisibilityHandler(),
            new ShieldSpellHandler(),
            new FireballHandler(),
            new WallOfFireHandler(),
            new WallOfThornsHandler(),
            new FeeblemindHandler(),
            new LightningBoltHandler(),
            new IceStormHandler(),
            new CloudkillHandler(),
            new DisintegrateHandler(),
            new DeathFogHandler(),
            new DelayedBlastFireballHandler(),
            new EarthquakeHandler(),
            new FireStormHandler(),
            new UnholyWordHandler(),
            new FingerOfDeathHandler(),
            new IncendiaryCloudHandler(),
            new MeteorSwarmHandler(),
            new PowerWordStunHandler(),
            new PowerWordKillHandler(),
            new ColorSprayHandler(),
            new FearHandler(),
            new ParalyzationHandler(),
            new PhantasmalForceHandler(),
        });

        _spellCastingService = new SpellCastingService(resolver, spellRepo.LoadAll());
        _combatResolver = new CombatResolver(spellCastingService: _spellCastingService);

        var treasureRepo = new TreasureTableRepository("Data/Treasure");
        _treasureService = new TreasureService(treasureRepo, _random);
        _chestTrapTableProvider = new ChestTrapTableProvider(Path.Combine("Data", "Treasure", "chest-traps.json"));
    }

    public CombatOutcome StartEncounter(IWin32Window owner, string monsterName, int monsterCount, List<Character> party, CharacterRepository characterRepository, int? dungeonLevel = null)
    {
        var monsters = _monsterFactory.CreateGroup(monsterName, monsterCount);
        var session = new CombatSession(party, monsters);
        RestorePersistedRoundEffects(session);
        ResolveEncounterSurprise(session);
        EncounterStarted?.Invoke(session);

        while (session.Outcome == CombatOutcome.InProgress)
        {
            if (!session.AliveParty.Any())
            {
                session.Outcome = CombatOutcome.Defeat;
                break;
            }

            var aliveMonsters = session.AliveMonsters.ToList();
            var hasMultipleGroups = session.GetDistinctGroupIds().Take(2).Count() > 1;
            var asleepMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Asleep));
            var heldMonstersLegacy = 0;
            var entangledMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Entangled));
            var panickedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Panicked));
            var fearedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Feared));
            var turnedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.TurnedUndead));
            var blindedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Blinded));
            var confusedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Confused));
            var stunnedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Stunned));
            var slowedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Slowed));
            var paralyzedMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
            var unconsciousMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Unconscious));
            var monsterTemplate = aliveMonsters.FirstOrDefault()?.Template;
            using var encounterForm = hasMultipleGroups
                ? new EncounterForm(session, dungeonLevel)
                : new EncounterForm(monsterName, aliveMonsters.Count, asleepMonsters, heldMonstersLegacy, entangledMonsters, panickedMonsters,
                    fearedMonsters, turnedMonsters, blindedMonsters, confusedMonsters, stunnedMonsters, slowedMonsters, paralyzedMonsters, unconsciousMonsters,
                    session.Party, session.RoundNumber, dungeonLevel, monsterTemplate, session);
            //    public EncounterForm(string monsterName, int monsterCount, int asleepMonsterCount, int heldMonsterCount, int entangledMonsterCount, int panickedMonsterCount,
            //    int fearedMonsterCount, int turnedMonsterCount, int blindedMonsterCount, int confusedMonsterCount, int stunnedMonsterCount, int slowedMonsterCount, int paralyzedMonsterCount,
            //    int unconsciousMonsterCount, List<Character> party, int roundNumber, int? dungeonLevel = null, Monster? monsterTemplate = null, CombatSession? session = null)

            encounterForm.ViewerPromptChanged += prompt => ViewerPromptChanged?.Invoke(session, prompt);
            var dialogResult = encounterForm.ShowDialog(owner);
            if (dialogResult != DialogResult.OK)
            {
                // If player closes dialog, treat as escape to avoid dead-end.
                session.Outcome = CombatOutcome.Escaped;
                break;
            }

            ActionsChosen?.Invoke(session, encounterForm.SelectedActions);

            var roundEvents = _combatResolver.ResolveRound(session, encounterForm.SelectedActions);
            HandleRotGrubFlamePrompts(owner, session, roundEvents, characterRepository);
            ApplyShriekReinforcements(session, roundEvents, dungeonLevel);
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory
)
        {
            ApplyVictoryRewards(owner, session, characterRepository, dungeonLevel);
        }

        RemoveTemporaryCombatEffects(session);

        foreach (var character in party)
            characterRepository.Save(character);

        MoveDeadPartyMembersToEnd(session.Party);
        ShowFinalOutcome(owner, session.Outcome, session);
        return session.Outcome;
    }

    public CombatOutcome StartEncounterWithGroupCounts(IWin32Window owner, List<(string name, int count)> groups, List<Character> party, CharacterRepository characterRepository, int? dungeonLevel = null)
    {
        var normalized = groups
            .Where(g => !string.IsNullOrWhiteSpace(g.name))
            .Select(g => (g.name, Math.Max(1, g.count)))
            .ToList();

        if (normalized.Count == 0)
            return CombatOutcome.Escaped;

        var monsters = _monsterFactory.CreateMultipleGroups(normalized);
        var session = new CombatSession(party, monsters);
        RestorePersistedRoundEffects(session);
        ResolveEncounterSurprise(session);
        EncounterStarted?.Invoke(session);

        while (session.Outcome == CombatOutcome.InProgress)
        {
            if (!session.AliveParty.Any())
            {
                session.Outcome = CombatOutcome.Defeat;
                break;
            }

            using var encounterForm = new EncounterForm(session, dungeonLevel);
            encounterForm.ViewerPromptChanged += prompt => ViewerPromptChanged?.Invoke(session, prompt);
            var dialogResult = encounterForm.ShowDialog(owner);
            if (dialogResult != DialogResult.OK)
            {
                session.Outcome = CombatOutcome.Escaped;
                break;
            }

            ActionsChosen?.Invoke(session, encounterForm.SelectedActions);

            var roundEvents = _combatResolver.ResolveRound(session, encounterForm.SelectedActions);
            HandleRotGrubFlamePrompts(owner, session, roundEvents, characterRepository);
            ApplyShriekReinforcements(session, roundEvents, dungeonLevel);
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
            ApplyVictoryRewards(owner, session, characterRepository, dungeonLevel);

        RemoveTemporaryCombatEffects(session);

        foreach (var character in party)
            characterRepository.Save(character);

        MoveDeadPartyMembersToEnd(session.Party);
        ShowFinalOutcome(owner, session.Outcome, session);
        return session.Outcome;
    }

    public CombatOutcome StartEncounterWithMultipleGroups(IWin32Window owner, string[] monsterNames, List<Character> party, CharacterRepository characterRepository, MonsterRepository monsterRepository, int? dungeonLevel = null)
    {
        var groups = new List<(string name, int count)>();
        foreach (var monsterName in monsterNames)
        {
            var monster = monsterRepository.GetAll().FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));
            int count;
            if (monster != null)
            {
                count = _random.Next(monster.NumberOfAppearancesMin, monster.NumberOfAppearancesMax + 1);
            }
            else
            {
                count = _random.Next(1, 4); // Smaller groups when multiple
            }
            groups.Add((monsterName, count));
        }

        var monsters = _monsterFactory.CreateMultipleGroups(groups);
        var session = new CombatSession(party, monsters);
        RestorePersistedRoundEffects(session);
        ResolveEncounterSurprise(session);
        EncounterStarted?.Invoke(session);

        while (session.Outcome == CombatOutcome.InProgress)
        {
            if (!session.AliveParty.Any())
            {
                session.Outcome = CombatOutcome.Defeat;
                break;
            }

            using var encounterForm = new EncounterForm(session, dungeonLevel);
            encounterForm.ViewerPromptChanged += prompt => ViewerPromptChanged?.Invoke(session, prompt);
            var dialogResult = encounterForm.ShowDialog(owner);
            if (dialogResult != DialogResult.OK)
            {
                session.Outcome = CombatOutcome.Escaped;
                break;
            }

            ActionsChosen?.Invoke(session, encounterForm.SelectedActions);

            var roundEvents = _combatResolver.ResolveRound(session, encounterForm.SelectedActions);
            HandleRotGrubFlamePrompts(owner, session, roundEvents, characterRepository);
            ApplyShriekReinforcements(session, roundEvents, dungeonLevel);
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
        {
            ApplyVictoryRewards(owner, session, characterRepository, dungeonLevel);
        }

        RemoveTemporaryCombatEffects(session);

        foreach (var character in party)
            characterRepository.Save(character);

        MoveDeadPartyMembersToEnd(session.Party);
        ShowFinalOutcome(owner, session.Outcome, session);
        return session.Outcome;
    }

    private void ResolveEncounterSurprise(CombatSession session)
    {
        if (session.SurpriseResolved)
            return;
      
        var partyRoll = _dice.Roll(6);
        var partyThreshold = 2;
        var partySurprisesMonsters = partyRoll <= partyThreshold;

        var specialThreshold = GetMonsterSpecialSurpriseThreshold(session.Monsters);
        var monstersRollUsesD100 = specialThreshold.HasValue && specialThreshold.Value >= 95;

        var monstersRoll = monstersRollUsesD100 ? _dice.Roll(100) : _dice.Roll(6);
        var monsterThreshold = monstersRollUsesD100
            ? specialThreshold!.Value
            : Math.Max(2, specialThreshold ?? 2);
        var monstersSurpriseParty = monstersRoll <= monsterThreshold;
        /*
      if (partySurprisesMonsters && monstersSurpriseParty)
      {
          partySurprisesMonsters = false;
          monstersSurpriseParty = false;
      }
      */

        // --- NEW RULE: If one rolls 1 and the other rolls 2, the one with 2 wins surprise ---
        if (!monstersRollUsesD100) // only applies to d6 logic
        {
            bool partyLow = partyRoll == 1;
            bool monsterLow = monstersRoll == 1;
            bool partyMid = partyRoll == 2;
            bool monsterMid = monstersRoll == 2;

            if (partyLow && monsterMid)
            {
                // Monster wins surprise
                partySurprisesMonsters = false;
                monstersSurpriseParty = true;
            }
            else if (monsterLow && partyMid)
            {
                // Party wins surprise
                partySurprisesMonsters = true;
                monstersSurpriseParty = false;
            }
        }

        // Original rule: if both surprise each other, cancel surprise
        if (partySurprisesMonsters && monstersSurpriseParty)
        {
            partySurprisesMonsters = false;
            monstersSurpriseParty = false;
        }

        session.PartySurprisedRound1 = monstersSurpriseParty;
        session.MonstersSurprisedRound1 = partySurprisesMonsters;
        session.SurpriseResolved = true;

        var monsterRollDice = monstersRollUsesD100 ? "1d100" : "1d6";
        RuleApplicationInfo.Publish(
            "DMG",
            "62",
            "Party start",
            "Before round 1, party rolls 1d6. Monster side rolls 1d6, except special monsters may improve surprise chance. If both surprise each other, treat as no surprise.",
            "1",
            "6",
            partyRoll.ToString(),
            partySurprisesMonsters ? "Party surprises monsters." : "Party does not surprise monsters.");

        RuleApplicationInfo.Publish(
            "DMG",
            "62",
            "Encounter start",
            $"Monster side roll uses {monsterRollDice}. Threshold is {monsterThreshold}.",
            "1",
            monstersRollUsesD100 ? "100" : "6",
            monstersRoll.ToString(),
            monstersSurpriseParty ? "Monsters surprise party." : "Monsters do not surprise party.");

        if (session.PartySurprisedRound1 || session.MonstersSurprisedRound1)
        {
            session.SurpriseSummary = session.PartySurprisedRound1
                ? "Party surprised"
                : "Monsters surprised";
        }
        else
        {
            session.SurpriseSummary = "No surprise";
        }
    }

    private static int? GetMonsterSpecialSurpriseThreshold(IEnumerable<MonsterInstance> monsters)
    {
        var threshold = 0;

        foreach (var monster in monsters)
        {
            var name = monster.Template.Name?.Trim() ?? string.Empty;
            if (name.Equals("Piercer", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Piercer ", StringComparison.OrdinalIgnoreCase))
            {
                threshold = Math.Max(threshold, 95);
                continue;
            }

            if (name.Equals("Xorn", StringComparison.OrdinalIgnoreCase))
                threshold = Math.Max(threshold, 5);
            else if (name.Equals("Bugbear", StringComparison.OrdinalIgnoreCase)
                     || name.Equals("Ghoul", StringComparison.OrdinalIgnoreCase)
                     || name.Equals("Su-Monster", StringComparison.OrdinalIgnoreCase)
                     || name.Equals("Su Monster", StringComparison.OrdinalIgnoreCase))
                threshold = Math.Max(threshold, 3);
            else if (name.Equals("Giant Spider", StringComparison.OrdinalIgnoreCase))
                threshold = Math.Max(threshold, 4);
        }

        return threshold > 0 ? threshold : null;
    }

    private static void RestorePersistedRoundEffects(CombatSession session)
    {
        foreach (var c in session.Party)
        {
            var persistedRounds = c.TemporaryStrengthRoundsRemaining;
            var persistedBonus = c.TemporaryStrengthBonus;
            if (persistedRounds > 0 && persistedBonus > 0 && session.GetStrengthBuffRounds(c.Name) <= 0)
            {
                session.SetStrengthBuff(c.Name, persistedBonus, persistedRounds);
            }
        }
    }

    private static void RemoveTemporaryCombatEffects(CombatSession session)
    {
        foreach (var c in session.Party)
        {
            if (c.HasStatus(CharacterStatus.Asleep))
                c.RemoveStatus(CharacterStatus.Asleep);
        }

        session.AsleepPartyRounds.Clear();

        foreach (var name in session.BlessedPartyMembers)
        {
            var c = session.Party.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c != null)
                c.ArmorClass += 1;
        }

        foreach (var name in session.InvisiblyBuffedPartyMembers)
        {
            var c = session.Party.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null)
                continue;

            if (c.HasStatus(CharacterStatus.Invisible))
            {
                c.RemoveStatus(CharacterStatus.Invisible);
                c.ArmorClass += 4;
            }
        }

        foreach (var name in session.ImprovedInvisibilityRounds.Keys.ToList())
        {
            var c = session.Party.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null)
                continue;

            if (c.HasStatus(CharacterStatus.Invisible))
            {
                c.RemoveStatus(CharacterStatus.Invisible);
                c.ArmorClass += 4;
            }
        }

        foreach (var name in session.StrengthBuffBonuses.Keys.ToList())
            session.ClearStrengthBuff(name);

        foreach (var name in session.BarkskinBonuses.Keys.ToList())
        {
            var c = session.Party.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null)
                continue;

            var bonus = session.GetBarkskinBonus(name);
            if (bonus > 0)
                c.ArmorClass += bonus;

            session.ClearBarkskin(name);
        }

        foreach (var name in session.FaerieFiredPartyMembers.ToList())
        {
            var c = session.Party.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null)
                continue;

            c.ArmorClass -= 2;
        }

        session.BlessedPartyMembers.Clear();
        session.InvisiblyBuffedPartyMembers.Clear();
        session.ImprovedInvisibilityRounds.Clear();
        session.BarkskinBonuses.Clear();
        session.BarkskinRounds.Clear();
        session.FaerieFiredPartyMembers.Clear();
        session.MirrorImageCounts.Clear();
        session.MirrorImageRounds.Clear();
    }
   
    private void ApplyVictoryRewards(IWin32Window owner, CombatSession session, CharacterRepository characterRepository, int? dungeonLevel)
    {
        var survivors = session.Party
            .Where(c => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead))
            .ToList();

        if (survivors.Count == 0)
            return;

        // --- NYTT: Gruppera monster ---
        var monsterGroups = session.Monsters
            .GroupBy(m => m.GroupId)
            .ToList();

        // --- NYTT: Beräkna XP per grupp ---
        var xpCalculator = new XpCalculator();
        var groupXpInfos = monsterGroups.Select(g =>
        {
            var monsters = g.ToList();
            var first = monsters.First().Template;

            var groupFledAfterTheft = session.NoXpMonsterGroupIds.Contains(g.Key);
            if (groupFledAfterTheft)
            {
                return new
                {
                    GroupId = g.Key,
                    MonsterName = first.Name,
                    Count = monsters.Count,
                    XPPerHP = 0,
                    TotalHP = monsters.Sum(m => m.MaxHitPoints),
                    TotalXP = 0,
                    NoXpReason = "Stole from party and fled"
                };
            }

            int xpPerHp = first.XPValuePerHitPoint;
            if (xpPerHp == 0)
                xpPerHp = xpCalculator.GetHpXp(first.HitDice, 1);
            int totalHp = monsters.Sum(m => m.MaxHitPoints);

            int totalXp = monsters.Sum(m =>
                Math.Max(0, m.Template.BaseXPValue + xpPerHp * m.MaxHitPoints));

            return new
            {
                GroupId = g.Key,
                MonsterName = first.Name,
                Count = monsters.Count,
                XPPerHP = xpPerHp,
                TotalHP = totalHp,
                TotalXP = totalXp,
                NoXpReason = string.Empty
            };
        }).ToList();

        // --- NYTT: Summera XP från alla grupper ---
        int totalMonsterXp = groupXpInfos.Sum(g => g.TotalXP);

        // --- Treasure ---
        var treasure = _treasureService.RollTreasureForEncounter(session.Monsters);
        var hasInLairTreasure = treasure.Lair.CopperPieces > 0
                               || treasure.Lair.SilverPieces > 0
                               || treasure.Lair.ElectrumPieces > 0
                               || treasure.Lair.GoldPieces > 0
                               || treasure.Lair.PlatinumPieces > 0
                               || treasure.Lair.Gems.Count > 0
                               || treasure.Lair.Jewelry.Count > 0
                               || treasure.Lair.Art.Count > 0
                               || treasure.Lair.MagicPlaceholders.Count > 0;

        var dungeonDepth = Math.Max(1, dungeonLevel ?? 1);
        var chestResolution = new LairChestResolutionResult { IncludeLairTreasure = true, TrapType = ChestTrapType.None };

        if (hasInLairTreasure)
        {
            var rolledTrap = _chestTrapTableProvider.RollTrap(dungeonDepth, _random, out var trapRollInfo);
            RuleApplicationInfo.Publish(trapRollInfo);
            chestResolution = ResolveLairChestInteraction(owner, session, survivors, dungeonDepth, rolledTrap);
        }

        var effectiveTreasure = BuildEffectiveTreasure(treasure, chestResolution.IncludeLairTreasure);

        // --- XP-fördelning ---
        var xpMultiplier = GameRulesProvider.Current.XpMultiplier;
        int xpEach = (int)Math.Round(totalMonsterXp * xpMultiplier / survivors.Count);
        int xpRemainder = totalMonsterXp % survivors.Count;
        int gemJewelryXpPool = Math.Max(0, effectiveTreasure.TotalGemValueGp + effectiveTreasure.TotalJewelryValueGp);
        int goldXpPool = Math.Max(0, effectiveTreasure.GoldPieces) + gemJewelryXpPool;
        int goldXpEach = goldXpPool / survivors.Count;
        int goldXpRemainder = goldXpPool % survivors.Count;


        var levelUpResults = new List<LevelUpResult>();
        var baseXpByCharacter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bonusXpByCharacter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var goldXpByCharacter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var classLevelsBeforeByCharacter = new Dictionary<string, Dictionary<CharacterClass, int>>(StringComparer.OrdinalIgnoreCase);
        var allSpells = _spellRepository.LoadAll();

        for (int i = 0; i < survivors.Count; i++)
        {
            var survivor = survivors[i];
            classLevelsBeforeByCharacter[survivor.Name] = survivor.Classes
                .Distinct()
                .ToDictionary(cls => cls, cls => survivor.GetClassLevel(cls));

            var baseGain = xpEach + (i < xpRemainder ? 1 : 0);
            var goldXpGain = goldXpEach + (i < goldXpRemainder ? 1 : 0);
            var xpModifierPercent = XpBonusCalculator.GetXpModifier(survivor.Class, survivor.Abilities);
            var individualBonus = (int)Math.Round(baseGain * (xpModifierPercent / 100.0), MidpointRounding.AwayFromZero);

            baseXpByCharacter[survivor.Name] = baseGain;
            bonusXpByCharacter[survivor.Name] = individualBonus;
            goldXpByCharacter[survivor.Name] = goldXpGain;

            var gain = baseGain + individualBonus + goldXpGain;
            if (gain < 0)
                gain = 0;

            levelUpResults.Add(_levelUpService.ApplyExperienceAndAutoLevel(survivor, gain, allSpells));
        }

        var totalAwardedXp = levelUpResults.Sum(r => r.ExperienceAfter - r.ExperienceBefore);

        DistributeCoin(survivors, effectiveTreasure.CopperPieces, (c, amount) => c.CopperPieces += amount);
        DistributeCoin(survivors, effectiveTreasure.SilverPieces, (c, amount) => c.SilverPieces += amount);
        DistributeCoin(survivors, effectiveTreasure.ElectrumPieces, (c, amount) => c.ElectrumPieces += amount);
        DistributeCoin(survivors, effectiveTreasure.GoldPieces, (c, amount) => c.GoldPieces += amount);
        DistributeCoin(survivors, effectiveTreasure.PlatinumPieces, (c, amount) => c.PlatinumPieces += amount);

        var valuablesValueGp = effectiveTreasure.TotalGemValueGp + effectiveTreasure.TotalJewelryValueGp + effectiveTreasure.TotalArtValueGp;
        DistributeCoin(survivors, valuablesValueGp, (c, amount) => c.GoldPieces += amount);

        var magicAward = AwardMagicItemsFromPlaceholders(survivors, effectiveTreasure.MagicPlaceholders, dungeonLevel);
        var wornEquipmentAward = AwardWornEquipmentMagicItems(session, survivors, dungeonLevel);
        var randomItemsAward = AwardRandomItemsAfterCombat(survivors, dungeonLevel);

        if (chestResolution.TriggeredAlarmEncounter)
        {
            TriggerAlarmEncounter(owner, session, characterRepository, dungeonLevel);
        }

        // --- Logg ---
        var sb = new StringBuilder();
        sb.AppendLine("Victory Rewards");
        sb.AppendLine();

        sb.AppendLine("Monster Groups Defeated:");
        foreach (var g in groupXpInfos)
        {
            if (g.Count == 1)
                sb.AppendLine($"{g.Count} {g.MonsterName}");
            else
                sb.AppendLine($"{g.GroupId}: {g.Count}x {g.MonsterName}");
            sb.AppendLine($"  Base XP per monster: {session.Monsters.First(m => m.GroupId == g.GroupId).Template.BaseXPValue}");
            sb.AppendLine($"  XP per HP: {g.XPPerHP}");
            sb.AppendLine($"  Total HP: {g.TotalHP}");
            sb.AppendLine($"  Total XP (group) before multiplier: {g.TotalXP}");
            sb.AppendLine($"  Total XP (group) after multiplier: {(int)Math.Round(g.TotalXP * xpMultiplier)}");
            if (!string.IsNullOrWhiteSpace(g.NoXpReason))
                sb.AppendLine($"  XP note: {g.NoXpReason}.");
            sb.AppendLine();
        }
        sb.AppendLine($"Total XP from all groups: {totalMonsterXp}");
        sb.AppendLine($"XP multiplier: x{xpMultiplier:0.##}");
        sb.AppendLine($"Gold XP bonus: {goldXpPool} XP total (1 XP per GP found; includes {effectiveTreasure.GoldPieces} from GP coins and {gemJewelryXpPool} from gem/jewelry value), split {goldXpEach} each with {goldXpRemainder} remainder");
        sb.AppendLine($"Total awarded XP: {totalAwardedXp}");
        sb.AppendLine($"Survivors: {survivors.Count}");
        sb.AppendLine();
        sb.AppendLine("XP awards:");
        foreach (var r in levelUpResults)
        {
            var gain = r.ExperienceAfter - r.ExperienceBefore;
            var survivor = survivors.FirstOrDefault(s => string.Equals(s.Name, r.CharacterName, StringComparison.OrdinalIgnoreCase));
            var xpModifierPercent = survivor == null ? 0 : XpBonusCalculator.GetXpModifier(survivor.Class, survivor.Abilities);
            baseXpByCharacter.TryGetValue(r.CharacterName, out var baseGain);
            bonusXpByCharacter.TryGetValue(r.CharacterName, out var bonusGain);
            goldXpByCharacter.TryGetValue(r.CharacterName, out var goldXpGain);

            if (survivor != null && survivor.Classes.Count > 1)
            {
                var classProgress = survivor.Classes
                    .Select(cls =>
                    {
                        var classLevel = survivor.GetClassLevel(cls);
                        var classXp = survivor.GetClassExperience(cls);
                        var nextThreshold = ExperienceTable.GetThresholdForLevel(cls, classLevel + 1);
                        var need = Math.Max(0, nextThreshold - classXp);
                        return $"{cls.ToDisplayString()}: has {classXp} XP, needs {need} XP";
                    });

                sb.AppendLine($"- {r.CharacterName}: +{gain} XP (combat {baseGain} + class bonus {bonusGain} [{xpModifierPercent:+#;-#;0}%] + GP XP {goldXpGain}; total {r.ExperienceAfter}; {string.Join(" | ", classProgress)})");
            }
            else
            {
                var classForProgress = survivor?.Class ?? CharacterClass.Fighter;
                var nextLevelThreshold = ExperienceTable.GetThresholdForLevel(classForProgress, r.NewLevel + 1);
                var xpToNextLevel = Math.Max(0, nextLevelThreshold - r.ExperienceAfter);
                sb.AppendLine($"- {r.CharacterName}: +{gain} XP (combat {baseGain} + class bonus {bonusGain} [{xpModifierPercent:+#;-#;0}%] + GP XP {goldXpGain}; total {r.ExperienceAfter}; need {xpToNextLevel} XP for next level)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Treasure found:");
        sb.AppendLine($"- Coins: {effectiveTreasure.CopperPieces} cp, {effectiveTreasure.SilverPieces} sp, {effectiveTreasure.ElectrumPieces} ep, {effectiveTreasure.GoldPieces} gp, {effectiveTreasure.PlatinumPieces} pp");
        sb.AppendLine($"- XP from GP value: {goldXpPool} XP total (1 XP per GP; {effectiveTreasure.GoldPieces} from GP coins + {gemJewelryXpPool} from gem/jewelry value)");

        if (effectiveTreasure.Gems.Count > 0)
            sb.AppendLine($"- Gems: {effectiveTreasure.Gems.Count} (total {effectiveTreasure.TotalGemValueGp} gp)");
        if (effectiveTreasure.Jewelry.Count > 0)
            sb.AppendLine($"- Jewelry: {effectiveTreasure.Jewelry.Count} (total {effectiveTreasure.TotalJewelryValueGp} gp)");
        if (effectiveTreasure.Art.Count > 0)
            sb.AppendLine($"- Art: {effectiveTreasure.Art.Count} (total {effectiveTreasure.TotalArtValueGp} gp)");

        if (hasInLairTreasure && !chestResolution.IncludeLairTreasure)
            sb.AppendLine("- In-lair chest treasure was left behind.");
        if (valuablesValueGp > 0)
            sb.AppendLine($"- Valuables value distributed as gp: {valuablesValueGp} gp");

        if (magicAward.AssignedItems.Count > 0)
        {
            sb.AppendLine("- Magic items awarded:");
            foreach (var assigned in magicAward.AssignedItems)
                sb.AppendLine($"    {assigned.ReceiverName}: {assigned.ItemName}");
        }

        if (magicAward.UnassignedItems.Count > 0)
        {
            sb.AppendLine("- Unclaimed magic items:");
            foreach (var unassigned in magicAward.UnassignedItems)
                sb.AppendLine($"    {unassigned}");
        }

        if (wornEquipmentAward.AssignedItems.Count > 0)
        {
            sb.AppendLine("- Worn equipment magic items:");
            foreach (var assigned in wornEquipmentAward.AssignedItems)
                sb.AppendLine($"    {assigned.ReceiverName}: {assigned.ItemName}");
        }

        if (wornEquipmentAward.UnassignedItems.Count > 0)
        {
            sb.AppendLine("- Unclaimed worn equipment magic items:");
            foreach (var unassigned in wornEquipmentAward.UnassignedItems)
                sb.AppendLine($"    {unassigned}");
        }

        if (randomItemsAward.AssignedItems.Count > 0)
        {
            sb.AppendLine("- Random items found:");
            foreach (var assigned in randomItemsAward.AssignedItems)
                sb.AppendLine($"    {assigned.ReceiverName}: {assigned.ItemName}");
        }

        if (randomItemsAward.UnassignedItems.Count > 0)
        {
            sb.AppendLine("- Unclaimed random items:");
            foreach (var unassigned in randomItemsAward.UnassignedItems)
                sb.AppendLine($"    {unassigned}");
        }

        var leveled = levelUpResults
            .Select(r =>
            {
                var survivor = survivors.FirstOrDefault(s => string.Equals(s.Name, r.CharacterName, StringComparison.OrdinalIgnoreCase));
                if (survivor == null)
                    return new { Result = r, Survivor = (Character?)null, ClassChanges = new List<(CharacterClass Class, int OldLevel, int NewLevel)>() };

                var beforeLevels = classLevelsBeforeByCharacter.TryGetValue(r.CharacterName, out var map)
                    ? map
                    : new Dictionary<CharacterClass, int>();

                var classChanges = survivor.Classes
                    .Distinct()
                    .Select(cls =>
                    {
                        var oldLevel = beforeLevels.TryGetValue(cls, out var lvl) ? lvl : survivor.GetClassLevel(cls);
                        var newLevel = survivor.GetClassLevel(cls);
                        return (Class: cls, OldLevel: oldLevel, NewLevel: newLevel);
                    })
                    .Where(x => x.NewLevel > x.OldLevel)
                    .ToList();

                return new { Result = r, Survivor = survivor, ClassChanges = classChanges };
            })
            .Where(x => x.ClassChanges.Count > 0)
            .ToList();

        if (leveled.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Level ups:");

            foreach (var entry in leveled)
            {
                var r = entry.Result;
                if (entry.Survivor == null || entry.Survivor.Classes.Count <= 1)
                {
                    sb.AppendLine($"- {r.CharacterName}: L{r.OldLevel} -> L{r.NewLevel} (HP +{r.HitPointsGained})");
                }
                else
                {
                    var first = true;
                    foreach (var change in entry.ClassChanges)
                    {
                        var hpGain = first ? r.HitPointsGained : 0;
                        sb.AppendLine($"- {r.CharacterName}: {change.Class.ToDisplayString()} L{change.OldLevel} -> L{change.NewLevel} (HP +{hpGain})");
                        first = false;
                    }
                }

                foreach (var change in r.SpellSlotChanges)
                {
                    var oldSlots = change.OldSlots.Count == 0 ? "none" : string.Join(",", change.OldSlots);
                    var newSlots = change.NewSlots.Count == 0 ? "none" : string.Join(",", change.NewSlots);
                    sb.AppendLine($"    {change.SpellClass} slots: [{oldSlots}] -> [{newSlots}]");
                }

                if (r.SpellsLearned.Count > 0)
                {
                    sb.AppendLine($"    Spells learned: {string.Join(", ", r.SpellsLearned)}");
                }
            }
        }

        Say(owner, "Combat Rewards", sb.ToString(), session);
    }


    private MagicAwardResult AwardMagicItemsFromPlaceholders(List<Character> survivors, List<TreasureMagicPlaceholderResult> placeholders, int? dungeonLevel)
    {
        var result = new MagicAwardResult();
        if (survivors.Count == 0 || placeholders == null || placeholders.Count == 0)
            return result;

        var allItems = FilterItemsByDungeonLevelCostCap(_itemRepository.LoadAll().ToList(), dungeonLevel);
        var miscMagicNames = LoadItemNamesFromFile("MiscMagic.json");
        if (allItems.Count == 0)
            return result;

        var nextReceiverIndex = 0;

        foreach (var placeholder in placeholders)
        {
            var rolls = Math.Max(0, placeholder.Count);
            for (int i = 0; i < rolls; i++)
            {
                var resolvedTable = ResolveAnyMagicTable(placeholder.Table, out var anyRollInfo);
                if (!string.IsNullOrWhiteSpace(anyRollInfo))
                    RuleApplicationInfo.Publish(anyRollInfo!);

                var pool = GetItemPoolForMagicTable(allItems, resolvedTable, miscMagicNames);
                if (pool.Count == 0)
                {
                    result.UnassignedItems.Add($"{resolvedTable} (no matching item defined)");
                    continue;
                }

                var rolled = pool[_random.Next(pool.Count)];
                var item = CloneItem(rolled);

                var assigned = false;
                for (int attempt = 0; attempt < survivors.Count; attempt++)
                {
                    var idx = (nextReceiverIndex + attempt) % survivors.Count;
                    var receiver = survivors[idx];
                    if (!receiver.CanCarry(item))
                        continue;

                    receiver.Inventory.Add(item);
                    result.AssignedItems.Add(new AssignedMagicItem
                    {
                        ReceiverName = receiver.Name,
                        ItemName = item.Name
                    });

                    nextReceiverIndex = (idx + 1) % survivors.Count;
                    assigned = true;
                    break;
                }

                if (!assigned)
                    result.UnassignedItems.Add(item.Name + " (no one can carry)");
            }
        }

        return result;
    }

    private MagicAwardResult AwardWornEquipmentMagicItems(CombatSession session, List<Character> survivors, int? dungeonLevel)
    {
        var result = new MagicAwardResult();
        if (session.Monsters.Count == 0 || survivors.Count == 0)
            return result;

        var allItems = FilterItemsByDungeonLevelCostCap(_itemRepository.LoadAll().ToList(), dungeonLevel);
        if (allItems.Count == 0)
            return result;

        var nextReceiverIndex = 0;

        foreach (var monster in session.Monsters)
        {
            if (!HasWornEquipmentTreasure(monster.Template.TreasureType))
                continue;

            if (!TryResolveWornEquipmentClass(monster.Template, out var monsterClass))
                continue;

            var level = ResolveWornEquipmentLevel(monster.Template);
            var chancePercent = Math.Clamp(level * 5, 0, 100);
            if (chancePercent <= 0)
                continue;

            var hasMiscWeapon = false;
            var miscWeaponIsEdged = false;

            RollWornEquipmentCategory("Armor", chancePercent, IsWornEquipmentArmor, false);
            RollWornEquipmentCategory("Shield", chancePercent, i => i.Type == ItemType.Shield, false);
            RollWornEquipmentCategory("Sword", chancePercent, IsWornEquipmentSword, false);
            RollWornEquipmentCategory("Miscellaneous Weapon", chancePercent, IsWornEquipmentMiscWeapon, true);
            RollWornEquipmentCategory("Potion", chancePercent, i => i.Type == ItemType.Potion, false);
            RollWornEquipmentCategory("Scroll", chancePercent, i => i.Type == ItemType.Scroll, false);
            RollWornEquipmentCategory("Ring", chancePercent, IsRingItem, false);
            RollWornEquipmentCategory("Miscellaneous Magic", chancePercent, IsMiscMagicItem, false);

            if (monsterClass == CharacterClass.Cleric && (!hasMiscWeapon || miscWeaponIsEdged))
            {
                RollWornEquipmentCategory("Wand/Staff/Rod", chancePercent, IsRodStaffWandItem, false, forceCategoryForCleric: true);
            }
            else if (monsterClass == CharacterClass.MagicUser)
            {
                RollWornEquipmentCategory("Wand/Staff/Rod", chancePercent, IsRodStaffWandItem, false);
            }

            void RollWornEquipmentCategory(string categoryName, int categoryChancePercent, Func<Item, bool> categoryFilter, bool isMiscWeaponCategory, bool forceCategoryForCleric = false)
            {
                if (!forceCategoryForCleric && !IsCategoryAllowed(monsterClass, categoryName))
                    return;

                var chanceRoll = _dice.Roll(100);
                if (chanceRoll > categoryChancePercent)
                    return;

                if (!TrySelectWornEquipmentItem(allItems, categoryFilter, monsterClass, out var selectedItem, out var rerollUsed))
                    return;

                if (isMiscWeaponCategory)
                {
                    hasMiscWeapon = selectedItem != null;
                    miscWeaponIsEdged = selectedItem != null && IsEdgedWeapon(selectedItem);
                }

                if (selectedItem == null)
                    return;

                if (!TryAssignItemToSurvivors(survivors, selectedItem, result, ref nextReceiverIndex))
                {
                    result.UnassignedItems.Add($"{selectedItem.Name} (from WornEquipment: {monster.Template.Name}, {categoryName})");
                }

                RuleApplicationInfo.Publish($"WornEquipment: {monster.DisplayName} {categoryName} roll {chanceRoll}/100 <= {categoryChancePercent}%: yes. Item: {selectedItem.Name}{(rerollUsed ? " (rerolled once)" : string.Empty)}.");
            }
        }

        return result;
    }

    private MagicAwardResult AwardRandomItemsAfterCombat(List<Character> survivors, int? dungeonLevel)
    {
        var result = new MagicAwardResult();

        if (survivors.Count == 0)
            return result;

        var gameRules = GameRulesProvider.Current;
        var numberOfItems = _dice.GetNumberOfSuccesses(
            gameRules.NumberOfItemsThatCouldBeFound, 
            gameRules.ProbabilityFindingEachItem);

        if (numberOfItems <= 0)
            return result;

        var allItems = FilterItemsByDungeonLevelCostCap(_itemRepository.LoadAll().Where(i => i.IsShopBuyable).ToList(), dungeonLevel);
        if (allItems.Count == 0)
            return result;

        var nextReceiverIndex = 0;

        for (int i = 0; i < numberOfItems; i++)
        {
            var randomItem = SelectItemByRarityWeight(allItems);
            var item = CloneItem(randomItem);

            var assigned = false;
            for (int attempt = 0; attempt < survivors.Count; attempt++)
            {
                var idx = (nextReceiverIndex + attempt) % survivors.Count;
                var receiver = survivors[idx];
                if (!receiver.CanCarry(item))
                    continue;

                receiver.Inventory.Add(item);
                result.AssignedItems.Add(new AssignedMagicItem
                {
                    ReceiverName = receiver.Name,
                    ItemName = item.Name
                });

                nextReceiverIndex = (idx + 1) % survivors.Count;
                assigned = true;
                break;
            }

            if (!assigned)
                result.UnassignedItems.Add(item.Name + " (no one can carry)");
        }

        return result;
    }

    private Item SelectItemByRarityWeight(List<Item> items)
    {
        var rarityWeights = new Dictionary<RarityType, int>
        {
            { RarityType.Common, 58 },
            { RarityType.Uncommon, 25 },
            { RarityType.Rare, 10 },
            { RarityType.VeryRare, 4 },
            { RarityType.Legendary, 2 },
            { RarityType.Unique, 1 }
        };

        // Calculate total weight
        var totalWeight = 0;
        var itemWeights = new List<(Item item, int weight)>();

        foreach (var item in items)
        {
            var weight = rarityWeights.ContainsKey(item.Rarity) ? rarityWeights[item.Rarity] : rarityWeights[RarityType.Common];
            itemWeights.Add((item, weight));
            totalWeight += weight;
        }

        // Select random item based on weight
        var randomValue = _random.Next(totalWeight);
        var cumulativeWeight = 0;

        foreach (var (item, weight) in itemWeights)
        {
            cumulativeWeight += weight;
            if (randomValue < cumulativeWeight)
                return item;
        }

        // Fallback (should never reach here)
        return items[_random.Next(items.Count)];
    }

    private static List<Item> FilterItemsByDungeonLevelCostCap(List<Item> items, int? dungeonLevel)
    {
        var maxCost = GetMaxFoundItemCostForDungeonLevel(dungeonLevel);
        if (!maxCost.HasValue)
            return items;

        return items.Where(i => i.Cost <= maxCost.Value).ToList();
    }

    private static int? GetMaxFoundItemCostForDungeonLevel(int? dungeonLevel)
    {
        if (!dungeonLevel.HasValue)
            return null;

        return dungeonLevel.Value switch
        {
            <= 1 => 2000,
            2 => 3000,
            3 => 5000,
            4 => 10000,
            5 => 11000,
            6 => 14000,
            7 => 20000,
            8 => 40000,
            9 => 100000,
            _ => null
        };
    }

    private static List<Item> GetItemPoolForMagicTable(List<Item> allItems, string table, HashSet<string> miscMagicNames)
    {
        if (string.IsNullOrWhiteSpace(table))
            return new List<Item>();

        var key = table.Trim().ToLowerInvariant();
        return key switch
        {
            "potion" => allItems.Where(i => i.Type == ItemType.Potion).ToList(),
            "scroll" => allItems.Where(i => i.Type == ItemType.Scroll).ToList(),
            "ring" => allItems.Where(IsRingItem).ToList(),
            "rods, staves & wands" => allItems.Where(IsRodStaffWandItem).ToList(),
            "rods staves wands" => allItems.Where(IsRodStaffWandItem).ToList(),
            "rods/staves/wands" => allItems.Where(IsRodStaffWandItem).ToList(),
            "miscmagic" => allItems.Where(i => miscMagicNames.Contains(i.Name)).ToList(),
            "misc magic" => allItems.Where(i => miscMagicNames.Contains(i.Name)).ToList(),
            "weapon" => allItems.Where(i => i.Type == ItemType.Weapon).ToList(),
            "armor" => allItems.Where(i => i.Type == ItemType.Armor || i.Type == ItemType.Shield).ToList(),
            "magicitem" => allItems.Where(i => i.Type == ItemType.MagicItem).ToList(),
            _ => allItems.Where(i => i.Type == ItemType.MagicItem && i.Name.Contains(table, StringComparison.OrdinalIgnoreCase)).ToList()
        };
    }

    private static HashSet<string> LoadItemNamesFromFile(string fileName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine("Data", "Items", fileName);
        if (!File.Exists(path))
            return names;

        var json = File.ReadAllText(path);
        var grouped = JsonSerializer.Deserialize<ItemCategoryJsonModel>(json);
        if (grouped?.Items != null)
        {
            foreach (var item in grouped.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                    names.Add(item.Name);
            }
        }

        return names;
    }

    private string ResolveAnyMagicTable(string table, out string? anyRollInfo)
    {
        anyRollInfo = null;
        if (!string.Equals(table?.Trim(), "Any", StringComparison.OrdinalIgnoreCase))
            return table;

        var roll = _random.Next(1, 101);
        var resolved = roll switch
        {
            <= 20 => "Potion",
            <= 35 => "Scroll",
            <= 40 => "Ring",
            <= 45 => "Rods, Staves & Wands",
            <= 60 => "Misc Magic",
            <= 75 => "Armor",
            _ => "Weapon"
        };

        anyRollInfo = $"Magic table Any: rolled {roll} on 1d100 => {resolved}.";
        return resolved;
    }

    private static bool IsRingItem(Item item)
    {
        if (item == null)
            return false;

        if (item.Slot == EquipmentSlot.Ring1 || item.Slot == EquipmentSlot.Ring2)
            return true;

        return item.Name.StartsWith("Ring", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWornEquipmentArmor(Item item)
    {
        return item.Type == ItemType.Armor;
    }

    private static bool IsWornEquipmentSword(Item item)
    {
        return item.Type == ItemType.Weapon
               && item.Name.Contains("Sword", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWornEquipmentMiscWeapon(Item item)
    {
        return item.Type == ItemType.Weapon
               && !item.Name.Contains("Sword", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRodStaffWandItem(Item item)
    {
        if (item == null)
            return false;

        var name = item.Name ?? string.Empty;
        return name.StartsWith("Rod", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Staff", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Wand", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMiscMagicItem(Item item)
    {
        if (item == null || item.Type != ItemType.MagicItem)
            return false;

        return !IsRingItem(item) && !IsRodStaffWandItem(item);
    }

    private static bool HasWornEquipmentTreasure(string? treasureType)
    {
        if (string.IsNullOrWhiteSpace(treasureType))
            return false;

        return treasureType
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Select(t =>
            {
                var idx = t.IndexOf('(');
                return idx > 0 ? t[..idx].Trim() : t;
            })
            .Any(t => string.Equals(t, "WornEquipment", StringComparison.OrdinalIgnoreCase));
    }

    private static int ResolveWornEquipmentLevel(Monster monster)
    {
        if (monster.HitDice > 0)
            return monster.HitDice;

        var name = monster.Name ?? string.Empty;
        var digits = new string(name.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var parsed) && parsed > 0)
            return parsed;

        return 1;
    }

    private static bool TryResolveWornEquipmentClass(Monster monster, out CharacterClass characterClass)
    {
        var text = string.Join(" ", new[]
        {
            monster.Name,
            string.Join(" ", monster.SpecialAbilities.Select(a => a.Name))
        }).ToLowerInvariant();

        if (text.Contains("bard") || text.Contains("assassin") || text.Contains("monk") || text.Contains("thief"))
        {
            characterClass = CharacterClass.Thief;
            return true;
        }

        if (text.Contains("druid") || text.Contains("cleric") || text.Contains("priest"))
        {
            characterClass = CharacterClass.Cleric;
            return true;
        }

        if (text.Contains("illusionist") || text.Contains("magic-user") || text.Contains("magic user") || text.Contains("mage"))
        {
            characterClass = CharacterClass.MagicUser;
            return true;
        }

        if (text.Contains("ranger") || text.Contains("paladin") || text.Contains("fighter"))
        {
            characterClass = CharacterClass.Fighter;
            return true;
        }

        // Default fallback for WornEquipment when no class marker is found.
        characterClass = CharacterClass.Fighter;
        return true;

    }

    private static bool IsCategoryAllowed(CharacterClass cls, string categoryName)
    {
        var key = categoryName.Trim().ToLowerInvariant();
        return cls switch
        {
            CharacterClass.Fighter => key is "armor" or "shield" or "sword" or "miscellaneous weapon" or "potion",
            CharacterClass.MagicUser => key is "scroll" or "ring" or "wand/staff/rod" or "miscellaneous magic",
            CharacterClass.Cleric => key is "armor" or "shield" or "miscellaneous weapon" or "potion" or "scroll" or "miscellaneous magic",
            CharacterClass.Thief => key is "shield" or "sword" or "miscellaneous weapon" or "potion" or "ring" or "miscellaneous magic",
            _ => false
        };
    }

    private static bool TrySelectWornEquipmentItem(List<Item> allItems, Func<Item, bool> categoryFilter, CharacterClass ownerClass, out Item? selected, out bool rerollUsed)
    {
        selected = null;
        rerollUsed = false;

        var categoryPool = allItems
            .Where(categoryFilter)
            .Where(IsMagicalForWornEquipment)
            .ToList();

        if (categoryPool.Count == 0)
            return false;

        selected = PickRandom(categoryPool);
        if (selected == null)
            return false;

        if (!IsWornEquipmentItemUsableBy(ownerClass, selected))
        {
            rerollUsed = true;
            var firstPick = selected;
            var rerollPool = categoryPool
                .Where(i => !ReferenceEquals(i, firstPick))
                .ToList();

            selected = PickRandom(rerollPool);
            if (selected == null || !IsWornEquipmentItemUsableBy(ownerClass, selected))
            {
                selected = null;
                return false;
            }
        }

        selected = CloneItem(selected);
        return true;
    }

    private static bool IsMagicalForWornEquipment(Item item)
    {
        return item.Type is ItemType.Potion or ItemType.Scroll or ItemType.MagicItem
               || IsRingItem(item)
               || IsRodStaffWandItem(item)
               || item.MagicBonus != 0
               || item.ToHitBonus != 0
               || item.ArmorClassBonus != 0
               || item.SpecialAbilities.Count > 0
               || item.IsCursed;
    }

    private static bool IsWornEquipmentItemUsableBy(CharacterClass ownerClass, Item item)
    {
        if (item.IsCursed)
            return false;

        if (item.AllowedClasses == null || item.AllowedClasses.Count == 0)
            return true;

        return item.AllowedClasses.Contains(ownerClass);
    }

    private static bool IsEdgedWeapon(Item item)
    {
        var damageType = item.DamageType?.Trim();
        if (string.IsNullOrWhiteSpace(damageType))
            return false;

        return damageType.Equals("Slashing", StringComparison.OrdinalIgnoreCase)
               || damageType.Equals("Piercing", StringComparison.OrdinalIgnoreCase);
    }

    private static Item? PickRandom(List<Item> pool)
    {
        if (pool.Count == 0)
            return null;

        return pool[Random.Shared.Next(pool.Count)];
    }

    private static bool TryAssignItemToSurvivors(List<Character> survivors, Item item, MagicAwardResult result, ref int nextReceiverIndex)
    {
        for (int attempt = 0; attempt < survivors.Count; attempt++)
        {
            var idx = (nextReceiverIndex + attempt) % survivors.Count;
            var receiver = survivors[idx];
            if (!receiver.CanCarry(item))
                continue;

            receiver.Inventory.Add(item);
            result.AssignedItems.Add(new AssignedMagicItem
            {
                ReceiverName = receiver.Name,
                ItemName = item.Name
            });

            nextReceiverIndex = (idx + 1) % survivors.Count;
            return true;
        }

        return false;
    }

    private static Item CloneItem(Item source)
    {
        return new Item
        {
            Name = source.Name,
            Type = source.Type,
            Slot = source.Slot,
            Cost = source.Cost,
            Weight = source.Weight,
            ToHitBonus = source.ToHitBonus,
            Status = source.Status,
            IsShopBuyable = source.IsShopBuyable,
            StockQuantity = source.StockQuantity,
            ArmorClassBonus = source.ArmorClassBonus,
            Damage = source.Damage,
            DamageVsLarge = source.DamageVsLarge,
            AllowedClasses = new List<CharacterClass>(source.AllowedClasses),
            SpecialAbilities = new List<string>(source.SpecialAbilities)
        };
    }

    private sealed class MagicAwardResult
    {
        public List<AssignedMagicItem> AssignedItems { get; } = new();
        public List<string> UnassignedItems { get; } = new();
    }

    private sealed class AssignedMagicItem
    {
        public string ReceiverName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
    }

    private void MoveDeadPartyMembersToEnd(List<Character> combatParty)
    {
        // Reorder in-memory combat turn order immediately.
        var awakeAlive = combatParty
            .Where(c => c.CurrentHitPoints > 0
                        && !c.HasStatus(CharacterStatus.Dead)
                        && !c.HasStatus(CharacterStatus.Asleep))
            .ToList();
        var asleepAlive = combatParty
            .Where(c => c.CurrentHitPoints > 0
                        && !c.HasStatus(CharacterStatus.Dead)
                        && c.HasStatus(CharacterStatus.Asleep))
            .ToList();
        var dead = combatParty
            .Where(c => c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead))
            .ToList();

        combatParty.Clear();
        combatParty.AddRange(awakeAlive);
        combatParty.AddRange(asleepAlive);
        combatParty.AddRange(dead);

        var partyData = _partyRepository.Load();
        if (partyData.Members.Count == 0)
            return;

        var stateLookup = combatParty
            .ToDictionary(
                c => c.Name,
                c => c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead)
                    ? 2
                    : c.HasStatus(CharacterStatus.Asleep) ? 1 : 0,
                StringComparer.OrdinalIgnoreCase);

        var awakeAliveNames = new List<string>();
        var asleepAliveNames = new List<string>();
        var unknownNames = new List<string>();
        var deadNames = new List<string>();

        foreach (var memberName in partyData.Members)
        {
            if (!stateLookup.TryGetValue(memberName, out var state))
            {
                unknownNames.Add(memberName);
                continue;
            }

            if (state == 2)
            {
                deadNames.Add(memberName);
            }
            else if (state == 1)
            {
                asleepAliveNames.Add(memberName);
            }
            else
            {
                awakeAliveNames.Add(memberName);
            }
        }

        var reordered = awakeAliveNames
            .Concat(asleepAliveNames)
            .Concat(unknownNames)
            .Concat(deadNames)
            .ToList();

        if (!partyData.Members.SequenceEqual(reordered, StringComparer.OrdinalIgnoreCase))
        {
            partyData.Members = reordered;
            _partyRepository.Save(partyData);
        }
    }

    private void ShowRoundEvents(IWin32Window owner, IEnumerable<Adnd.Core.Combat.Events.CombatEvent> events,
                                 CombatSession session)
    {
        var sb = new StringBuilder();
        foreach (var e in events)
            sb.AppendLine(e.Message);

        Say(owner, "Combat Round", sb.ToString(), session);
    }

    private void HandleRotGrubFlamePrompts(IWin32Window owner, CombatSession session, List<CombatEvent> roundEvents, CharacterRepository characterRepository)
    {
        var prompts = roundEvents
            .Select(e => e.Message)
            .Where(m => m.StartsWith("ROT_GRUB_PROMPT::", StringComparison.Ordinal))
            .Select(m => m.Substring("ROT_GRUB_PROMPT::".Length).Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prompts.Count == 0)
            return;

        roundEvents.RemoveAll(e => e.Message.StartsWith("ROT_GRUB_PROMPT::", StringComparison.Ordinal));

        foreach (var targetName in prompts)
        {
            var target = session.Party.FirstOrDefault(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.RotGrubFlamePromptPending)
                continue;

            var result = AskYesNoOnBoth(owner, session,
                "Rot Grub",
                $"{target.Name} is infested by rot grubs. Apply flame to wound?");

            if (result == DialogResult.Yes)
            {
                var damage = _dice.Roll(6);
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                target.RotGrubFlamePromptPending = false;
                target.RotGrubDeathRoundsRemaining = 0;

                var actual = before - target.CurrentHitPoints;
                roundEvents.Add(new CombatEvent($"Flame is applied to {target.Name}'s wound for {actual} damage (1d6)."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.CurrentHitPoints = 0;
                    target.AddStatus(CharacterStatus.Dead);
                    roundEvents.Add(new CombatEvent($"{target.Name} dies from the flame treatment."));
                }
            }
            else
            {
                var rounds = _random.Next(10, 31);
                target.ApplyRotGrubInfestation(rounds);
                roundEvents.Add(new CombatEvent($"{target.Name} refuses flame and is diseased by rot grubs. Death in {rounds} rounds unless cured."));
            }

            characterRepository.Save(target);
        }
    }

    private DialogResult AskYesNoOnBoth(IWin32Window owner, CombatSession session, string title, string question)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(620, 170),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 18,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = title.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 16,
            Top = 66,
            Width = framePanel.ClientSize.Width - 32,
            Height = 54,
            Text = question,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 12f, FontStyle.Bold)
        };

        var hintLabel = new Label
        {
            Left = 0,
            Top = 124,
            Width = framePanel.ClientSize.Width,
            Height = 26,
            Text = "(Y/N)",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 12f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Y)
            {
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.N || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(questionLabel);
        framePanel.Controls.Add(hintLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", question, null, new[]
        {
            new ViewerPromptOption("yes", "Yes"),
            new ViewerPromptOption("no", "No"),
        });

        var answers = new Dictionary<string, DialogResult>
        {
            ["yes"] = DialogResult.Yes,
            ["no"] = DialogResult.No,
        };

        var result = ViewerDialog.RunModal(form, owner, prompt, answers, p => ViewerPromptChanged?.Invoke(session, p));
        ViewerPromptChanged?.Invoke(session, null);
        return result;
    }

    private void ApplyShriekReinforcements(CombatSession session, List<CombatEvent> roundEvents, int? dungeonLevel)
    {
        var hasShriek = session.AliveMonsters.Any(m =>
            string.Equals(m.Template.Name, "Shrieker", StringComparison.OrdinalIgnoreCase)
            || m.Template.SpecialAbilities.Any(a => string.Equals(a.Name?.Trim(), "Shriek", StringComparison.OrdinalIgnoreCase)));
        if (!hasShriek)
            return;

        if (session.GetDistinctGroupIds().Count() >= 4)
            return;

        var roll = _dice.Roll(100);
        if (roll <= 50)
        {
            var depth = Math.Max(1, dungeonLevel ?? 1);
            var existingGroups = session.GetDistinctGroupIds().ToList();
            var nextGroupNumber = 1;
            while (existingGroups.Contains($"Group{nextGroupNumber}", StringComparer.OrdinalIgnoreCase))
                nextGroupNumber++;

            var groupId = $"Group{nextGroupNumber}";
            if (TryRollReinforcementEncounter(depth, out var selectedName, out var selectedCount))
            {
                var reinforcements = _monsterFactory.CreateGroup(selectedName, selectedCount, groupId);
                session.Monsters.AddRange(reinforcements);
                roundEvents.Add(new CombatEvent($"Shriek attracts reinforcements: {selectedCount} {selectedName}{(selectedCount > 1 ? "s" : string.Empty)} join the fight ({groupId})."));
            }
        }
        else
        {
            roundEvents.Add(new CombatEvent($"Shriek fails to attract reinforcements this round ({roll} on 1d100)."));
        }
    }

    private void ShowFinalOutcome(IWin32Window owner, CombatOutcome outcome, CombatSession session)
    {
        var text = outcome switch
        {
            CombatOutcome.Victory => "Victory!",
            CombatOutcome.Defeat => "Defeat...",
            CombatOutcome.Escaped => "The party escaped.",
            _ => "Combat ended."
        };

    //    Say(owner, "Combat Result", text, session);
        if (outcome != CombatOutcome.Victory)
            Say(owner, "Combat Result", text, session);
    }

    /// <summary>
    /// Says something and waits, on both surfaces at once: a dialog in the game's window and a Continue
    /// button on the table, either of which dismisses it.
    ///
    /// Published through the same <see cref="ViewerPromptChanged"/> event as a fight's own choices, so this
    /// still needs no reference to a bridge or a snapshot -- whoever is publishing decides how a prompt
    /// reaches the table, exactly as before. Cleared afterwards, or the table would go on offering Continue
    /// for a message that has already been answered.
    /// </summary>
    private void Say(IWin32Window owner, string title, string text, CombatSession session)
    {
        ViewerPromptChanged?.Invoke(session, ViewerMessage.Prompt(Summarise(text)));
        ViewerMessage.Show(owner, title, text);
        ViewerPromptChanged?.Invoke(session, null);
    }

    /// <summary>
    /// The one line the table can carry. The blow-by-blow reaches the viewer as beats, which it narrates
    /// and animates as they play; the dialog's own header has room for a line, not for a round.
    /// </summary>
    private static string Summarise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Continue.";

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "Continue.";

        var last = lines[lines.Length - 1].Trim();
        return lines.Length == 1 ? last : $"{last}  (+{lines.Length - 1} more)";
    }

    private static TreasureResult BuildEffectiveTreasure(TreasureResult source, bool includeLairTreasure)
    {
        if (includeLairTreasure)
            return source;

        var result = new TreasureResult();

        result.NonLair.CopperPieces = source.NonLair.CopperPieces;
        result.NonLair.SilverPieces = source.NonLair.SilverPieces;
        result.NonLair.ElectrumPieces = source.NonLair.ElectrumPieces;
        result.NonLair.GoldPieces = source.NonLair.GoldPieces;
        result.NonLair.PlatinumPieces = source.NonLair.PlatinumPieces;
        result.NonLair.Gems = source.NonLair.Gems.Select(CloneValuable).ToList();
        result.NonLair.Jewelry = source.NonLair.Jewelry.Select(CloneValuable).ToList();
        result.NonLair.Art = source.NonLair.Art.Select(CloneValuable).ToList();
        result.NonLair.MagicPlaceholders = source.NonLair.MagicPlaceholders.Select(CloneMagicPlaceholder).ToList();

        result.Total.CopperPieces = result.NonLair.CopperPieces;
        result.Total.SilverPieces = result.NonLair.SilverPieces;
        result.Total.ElectrumPieces = result.NonLair.ElectrumPieces;
        result.Total.GoldPieces = result.NonLair.GoldPieces;
        result.Total.PlatinumPieces = result.NonLair.PlatinumPieces;
        result.Total.Gems = result.NonLair.Gems.Select(CloneValuable).ToList();
        result.Total.Jewelry = result.NonLair.Jewelry.Select(CloneValuable).ToList();
        result.Total.Art = result.NonLair.Art.Select(CloneValuable).ToList();
        result.Total.MagicPlaceholders = result.NonLair.MagicPlaceholders.Select(CloneMagicPlaceholder).ToList();

        result.SyncLegacyTotalsFromBuckets();
        return result;
    }

    private static TreasureValuableResult CloneValuable(TreasureValuableResult value)
        => new() { Category = value.Category, ValueGp = value.ValueGp, SourceTable = value.SourceTable };

    private static TreasureMagicPlaceholderResult CloneMagicPlaceholder(TreasureMagicPlaceholderResult value)
        => new() { Table = value.Table, Count = value.Count, SourceTable = value.SourceTable };

    private void TriggerAlarmEncounter(IWin32Window owner, CombatSession session, CharacterRepository characterRepository, int? dungeonLevel)
    {
        var aliveParty = session.Party.Where(c => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead)).ToList();
        if (aliveParty.Count == 0)
            return;

        var level = Math.Max(1, dungeonLevel ?? 1);
        if (!TryRollReinforcementEncounter(level, out var selectedName, out var count))
            return;

        Say(owner, "Treasure Chest", $"Alarm summons {count} {selectedName}{(count > 1 ? "s" : string.Empty)}!", session);
        StartEncounter(owner, selectedName, count, session.Party, characterRepository, dungeonLevel);
    }

    private bool TryRollReinforcementEncounter(int dungeonLevel, out string monsterName, out int count)
    {
        monsterName = string.Empty;
        count = 1;

        var monsterLevel = RollMonsterLevelFromEncounterTable(dungeonLevel);
        var rolled = RollFromMonsterLevels(monsterLevel, dungeonLevel);
        if (rolled == null)
            return false;

        monsterName = rolled.Value.MonsterName;
        count = rolled.Value.Count;
        return true;
    }

    private int RollMonsterLevelFromEncounterTable(int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterEncounter.json");
        if (!File.Exists(tablePath))
            return Math.Max(1, dungeonLevel);

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("monsterEncounterTable", out var tableRoot)
            || tableRoot.ValueKind != JsonValueKind.Object)
            return Math.Max(1, dungeonLevel);

        var levelKey = dungeonLevel.ToString();
        if (!tableRoot.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return Math.Max(1, dungeonLevel);

        var roll = _random.Next(1, 21);
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("roll", out var rollEl)
                || !entry.TryGetProperty("monsterLevel", out var levelEl))
                continue;

            var range = rollEl.GetString();
            if (!TryParseRollRange(range, out var min, out var max))
                continue;

            if (roll < min || roll > max)
                continue;

            var monsterLevel = levelEl.GetInt32();
            RuleApplicationInfo.Publish(
                "DMG",
                "174",
                $"Roll monster level for reinforcement at dungeon level {dungeonLevel}",
                "Use MonsterEncounterTable: roll 1d20 and map to encounter monster level.",
                "1",
                "20",
                roll.ToString(),
                $"Monster level {monsterLevel}.");
            return monsterLevel;
        }

        return Math.Max(1, dungeonLevel);
    }

    private (string MonsterName, int Count)? RollFromMonsterLevels(int monsterLevel, int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterLevels.json");
        if (!File.Exists(tablePath))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("MonsterLevels", out var allLevels))
            return null;

        var levelKey = $"Level{monsterLevel}";
        if (!allLevels.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var roll = _random.Next(1, 101);
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("DiceMin", out var minEl)
                    || !entry.TryGetProperty("DiceMax", out var maxEl)
                    || !entry.TryGetProperty("Creature", out var creatureEl))
                    continue;

                var min = minEl.GetInt32();
                var max = maxEl.GetInt32();
                if (roll < min || roll > max)
                    continue;

                var creature = creatureEl.GetString();
                var resolved = ResolveEncounterCreatureToMonsterName(creature, monsterLevel);
                string page = MazeForm.GetDMGpageForMonsterEncounterTable(monsterLevel);

                RuleApplicationInfo.Publish(
                    "DMG",
                    page,
                   // "175-177",//TODO update to exact page reference
                    $"Roll reinforcement creature for dungeon level {dungeonLevel} (monster level {monsterLevel})",
                    $"Use encounter table Level{monsterLevel}; roll 1d100 and find matching DiceMin-DiceMax range.",
                    "1",
                    "100",
                    roll.ToString(),
                    string.IsNullOrWhiteSpace(resolved)
                        ? $"Matched '{creature}', but no monster mapping was found. Rerolling on Level{monsterLevel}."
                        : $"Matched '{creature}', mapped to '{resolved}'.");

                if (string.IsNullOrWhiteSpace(resolved))
                    break;

                var count = RollEncounterCount(entry, resolved);
                return (resolved, count);
            }
        }

        return null;
    }

    private int RollEncounterCount(JsonElement entry, string resolvedMonster)
    {
        if (entry.TryGetProperty("CountMin", out var countMinEl)
            && entry.TryGetProperty("CountMax", out var countMaxEl)
            && countMinEl.ValueKind == JsonValueKind.Number
            && countMaxEl.ValueKind == JsonValueKind.Number)
        {
            var countMin = countMinEl.GetInt32();
            var countMax = countMaxEl.GetInt32();
            if (countMax < countMin)
                (countMin, countMax) = (countMax, countMin);

            countMin = Math.Max(1, countMin);
            countMax = Math.Max(1, countMax);
            var rolledCount = _random.Next(countMin, countMax + 1);
            string page = MazeForm.GetDMGpageForMonsterEncounterTable(rolledCount);

            RuleApplicationInfo.Publish(
                "DMG",
                page,//TODO update to exact page reference
                $"Roll reinforcement count for '{resolvedMonster}'",
                "Use CountMin-CountMax from MonsterLevels entry.",
                "1",
                (countMax - countMin + 1).ToString(),
                (rolledCount - countMin + 1).ToString(),
                $"Count {rolledCount} (range {countMin}-{countMax}).");

            return rolledCount;
        }

        var template = FindMonsterTemplateByName(resolvedMonster);
        if (template == null)
            return 1;

        var min = Math.Max(1, template.NumberOfAppearancesMin);
        var max = Math.Max(min, template.NumberOfAppearancesMax);
        return _random.Next(min, max + 1);
    }

    private string? ResolveEncounterCreatureToMonsterName(string? creature, int monsterLevel)
    {
        if (string.IsNullOrWhiteSpace(creature))
            return null;

        var raw = creature.Trim();
        if (string.Equals(raw, "Human", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "Humans", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Character", StringComparison.OrdinalIgnoreCase))
        {
            var roll = _random.Next(1, 101);
            return roll switch
            {
                <= 25 => "Bandit",
                <= 30 => "Berserker",
                <= 45 => "Brigand",
                _ => "Adventurer"
            };
        }

        var candidates = new List<string> { raw };
        var comma = raw.IndexOf(',');
        if (comma > 0 && comma < raw.Length - 1)
        {
            var left = raw[..comma].Trim();
            var right = raw[(comma + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                candidates.Add($"{right} {left}");
        }

        foreach (var candidate in candidates)
        {
            if (FindMonsterTemplateByName(candidate) != null)
                return candidate;
        }

        return null;
    }

    private Monster? FindMonsterTemplateByName(string name)
    {
        return _monsterRepository.GetAll()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseRollRange(string? range, out int min, out int max)
    {
        min = 0;
        max = 0;
        if (string.IsNullOrWhiteSpace(range))
            return false;

        var trimmed = range.Trim();
        var dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash >= trimmed.Length - 1)
        {
            if (!int.TryParse(trimmed, out var single))
                return false;

            min = single;
            max = single;
            return true;
        }

        var left = trimmed[..dash].Trim();
        var right = trimmed[(dash + 1)..].Trim();
        if (!int.TryParse(left, out min) || !int.TryParse(right, out max))
            return false;

        if (max < min)
            (min, max) = (max, min);

        return true;
    }

    private sealed class LairChestResolutionResult
    {
        public bool IncludeLairTreasure { get; set; }
        public bool TriggeredAlarmEncounter { get; set; }
        public ChestTrapType TrapType { get; set; }
        public bool TrapFound { get; set; }
        public bool TrapDisarmed { get; set; }
    }

    private LairChestResolutionResult ResolveLairChestInteraction(IWin32Window owner, CombatSession session, List<Character> survivors, int dungeonLevel, ChestTrapType trapType)
    {
        var result = new LairChestResolutionResult
        {
            IncludeLairTreasure = true,
            TrapType = trapType
        };

        using var dialog = new LairTreasureChestDialog(p => ViewerPromptChanged?.Invoke(session, p));
        dialog.ShowDialog(owner);

        switch (dialog.Choice)
        {
            case LairChestChoice.LeaveAlone:
                result.IncludeLairTreasure = false;
                RuleApplicationInfo.Publish("Treasure chest left alone. In-lair treasure not collected.");
                return result;

            case LairChestChoice.CastFindTraps:
                HandleCastFindTraps(owner, session, survivors, result);
                break;

            case LairChestChoice.Inspect:
                HandleInspectTrap(owner, session, survivors, result);
                break;
        }

        if (dialog.Choice == LairChestChoice.Open || dialog.Choice == LairChestChoice.CastFindTraps || dialog.Choice == LairChestChoice.Inspect)
        {
            var opener = PromptSelectPartyMember(owner, session, survivors, "Open chest", "Choose who opens the chest:");
            if (opener == null)
                return result;

            var shouldTrigger = !result.TrapDisarmed && trapType != ChestTrapType.None;
            if (!shouldTrigger)
                return result;

            var triggerRoll = _dice.Roll(100);
            var triggered = triggerRoll <= 50;
            RuleApplicationInfo.Publish($"Chest trap trigger roll: 1d100={triggerRoll}; trigger on 1-50 => {(triggered ? "TRIGGERED" : "safe")}");
            if (!triggered)
                return result;

            ApplyChestTrapEffect(owner, session, survivors, opener, dungeonLevel, trapType, result);
        }

        return result;
    }

    private void HandleCastFindTraps(IWin32Window owner, CombatSession session, List<Character> survivors, LairChestResolutionResult result)
    {
        var caster = PromptSelectPartyMember(owner, session, survivors, "Cast Find Traps", "Choose who casts Find Traps:", includeClassInList: true);
        if (caster == null)
            return;

        var findTrapsSpell = _spellRepository.LoadAll()
            .FirstOrDefault(s => string.Equals(s.Name, "Find Traps", StringComparison.OrdinalIgnoreCase));

        if (findTrapsSpell == null)
        {
            Say(owner, "Treasure Chest", "Find Traps spell is not available in spell data.", session);
            return;
        }

        var cast = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = findTrapsSpell.Id,
            Context = SpellUseContext.Exploration,
            Targets = new List<SpellCastTarget> { SpellCastTarget.Ally(caster) },
            PartyTargets = survivors,
            MonsterTargets = new List<MonsterInstance>()
        });

        if (!cast.Success)
        {
            Say(owner, "Treasure Chest", $"{caster.Name} fails to cast Find Traps.{Environment.NewLine}{string.Join(Environment.NewLine, cast.Events)}", session);
            return;
        }

        Say(owner, "Treasure Chest", $"{caster.Name} casts Find Traps and detects: {FormatTrapName(result.TrapType)}.", session);
        result.TrapFound = result.TrapType != ChestTrapType.None;
        RuleApplicationInfo.Publish($"Find Traps spell result: trap {(result.TrapFound ? "found" : "not found")} ({FormatTrapName(result.TrapType)}).");

        if (result.TrapType == ChestTrapType.None)
            return;

        var attempt = AskYesNoOnBoth(owner, session, "Disarm Trap", "A trap is found. Attempt to disarm it?");
        if (attempt != DialogResult.Yes)
        {
            RuleApplicationInfo.Publish("Disarm Traps result: not attempted (trap remains armed).");
            return;
        }

        TryDisarmByAnyThief(owner, session, survivors, result);
    }

    private void HandleInspectTrap(IWin32Window owner, CombatSession session, List<Character> survivors, LairChestResolutionResult result)
    {
        var inspector = PromptSelectPartyMember(owner, session, survivors, "Inspect chest", "Choose who inspects the chest:", includeClassInList: true);
        if (inspector == null)
            return;

        if (!IsThiefClass(inspector))
        {
            Say(owner, "Treasure Chest", $"{inspector.Name} is not a thief class and cannot inspect for traps effectively.", session);
            return;
        }

        var thiefLevel = Math.Max(1, inspector.GetClassLevel(CharacterClass.Thief));
        var chance = Math.Clamp((int)Math.Round(AbilitiesTables.ThiefFindRemoveTraps(thiefLevel, inspector.Race, inspector.Abilities.Dexterity), MidpointRounding.AwayFromZero), 1, 99);
        var roll = _dice.Roll(100);
        var found = roll <= chance && result.TrapType != ChestTrapType.None;
        RuleApplicationInfo.Publish($"Find Traps check ({inspector.Name}): 1d100={roll} vs {chance}% => {(found ? "found" : "not found")}");

        if (!found)
        {
            RuleApplicationInfo.Publish($"Find Traps result: trap not found by {inspector.Name}.");
            Say(owner, "Treasure Chest", $"{inspector.Name} does not find any trap.", session);
            return;
        }

        result.TrapFound = true;
        RuleApplicationInfo.Publish($"Find Traps result: trap found by {inspector.Name} ({FormatTrapName(result.TrapType)}).");
        Say(owner, "Treasure Chest", $"{inspector.Name} finds trap: {FormatTrapName(result.TrapType)}.", session);
        var attempt = AskYesNoOnBoth(owner, session, "Disarm Trap", "Attempt to disarm the trap?");
        if (attempt != DialogResult.Yes)
        {
            RuleApplicationInfo.Publish("Disarm Traps result: not attempted (trap remains armed).");
            return;
        }

        var disarmChance = Math.Clamp((int)Math.Round(AbilitiesTables.ThiefFindRemoveTraps(thiefLevel, inspector.Race, inspector.Abilities.Dexterity), MidpointRounding.AwayFromZero), 1, 99);
        var disarmRoll = _dice.Roll(100);
        var disarmed = disarmRoll <= disarmChance;
        RuleApplicationInfo.Publish($"Disarm Traps check ({inspector.Name}): 1d100={disarmRoll} vs {disarmChance}% => {(disarmed ? "disarmed" : "failed")}");
        result.TrapDisarmed = disarmed;
        if (disarmed)
            Say(owner, "Treasure Chest", $"{inspector.Name} disarms the trap.", session);
        else
            Say(owner, "Treasure Chest", $"{inspector.Name} fails to disarm the trap.", session);
    }

    private void TryDisarmByAnyThief(IWin32Window owner, CombatSession session, List<Character> survivors, LairChestResolutionResult result)
    {
        var thieves = survivors.Where(IsThiefClass).ToList();
        if (thieves.Count == 0)
        {
            Say(owner, "Treasure Chest", "No thief-class member is available to disarm the trap.", session);
            return;
        }

        var disarmer = PromptSelectPartyMember(owner, session, thieves, "Disarm Trap", "Choose who attempts to disarm:");
        if (disarmer == null)
            return;

        var disarmerThiefLevel = Math.Max(1, disarmer.GetClassLevel(CharacterClass.Thief));
        var disarmChance = Math.Clamp((int)Math.Round(AbilitiesTables.ThiefFindRemoveTraps(disarmerThiefLevel, disarmer.Race, disarmer.Abilities.Dexterity), MidpointRounding.AwayFromZero), 1, 99);
        var disarmRoll = _dice.Roll(100);
        var disarmed = disarmRoll <= disarmChance;
        RuleApplicationInfo.Publish($"Disarm Traps check ({disarmer.Name}): 1d100={disarmRoll} vs {disarmChance}% => {(disarmed ? "disarmed" : "failed")}");

        result.TrapDisarmed = disarmed;
        if (disarmed)
            Say(owner, "Treasure Chest", $"{disarmer.Name} disarms the trap.", session);
        else
            Say(owner, "Treasure Chest", $"{disarmer.Name} fails to disarm the trap.", session);
    }

    private void ApplyChestTrapEffect(IWin32Window owner, CombatSession session, List<Character> survivors, Character opener, int dungeonLevel, ChestTrapType trapType, LairChestResolutionResult result)
    {
        switch (trapType)
        {
            case ChestTrapType.PoisonNeedle:
                opener.AddStatus(CharacterStatus.Poisoned);
                ShowTrapTriggeredDialog(owner, session, "Poison Needle", $"{opener.Name} is poisoned.");
                break;

            case ChestTrapType.ExplodingBox:
                var mult = Math.Max(1, dungeonLevel);
                var dmg = _dice.Roll(6) * mult;
                foreach (var c in survivors)
                    c.CurrentHitPoints = Math.Max(0, c.CurrentHitPoints - dmg);
                ShowTrapTriggeredDialog(owner, session, "Exploding Box", $"Everyone takes {dmg} damage.");
                break;

            case ChestTrapType.GasBomb:
                foreach (var c in survivors)
                {
                    var saveTarget = _savingThrowService.GetSaveTarget(c, SaveThrowType.ParalyzationPoisonDeath);
                    var saveRoll = _dice.Roll(20);
                    if (saveRoll < saveTarget)
                        c.AddStatus(CharacterStatus.Poisoned);
                }
                ShowTrapTriggeredDialog(owner, session, "Gas Bomb", "Failed poison saves are poisoned.");
                break;

            case ChestTrapType.CrossbowBolt:
                var boltDamage = _dice.Roll(6);
                opener.CurrentHitPoints = Math.Max(0, opener.CurrentHitPoints - boltDamage);
                ShowTrapTriggeredDialog(owner, session, "Crossbow Bolt", $"{opener.Name} takes {boltDamage} damage.");
                break;

            case ChestTrapType.Alarm:
                result.TriggeredAlarmEncounter = true;
                ShowTrapTriggeredDialog(owner, session, "Alarm", "Another monster group approaches!");
                break;

            case ChestTrapType.MageBlaster:
                foreach (var c in survivors.Where(IsMageOrIllusionist))
                {
                    var d = _dice.Roll(6);
                    c.CurrentHitPoints = Math.Max(0, c.CurrentHitPoints - d);
                    c.ApplyParalysis(999999);
                }
                ShowTrapTriggeredDialog(owner, session, "Mage Blaster", "Magic-users and illusionists are blasted and paralyzed.");
                break;

            case ChestTrapType.PriestBlaster:
                foreach (var c in survivors.Where(IsClericOrDruid))
                {
                    var d = _dice.Roll(6);
                    c.CurrentHitPoints = Math.Max(0, c.CurrentHitPoints - d);
                    c.ApplyParalysis(999999);
                }
                ShowTrapTriggeredDialog(owner, session, "Priest Blaster", "Clerics and druids are blasted and paralyzed.");
                break;

            default:
                Say(owner, "Treasure Chest", "No trap triggers.", session);
                break;
        }
    }

    private void ShowTrapTriggeredDialog(IWin32Window owner, CombatSession session, string trapName, string outcome)
    {
        using var form = new Form
        {
            Text = "Trap Triggered",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(760, 180),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = "TRAP TRIGGERED",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var detailsLabel = new Label
        {
            Left = 0,
            Top = 58,
            Width = framePanel.ClientSize.Width,
            Height = 74,
            Text = $"{trapName.ToUpperInvariant()}\n{outcome}",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(detailsLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt(
            "choice",
            $"Trap triggered: {trapName}. {outcome}",
            null,
            new[] { new ViewerPromptOption("continue", "Continue") });

        var answers = new Dictionary<string, DialogResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["continue"] = DialogResult.OK
        };

        ViewerDialog.RunModal(form, owner, prompt, answers, p => ViewerPromptChanged?.Invoke(session, p));
        ViewerPromptChanged?.Invoke(session, null);
    }

    private Character? PromptSelectPartyMember(IWin32Window owner, CombatSession session, List<Character> candidates, string title, string promptText, bool includeClassInList = false)
    {
        var selectable = candidates
            .Where(c => c.CurrentHitPoints > 0)
            .Where(c => !c.HasStatus(CharacterStatus.Dead))
            .Where(c => !c.HasStatus(CharacterStatus.Paralyzed))
            .ToList();

        if (selectable.Count == 0)
            return null;

        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(820, 360),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 12,
            Width = framePanel.ClientSize.Width,
            Height = 28,
            Text = title.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 16f, FontStyle.Bold)
        };

        var body = new Label
        {
            Left = 16,
            Top = 48,
            Width = framePanel.ClientSize.Width - 32,
            Height = 250,
            Text = promptText + Environment.NewLine + string.Join(Environment.NewLine, selectable.Select((c, i) =>
            {
                var classText = includeClassInList
                    ? $" ({string.Join("/", c.Classes.Select(cls => cls.ToDisplayString()))})"
                    : string.Empty;
                return $"{i + 1}) {c.Name}{classText}";
            })),
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 11f, FontStyle.Bold)
        };

        int selectedIndex = -1;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
                form.Close();
                return;
            }

            if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                var idx = (int)e.KeyCode - (int)Keys.D1;
                if (idx >= 0 && idx < selectable.Count)
                {
                    selectedIndex = idx;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(body);
        form.Controls.Add(framePanel);

        var options = selectable
            .Select((c, i) => new ViewerPromptOption($"pick:{i + 1}", c.Name))
            .ToList();

        var prompt = new ViewerPrompt("choice", promptText, null, options);
        var answers = selectable
            .Select((c, i) => new { Key = $"pick:{i + 1}", Value = DialogResult.OK })
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        var viewerPick = ViewerDialog.RunModal(form, owner, prompt, answers, p => ViewerPromptChanged?.Invoke(session, p));
        ViewerPromptChanged?.Invoke(session, null);

        if (viewerPick == DialogResult.OK && selectedIndex < 0)
            selectedIndex = 0;

        return selectedIndex >= 0 && selectedIndex < selectable.Count ? selectable[selectedIndex] : null;
    }

    private static bool IsThiefClass(Character c)
        => c.Classes.Contains(CharacterClass.Thief) || c.Classes.Contains(CharacterClass.Assassin);

    private static bool IsMageOrIllusionist(Character c)
        => c.Classes.Contains(CharacterClass.MagicUser) || c.Classes.Contains(CharacterClass.Illusionist);

    private static bool IsClericOrDruid(Character c)
        => c.Classes.Contains(CharacterClass.Cleric) || c.Classes.Contains(CharacterClass.Druid);

    private static string FormatTrapName(ChestTrapType trap)
    {
        return trap switch
        {
            ChestTrapType.None => "No trap",
            ChestTrapType.PoisonNeedle => "Poison Needle",
            ChestTrapType.ExplodingBox => "Exploding Box",
            ChestTrapType.GasBomb => "Gas Bomb",
            ChestTrapType.CrossbowBolt => "Crossbow Bolt",
            ChestTrapType.Alarm => "Alarm",
            ChestTrapType.MageBlaster => "Mage Blaster",
            ChestTrapType.PriestBlaster => "Priest Blaster",
            _ => "No trap"
        };
    }

    private static void DistributeCoin(List<Character> survivors, int totalAmount, Action<Character, int> add)
    {
        if (totalAmount <= 0 || survivors.Count == 0)
            return;

        int each = totalAmount / survivors.Count;
        int remainder = totalAmount % survivors.Count;

        for (int i = 0; i < survivors.Count; i++)
        {
            add(survivors[i], each + (i < remainder ? 1 : 0));
        }
    }
}
