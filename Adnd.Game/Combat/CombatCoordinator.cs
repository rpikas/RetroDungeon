using Adnd.Core.Characters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Dices;
using Adnd.Core.Experience;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
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
using System.Windows.Forms;

namespace Adnd.Game.Combat;

public sealed class CombatCoordinator
{
    private readonly EncounterMonsterFactory _monsterFactory = new();
    private readonly CombatResolver _combatResolver;
    private readonly PartyRepository _partyRepository = new();
    private readonly LevelUpService _levelUpService = new();
    private readonly SpellRepository _spellRepository = new("Data/Spells");
    private readonly TreasureService _treasureService;
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
            new CallLightningHandler(),
            new EntangleHandler(),
            new FaerieFireHandler(),
            new BladeBarrierHandler(),
            new MagicMissileHandler(),
            new ChromaticOrbHandler(),
            new ShockingGraspHandler(),
            new MelfsAcidArrowHandler(),
            new HoldPersonHandler(),
            new HoldMonsterHandler(),
            new BlessHandler(),
            new SleepHandler(),
            new StrengthHandler(),
            new MirrorImageHandler(),
            new InvisibilityHandler(),
            new ShieldSpellHandler(),
            new FireballHandler(),
            new WallOfFireHandler(),
            new LightningBoltHandler(),
            new IceStormHandler(),
            new CloudkillHandler(),
            new DisintegrateHandler(),
            new DeathFogHandler(),
            new DelayedBlastFireballHandler(),
            new EarthquakeHandler(),
            new UnholyWordHandler(),
            new FingerOfDeathHandler(),
            new IncendiaryCloudHandler(),
            new MeteorSwarmHandler(),
            new PowerWordStunHandler(),
            new PowerWordKillHandler(),
            new ColorSprayHandler(),
            new FearHandler(),
            new PhantasmalForceHandler(),
        });

        var spellCastingService = new SpellCastingService(resolver, spellRepo.LoadAll());
        _combatResolver = new CombatResolver(spellCastingService: spellCastingService);

        var treasureRepo = new TreasureTableRepository("Data/Treasure");
        _treasureService = new TreasureService(treasureRepo, _random);
    }

    public CombatOutcome StartEncounter(IWin32Window owner, string monsterName, int monsterCount, List<Character> party, CharacterRepository characterRepository, int? dungeonLevel = null)
    {
        var monsters = _monsterFactory.CreateGroup(monsterName, monsterCount);
        var session = new CombatSession(party, monsters);
        RestorePersistedRoundEffects(session);
        EncounterStarted?.Invoke(session);

        while (session.Outcome == CombatOutcome.InProgress)
        {
            if (!session.AliveParty.Any())
            {
                session.Outcome = CombatOutcome.Defeat;
                break;
            }

            var aliveMonsters = session.AliveMonsters.ToList();
            var asleepMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Asleep));
            var heldMonsters = aliveMonsters.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
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
            using var encounterForm = new EncounterForm(monsterName, aliveMonsters.Count, asleepMonsters, heldMonsters, entangledMonsters, panickedMonsters, 
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
            ApplyShriekReinforcements(session, roundEvents);
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
        {
            ApplyVictoryRewards(owner, session, dungeonLevel);
        }

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
            ApplyShriekReinforcements(session, roundEvents);
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
        {
            ApplyVictoryRewards(owner, session, dungeonLevel);
        }

        RemoveTemporaryCombatEffects(session);

        foreach (var character in party)
            characterRepository.Save(character);

        MoveDeadPartyMembersToEnd(session.Party);
        ShowFinalOutcome(owner, session.Outcome, session);
        return session.Outcome;
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

        session.BlessedPartyMembers.Clear();
        session.InvisiblyBuffedPartyMembers.Clear();
        session.ImprovedInvisibilityRounds.Clear();
        session.BarkskinBonuses.Clear();
        session.BarkskinRounds.Clear();
        session.MirrorImageCounts.Clear();
        session.MirrorImageRounds.Clear();
    }
   
    private void ApplyVictoryRewards(IWin32Window owner, CombatSession session, int? dungeonLevel)
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
                TotalXP = totalXp
            };
        }).ToList();

        // --- NYTT: Summera XP från alla grupper ---
        int totalMonsterXp = groupXpInfos.Sum(g => g.TotalXP);

        // --- XP-fördelning ---
        var xpMultiplier = GameRulesProvider.Current.XpMultiplier;
        int xpEach = (int)Math.Round(totalMonsterXp * xpMultiplier / survivors.Count);
        int xpRemainder = totalMonsterXp % survivors.Count;


        var levelUpResults = new List<LevelUpResult>();
        var allSpells = _spellRepository.LoadAll();

        for (int i = 0; i < survivors.Count; i++)
        {
            var survivor = survivors[i];
            var baseGain = xpEach + (i < xpRemainder ? 1 : 0);
            var xpModifierPercent = XpBonusCalculator.GetXpModifier(survivor.Class, survivor.Abilities);
            var individualBonus = (int)Math.Round(baseGain * (xpModifierPercent / 100.0), MidpointRounding.AwayFromZero);

            var gain = baseGain + individualBonus;
            if (gain < 0)
                gain = 0;

            levelUpResults.Add(_levelUpService.ApplyExperienceAndAutoLevel(survivor, gain, allSpells));
        }

        var totalAwardedXp = levelUpResults.Sum(r => r.ExperienceAfter - r.ExperienceBefore);

        // --- Treasure etc (oförändrat) ---
        var treasure = _treasureService.RollTreasureForEncounter(session.Monsters);

        DistributeCoin(survivors, treasure.CopperPieces, (c, amount) => c.CopperPieces += amount);
        DistributeCoin(survivors, treasure.SilverPieces, (c, amount) => c.SilverPieces += amount);
        DistributeCoin(survivors, treasure.ElectrumPieces, (c, amount) => c.ElectrumPieces += amount);
        DistributeCoin(survivors, treasure.GoldPieces, (c, amount) => c.GoldPieces += amount);
        DistributeCoin(survivors, treasure.PlatinumPieces, (c, amount) => c.PlatinumPieces += amount);

        var valuablesValueGp = treasure.TotalGemValueGp + treasure.TotalJewelryValueGp + treasure.TotalArtValueGp;
        DistributeCoin(survivors, valuablesValueGp, (c, amount) => c.GoldPieces += amount);

        var magicAward = AwardMagicItemsFromPlaceholders(survivors, treasure.MagicPlaceholders, dungeonLevel);
        var randomItemsAward = AwardRandomItemsAfterCombat(survivors, dungeonLevel);

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
            sb.AppendLine();
        }
        if (groupXpInfos.Count > 1)
        { 
            sb.AppendLine($"Total XP from all groups: {totalMonsterXp}");
            sb.AppendLine($"XP multiplier: x{xpMultiplier:0.##}");
            sb.AppendLine($"Total awarded XP: {totalAwardedXp}");
            sb.AppendLine($"Survivors: {survivors.Count}");
            sb.AppendLine();
            sb.AppendLine("XP awards:");
        }
        foreach (var r in levelUpResults)
        {
            var gain = r.ExperienceAfter - r.ExperienceBefore;
            var survivor = survivors.FirstOrDefault(s => string.Equals(s.Name, r.CharacterName, StringComparison.OrdinalIgnoreCase));
            var xpModifierPercent = survivor == null ? 0 : XpBonusCalculator.GetXpModifier(survivor.Class, survivor.Abilities);
            sb.AppendLine($"- {r.CharacterName}: +{gain} XP (includes {xpModifierPercent:+#;-#;0}% individual bonus, total {r.ExperienceAfter})");
        }

        sb.AppendLine();
        sb.AppendLine("Treasure found:");
        sb.AppendLine($"- Coins: {treasure.CopperPieces} cp, {treasure.SilverPieces} sp, {treasure.ElectrumPieces} ep, {treasure.GoldPieces} gp, {treasure.PlatinumPieces} pp");

        if (treasure.Gems.Count > 0)
            sb.AppendLine($"- Gems: {treasure.Gems.Count} (total {treasure.TotalGemValueGp} gp)");
        if (treasure.Jewelry.Count > 0)
            sb.AppendLine($"- Jewelry: {treasure.Jewelry.Count} (total {treasure.TotalJewelryValueGp} gp)");
        if (treasure.Art.Count > 0)
            sb.AppendLine($"- Art: {treasure.Art.Count} (total {treasure.TotalArtValueGp} gp)");
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

        var leveled = levelUpResults.Where(x => x.LeveledUp).ToList();
        if (leveled.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Level ups:");

            foreach (var r in leveled)
            {
                sb.AppendLine($"- {r.CharacterName}: L{r.OldLevel} -> L{r.NewLevel} (HP +{r.HitPointsGained})");

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
        if (allItems.Count == 0)
            return result;

        var nextReceiverIndex = 0;

        foreach (var placeholder in placeholders)
        {
            var pool = GetItemPoolForMagicTable(allItems, placeholder.Table);
            if (pool.Count == 0)
            {
                for (int i = 0; i < Math.Max(1, placeholder.Count); i++)
                    result.UnassignedItems.Add($"{placeholder.Table} (no matching item defined)");
                continue;
            }

            var rolls = Math.Max(0, placeholder.Count);
            for (int i = 0; i < rolls; i++)
            {
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

    private static List<Item> GetItemPoolForMagicTable(List<Item> allItems, string table)
    {
        if (string.IsNullOrWhiteSpace(table))
            return new List<Item>();

        var key = table.Trim().ToLowerInvariant();
        return key switch
        {
            "potion" => allItems.Where(i => i.Type == ItemType.Potion).ToList(),
            "scroll" => allItems.Where(i => i.Type == ItemType.Scroll).ToList(),
            "weapon" => allItems.Where(i => i.Type == ItemType.Weapon).ToList(),
            "armor" => allItems.Where(i => i.Type == ItemType.Armor || i.Type == ItemType.Shield).ToList(),
            "magicitem" => allItems.Where(i => i.Type == ItemType.MagicItem).ToList(),
            _ => allItems.Where(i => i.Type == ItemType.MagicItem && i.Name.Contains(table, StringComparison.OrdinalIgnoreCase)).ToList()
        };
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

    private void ApplyShriekReinforcements(CombatSession session, List<CombatEvent> roundEvents)
    {
        var hasShriek = session.AliveMonsters.Any(m => m.Template.SpecialAbilities.Any(a =>
            string.Equals(a.Name?.Trim(), "Shriek", StringComparison.OrdinalIgnoreCase)));
        if (!hasShriek)
            return;

        var resolvedRound = session.Outcome == CombatOutcome.InProgress
            ? Math.Max(1, session.RoundNumber - 1)
            : session.RoundNumber;

        if (resolvedRound % 3 != 0)
            return;

        if (session.GetDistinctGroupIds().Count() >= 4)
            return;

        var roll = _dice.Roll(100);
        if (roll <= 50)
        {
            var candidates = _monsterRepository.GetAll()
                .Where(m => m.Source == Sources.Adnd)
                .ToList();

            if (candidates.Count > 0)
            {
                var selected = candidates[_random.Next(candidates.Count)];
                var count = _random.Next(Math.Max(1, selected.NumberOfAppearancesMin), Math.Max(selected.NumberOfAppearancesMin, selected.NumberOfAppearancesMax) + 1);
                var existingGroups = session.GetDistinctGroupIds().ToList();
                var nextGroupNumber = 1;
                while (existingGroups.Contains($"Group{nextGroupNumber}", StringComparer.OrdinalIgnoreCase))
                    nextGroupNumber++;

                var groupId = $"Group{nextGroupNumber}";
                var reinforcements = _monsterFactory.CreateGroup(selected.Name, count, groupId);
                session.Monsters.AddRange(reinforcements);
                roundEvents.Add(new CombatEvent($"Shriek attracts reinforcements: {count} {selected.Name}{(count > 1 ? "s" : string.Empty)} join the fight ({groupId})."));
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
