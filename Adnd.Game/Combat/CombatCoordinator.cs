using System.Text;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Spells.Casting;
using Adnd.Game.Viewer;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Core.Treasure;
using Adnd.Data.Characters;
using Adnd.Data.Encounters.Factories;
using Adnd.Data.Items;
using Adnd.Data.Monsters;
using Adnd.Data.Party;
using Adnd.Data.Spells;
using Adnd.Data.Treasure;

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
            new RaiseDeadHandler(),
            new ResurrectionHandler(),
            new SpiritualHammerHandler(),
            new GlyphOfWardingHandler(),
            new FlameStrikeHandler(),
            new InsectPlagueHandler(),
            new CallLightningHandler(),
            new BladeBarrierHandler(),
            new MagicMissileHandler(),
            new ChromaticOrbHandler(),
            new ShockingGraspHandler(),
            new MelfsAcidArrowHandler(),
            new HoldPersonHandler(),
            new HoldMonsterHandler(),
            new BlessHandler(),
            new SleepHandler(),
            new InvisibilityHandler(),
            new FireballHandler(),
            new WallOfFireHandler(),
            new LightningBoltHandler(),
            new IceStormHandler(),
            new CloudkillHandler(),
            new DisintegrateHandler(),
            new DeathFogHandler(),
            new DelayedBlastFireballHandler(),
            new FingerOfDeathHandler(),
            new IncendiaryCloudHandler(),
            new MeteorSwarmHandler(),
            new PowerWordStunHandler(),
            new PowerWordKillHandler(),
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
            var monsterTemplate = aliveMonsters.FirstOrDefault()?.Template;
            using var encounterForm = new EncounterForm(monsterName, aliveMonsters.Count, asleepMonsters, heldMonsters, session.Party, session.RoundNumber, dungeonLevel, monsterTemplate, session);
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
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
        {
            ApplyVictoryRewards(owner, session);
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
            RoundResolved?.Invoke(session);
            ShowRoundEvents(owner, roundEvents, session);

            MoveDeadPartyMembersToEnd(session.Party);
        }

        if (session.Outcome == CombatOutcome.Victory)
        {
            ApplyVictoryRewards(owner, session);
        }

        RemoveTemporaryCombatEffects(session);

        foreach (var character in party)
            characterRepository.Save(character);

        MoveDeadPartyMembersToEnd(session.Party);
        ShowFinalOutcome(owner, session.Outcome, session);
        return session.Outcome;
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

        session.BlessedPartyMembers.Clear();
        session.InvisiblyBuffedPartyMembers.Clear();
    }

    private void ApplyVictoryRewards(IWin32Window owner, CombatSession session)
    {
        var survivors = session.Party
            .Where(c => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead))
            .ToList();

        if (survivors.Count == 0)
            return;

        int totalMonsterXp = session.Monsters.Sum(m => Math.Max(0, m.Template.XPValue));
        int xpEach = totalMonsterXp / survivors.Count;
        int xpRemainder = totalMonsterXp % survivors.Count;
        var xpMultiplier = GameRulesProvider.Current.XpMultiplier;

        var levelUpResults = new List<LevelUpResult>();
        var allSpells = _spellRepository.LoadAll();
        for (int i = 0; i < survivors.Count; i++)
        {
            var baseGain = xpEach + (i < xpRemainder ? 1 : 0);
            var gain = (int)Math.Round(baseGain * xpMultiplier, MidpointRounding.AwayFromZero);
            if (gain < 0)
                gain = 0;

            levelUpResults.Add(_levelUpService.ApplyExperienceAndAutoLevel(survivors[i], gain, allSpells));
        }

        var totalAwardedXp = levelUpResults.Sum(r => r.ExperienceAfter - r.ExperienceBefore);

        var treasure = _treasureService.RollTreasureForEncounter(session.Monsters);

        DistributeCoin(survivors, treasure.CopperPieces, (c, amount) => c.CopperPieces += amount);
        DistributeCoin(survivors, treasure.SilverPieces, (c, amount) => c.SilverPieces += amount);
        DistributeCoin(survivors, treasure.ElectrumPieces, (c, amount) => c.ElectrumPieces += amount);
        DistributeCoin(survivors, treasure.GoldPieces, (c, amount) => c.GoldPieces += amount);
        DistributeCoin(survivors, treasure.PlatinumPieces, (c, amount) => c.PlatinumPieces += amount);

        var valuablesValueGp = treasure.TotalGemValueGp + treasure.TotalJewelryValueGp + treasure.TotalArtValueGp;
        DistributeCoin(survivors, valuablesValueGp, (c, amount) => c.GoldPieces += amount);

        var magicAward = AwardMagicItemsFromPlaceholders(survivors, treasure.MagicPlaceholders);

        // Random item finding after killing monsters
        var randomItemsAward = AwardRandomItemsAfterCombat(survivors);

        var sb = new StringBuilder();
        sb.AppendLine("Victory Rewards");
        sb.AppendLine();
        sb.AppendLine($"Monsters defeated: {session.Monsters.Count}");
        sb.AppendLine($"Base monster XP: {totalMonsterXp}");
        sb.AppendLine($"XP multiplier: x{xpMultiplier:0.##}");
        sb.AppendLine($"Total awarded XP: {totalAwardedXp}");
        sb.AppendLine($"Survivors: {survivors.Count}");
        sb.AppendLine();
        sb.AppendLine("XP awards:");

        foreach (var r in levelUpResults)
        {
            var gain = r.ExperienceAfter - r.ExperienceBefore;
            sb.AppendLine($"- {r.CharacterName}: +{gain} XP (total {r.ExperienceAfter})");
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

    private MagicAwardResult AwardMagicItemsFromPlaceholders(List<Character> survivors, List<TreasureMagicPlaceholderResult> placeholders)
    {
        var result = new MagicAwardResult();
        if (survivors.Count == 0 || placeholders == null || placeholders.Count == 0)
            return result;

        var allItems = _itemRepository.LoadAll().ToList();
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

    private MagicAwardResult AwardRandomItemsAfterCombat(List<Character> survivors)
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

        var allItems = _itemRepository.LoadAll().Where(i => i.IsShopBuyable).ToList();
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
            AllowedClasses = new List<CharacterClass>(source.AllowedClasses)
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
        var alive = combatParty
            .Where(c => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead))
            .ToList();
        var dead = combatParty
            .Where(c => c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead))
            .ToList();

        combatParty.Clear();
        combatParty.AddRange(alive);
        combatParty.AddRange(dead);

        var partyData = _partyRepository.Load();
        if (partyData.Members.Count == 0)
            return;

        var deadLookup = combatParty
            .ToDictionary(
                c => c.Name,
                c => c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead),
                StringComparer.OrdinalIgnoreCase);

        var aliveNames = new List<string>();
        var unknownNames = new List<string>();
        var deadNames = new List<string>();

        foreach (var memberName in partyData.Members)
        {
            if (!deadLookup.TryGetValue(memberName, out var isDead))
            {
                unknownNames.Add(memberName);
                continue;
            }

            if (isDead)
                deadNames.Add(memberName);
            else
                aliveNames.Add(memberName);
        }

        var reordered = aliveNames
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
