using System;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Data.Characters;
using Adnd.Data.Party;
using Adnd.Data.Spells;

namespace Adnd.Game;

public class PartyMenu
{
    private readonly CharacterRepository _repo = new("Data/Characters");
    private readonly PartyRepository _partyRepo = new("Data/Party");
    private readonly SpellRepository _spellRepo = new("Data/Spells");
    private readonly SpellCastingService _spellCastingService;

    public PartyMenu()
    {
        var resolver = new SpellResolver(new ISpellEffectHandler[]
        {
            new CureLightWoundsHandler(),
            new BarkskinHandler(),
            new CureSeriousWoundsHandler(),
            new CureCriticalWoundsHandler(),
            new HealHandler(),
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
            new HoldMonsterHandler(),
            new BlessHandler(),
            new SleepHandler(),
            new InvisibilityHandler(),
            new IceStormHandler(),
            new LightningBoltHandler(),
            new WallOfFireHandler(),
            new CloudkillHandler(),
            new DisintegrateHandler(),
            new DeathFogHandler(),
            new DelayedBlastFireballHandler(),
            new FingerOfDeathHandler(),
            new IncendiaryCloudHandler(),
            new MeteorSwarmHandler(),
            new PowerWordStunHandler(),
            new PowerWordKillHandler()
        });

        _spellCastingService = new SpellCastingService(resolver, _spellRepo.LoadAll());
    }

    public void Show()
    {
        while (true)
        {
            var party = _partyRepo.Load();
            var roster = _repo.GetAll().ToList();
            // Build a lookup of roster characters and compute active (found) party members.
            var rosterDict = roster.ToDictionary(c => c.Name, c => c);
            var activeMembers = party.Members.Where(m => rosterDict.ContainsKey(m)).ToList();

            // Auto-clean: remove not-found members from party permanently
            if (activeMembers.Count != party.Members.Count)
            {
                party.Members = activeMembers;
                _partyRepo.Save(party);
            }

            Console.Clear();
            Console.WriteLine("=== PARTY MANAGEMENT ===");
            Console.WriteLine($"Party Members: {activeMembers.Count} / 6\n");

            if (activeMembers.Count == 0)
                Console.WriteLine("(Party is empty)");
            else
            {
                // Column headers
                Console.WriteLine($"{"#",-3} {"Name",-15} {"Race",-10} {"Class",-18} {"Lvl",-7} {"HP",7} {"AC",3} {"Status",-20}");
                Console.WriteLine(new string('-', 87));

                for (int i = 0; i < activeMembers.Count; i++)
                {
                    var name = activeMembers[i];
                    var c = rosterDict[name];
                    var cls = c.Classes != null && c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString())) : c.Class.ToDisplayString();
                    var hpDisplay = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
                    var statusInfo = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";
                    var levelDisplay = GetLevelDisplay(c);

                    Console.WriteLine($"{i + 1,-3} {c.Name,-15} {c.Race.ToDisplayString(),-10} {cls,-18} {levelDisplay,-7} {hpDisplay,7} {c.ArmorClass,3} {statusInfo,-20}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("\nA)dd Member");
            Console.WriteLine("R)emove Member");
            Console.WriteLine("I)nspect Member");
            Console.WriteLine("C)hange order of Member");
            Console.WriteLine("T)avern on the tabletop");
            Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.T) { TabletopTavern(); continue; }

            // Check for number keys 1-6 to inspect party member directly
            if (key >= ConsoleKey.D1 && key <= ConsoleKey.D6)
            {
                int index = key - ConsoleKey.D1; // D1 = 0, D2 = 1, etc.
                if (index < activeMembers.Count)
                {
                    var name = activeMembers[index];
                    var c = _repo.GetAll().FirstOrDefault(x => x.Name == name);
                    if (c != null)
                    {
                        ShowCharacterDetail(c, party);
                    }
                }
            }
            else if (key == ConsoleKey.A) AddMember(roster, party);
            else if (key == ConsoleKey.R) RemoveMember(party);
            else if (key == ConsoleKey.I) InspectMember(party);
            else if (key == ConsoleKey.C) ChangeOrderOfMembers(party);
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter) break;
        }
    }


    private void InspectMember(Party party)
    {
        var rosterDict = _repo.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var activeMembers = party.Members.Where(m => rosterDict.ContainsKey(m)).ToList();

        if (activeMembers.Count == 0)
        {
            Console.WriteLine("Party is empty.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("=== INSPECT MEMBER ===\n");
        Console.WriteLine($"{"#",-3} {"Name",-15} {"Race",-10} {"Class",-18} {"Lvl",-7} {"HP",7} {"AC",3} {"Status",-20}");
        Console.WriteLine(new string('-', 87));

        for (int i = 0; i < activeMembers.Count; i++)
        {
            var c = rosterDict[activeMembers[i]];
            var cls = c.Classes != null && c.Classes.Count > 0
                ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                : c.Class.ToDisplayString();
            var hpDisplay = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
            var statusInfo = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";
            var levelDisplay = GetLevelDisplay(c);

            Console.WriteLine($"{i + 1,-3} {c.Name,-15} {c.Race.ToDisplayString(),-10} {cls,-18} {levelDisplay,-7} {hpDisplay,7} {c.ArmorClass,3} {statusInfo,-20}");
        }

        Console.Write("\nChoose #: ");
        var sel = InputHelper.ReadNumber(1, activeMembers.Count);
        if (sel.HasValue)
        {
            var name = activeMembers[sel.Value - 1];
            var c = _repo.GetAll().FirstOrDefault(x => x.Name == name);
            if (c == null)
            {
                Console.WriteLine("Character data not found.");
                Console.ReadKey(true);
            }
            else
            {
                ShowCharacterDetail(c, party);
            }
        }
        else
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
        }
    }

    private void ShowCharacterDetail(Character c, Party party)
    {
        while (true)
        {
            // Reload character to ensure we have the latest data
            var updated = _repo.GetAll().FirstOrDefault(x => x.Name == c.Name);
            if (updated == null)
            {
                Console.WriteLine("Character no longer exists.");
                Console.ReadKey(true);
                break;
            }
            c = updated;

            Console.Clear();
            Console.WriteLine($"=== {c.Name} ===\n");
            Console.WriteLine(c.ToString());

            Console.WriteLine("\n=== EQUIPPED ITEMS ===");
            foreach (var kv in c.Equipment)
            {
                if (kv.Value == null)
                    Console.WriteLine($" - {kv.Key}: (empty)");
                else
                    Console.WriteLine($" - {kv.Key}: {kv.Value.Name}");
            }

            Console.WriteLine("\n=== INVENTORY ===");
            if (c.Inventory.Count == 0)
                Console.WriteLine(" (empty)");
            else
                for (int i = 0; i < c.Inventory.Count; i++)
                    Console.WriteLine($"{i + 1}. {c.Inventory[i].Name}");

            Console.WriteLine("\n=== SPELLCASTING ===");
            if (c.Spellcasting == null || c.Spellcasting.Count == 0)
            {
                Console.WriteLine(" (no spellcastings)");
            }
            else
            {
                foreach (var state in c.Spellcasting)
                {
                    SyncAutoKnownSpells(c, state);
                    var all = _spellRepo.LoadByClass(state.SpellClass);
                    var known = all.Where(s => state.KnownSpellIds.Contains(s.Id)).ToList();
                    Console.WriteLine($" - {state.SpellClass}: known {known.Count}, prepared {state.PreparedSpells.Sum(ps => ps.Count)}");

                    for (int lvl = 0; lvl < state.SlotsPerDay.Count; lvl++)
                    {
                        var max = state.SlotsPerDay[lvl];
                        if (max <= 0)
                            continue;
                        var used = lvl < state.SlotsUsed.Count ? state.SlotsUsed[lvl] : 0;
                        Console.WriteLine($"   L{lvl + 1} slots: {Math.Max(0, max - used)}/{max}");
                    }

                    if (known.Count > 0)
                    {
                        Console.WriteLine("   Known: " + string.Join(", ", known.Select(s => $"L{s.Level} {s.Name}")));
                    }
                }
            }

            Console.WriteLine("\nR)ead");
            Console.WriteLine("E)quip");
            Console.WriteLine("U)nequip");
            Console.WriteLine("T)rade");
            Console.WriteLine("D)rop");
            Console.WriteLine("P)ool Gold");
            Console.WriteLine("I)dentify");
            if (CanUseMemorizeAction(c))
                Console.WriteLine("M)emorize Spells");
            Console.WriteLine("C)ast Spell");
        Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.R) ReadAction(c);
            else if (key == ConsoleKey.E) EquipAction(c);
            else if (key == ConsoleKey.U) UnequipAction(c);
            else if (key == ConsoleKey.T) TradeAction(c, party);
            else if (key == ConsoleKey.D) DropAction(c);
            else if (key == ConsoleKey.P) PoolGoldAction(c, party);
            else if (key == ConsoleKey.I) IdentifyAction(c);
            else if (key == ConsoleKey.M && CanUseMemorizeAction(c)) MemorizeSpellAction(c);
            else if (key == ConsoleKey.C) CastSpellAction(c, party);
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter) break;
        }
    }

    private static bool CanUseMemorizeAction(Character c)
    {
        return c.Classes.Any(cls => cls == CharacterClass.MagicUser
                                    || cls == CharacterClass.Illusionist
                                    || cls == CharacterClass.Ranger);
    }

    private void MemorizeSpellAction(Character c)
    {
        if (!CanUseMemorizeAction(c))
            return;

        var states = c.Spellcasting;
        if (states == null || states.Count == 0)
        {
            Console.WriteLine("\nThis character has no spellcasting.");
            Console.ReadKey(true);
            return;
        }

        SpellcastingState state;
        if (states.Count == 1)
        {
            state = states[0];
        }
        else
        {
            Console.WriteLine("\nChoose spellcasting type:");
            for (int i = 0; i < states.Count; i++)
            {
                var unlockedLevels = states[i].SlotsPerDay
                    .Select((v, idx) => (v, idx))
                    .Where(x => x.v > 0)
                    .Select(x => $"L{x.idx + 1}:{x.v}");
                Console.WriteLine($"{(char)('A' + i)}. {states[i].SpellClass} ({string.Join(", ", unlockedLevels)})");
            }

            Console.Write("Choose type letter (or Enter to cancel): ");
            var stateIdx = InputHelper.ReadLetterIndex(states.Count);
            if (!stateIdx.HasValue)
                return;
            state = states[stateIdx.Value];
        }

        SyncAutoKnownSpells(c, state);
        var classSpells = _spellRepo.LoadByClass(state.SpellClass);
        if (classSpells.Count == 0)
        {
            Console.WriteLine("\nNo spells are defined for this spell class yet.");
            Console.ReadKey(true);
            return;
        }

        var knownSpells = classSpells
            .Where(s => state.KnownSpellIds.Contains(s.Id))
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();

        if (knownSpells.Count == 0)
        {
            Console.WriteLine("\nNo known spells available to memorize.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine($"\nKnown spells ({state.SpellClass}):");
        for (int i = 0; i < knownSpells.Count; i++)
        {
            var s = knownSpells[i];
            var lvlIdx = s.Level - 1;
            var maxAtLevel = lvlIdx >= 0 && lvlIdx < state.SlotsPerDay.Count ? state.SlotsPerDay[lvlIdx] : 0;
            var preparedAtLevel = state.PreparedSpells
                .Join(classSpells, p => p.SpellId, sp => sp.Id, (p, sp) => new { p.Count, sp.Level })
                .Where(x => x.Level == s.Level)
                .Sum(x => x.Count);
            Console.WriteLine($"{(char)('A' + i)}. L{s.Level} {s.Name} (prepared {preparedAtLevel}/{maxAtLevel})");
        }

        Console.Write("Choose spell letter (or Enter to cancel): ");
        var idx = InputHelper.ReadLetterIndex(knownSpells.Count);
        if (!idx.HasValue)
            return;

        var chosen = knownSpells[idx.Value];
        var levelIndex = chosen.Level - 1;
        if (levelIndex < 0 || levelIndex >= state.SlotsPerDay.Count || state.SlotsPerDay[levelIndex] <= 0)
        {
            Console.WriteLine("\nNo spell slots available for that spell level.");
            Console.ReadKey(true);
            return;
        }

        var preparedForLevel = state.PreparedSpells
            .Join(classSpells, p => p.SpellId, sp => sp.Id, (p, sp) => new { p.Count, sp.Level })
            .Where(x => x.Level == chosen.Level)
            .Sum(x => x.Count);

        if (preparedForLevel >= state.SlotsPerDay[levelIndex])
        {
            Console.WriteLine("\nAll slots for that spell level are already memorized.");
            Console.ReadKey(true);
            return;
        }

        var prepared = state.PreparedSpells.FirstOrDefault(ps => ps.SpellId == chosen.Id);
        if (prepared == null)
        {
            state.PreparedSpells.Add(new PreparedSpell { SpellId = chosen.Id, Count = 1 });
        }
        else
        {
            prepared.Count += 1;
        }

        _repo.Save(c);
        Console.WriteLine($"\nMemorized: {chosen.Name} (Level {chosen.Level})");
        Console.ReadKey(true);
    }

    private void CastSpellAction(Character c, Party party)
    {
        var states = c.Spellcasting;
        if (states == null || states.Count == 0)
            return;

        SpellcastingState state;
        if (states.Count == 1)
        {
            state = states[0];
        }
        else
        {
            Console.WriteLine("\nChoose spellcasting type:");
            for (int i = 0; i < states.Count; i++)
            {
                var unlockedLevels = states[i].SlotsPerDay
                    .Select((v, idx) => (v, idx))
                    .Where(x => x.v > 0)
                    .Select(x => $"L{x.idx + 1}:{x.v}");
                Console.WriteLine($"{(char)('A' + i)}. {states[i].SpellClass} ({string.Join(", ", unlockedLevels)})");
            }

            Console.Write("Choose type letter (or Enter to cancel): ");
            var stateIdx = InputHelper.ReadLetterIndex(states.Count);
            if (!stateIdx.HasValue)
                return;
            state = states[stateIdx.Value];
        }

        SyncAutoKnownSpells(c, state);

        var allForClass = _spellRepo.LoadByClass(state.SpellClass)
            .Where(s => s.CastContext is SpellCastContext.Both or SpellCastContext.Exploration)
            .ToList();

        bool isAutoMemorizedClass = IsAutoMemorizedClass(state.SpellClass);

        var castable = allForClass
            .Where(spell =>
            {
                var levelIdx = spell.Level - 1;
                if (levelIdx < 0 || levelIdx >= state.SlotsPerDay.Count)
                    return false;

                if (state.SlotsPerDay[levelIdx] <= 0)
                    return false;

                var used = levelIdx < state.SlotsUsed.Count ? state.SlotsUsed[levelIdx] : 0;
                if (used >= state.SlotsPerDay[levelIdx])
                    return false;

                if (isAutoMemorizedClass)
                    return true;

                var knows = state.KnownSpellIds.Contains(spell.Id);
                var prepared = state.PreparedSpells.Any(ps => ps.SpellId == spell.Id && ps.Count > 0);
                return knows && prepared;
            })
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();

        if (castable.Count == 0)
        {
            Console.WriteLine("\nNo castable exploration spells available.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine("\nCastable spells:");
        for (int i = 0; i < castable.Count; i++)
            Console.WriteLine($"{(char)('A' + i)}. L{castable[i].Level} {castable[i].Name}");

        Console.Write("Choose spell letter (or Enter to cancel): ");
        var idx = InputHelper.ReadLetterIndex(castable.Count);
        if (!idx.HasValue)
            return;

        var spell = castable[idx.Value];

        var roster = _repo.GetAll().ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        var partyMembers = party.Members
            .Where(name => roster.ContainsKey(name))
            .Select(name => roster[name])
            .ToList();

        if (partyMembers.Count == 0)
        {
            Console.WriteLine("\nNo valid party members found.");
            Console.ReadKey(true);
            return;
        }

        var caster = partyMembers.FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase)) ?? c;

        var targets = new List<SpellCastTarget>();

        if (spell.RangeType == SpellRangeType.Self)
        {
            targets.Add(SpellCastTarget.Ally(caster));
        }
        else if (spell.RangeType == SpellRangeType.Ally)
        {
            Console.WriteLine("\nChoose ally target:");
            for (int i = 0; i < partyMembers.Count; i++)
                Console.WriteLine($"{i + 1}. {partyMembers[i].Name} (HP {partyMembers[i].CurrentHitPoints}/{partyMembers[i].MaxHitPoints})");

            Console.Write("Choose #: ");
            var targetSel = InputHelper.ReadNumber(1, partyMembers.Count);
            if (!targetSel.HasValue)
                return;

            targets.Add(SpellCastTarget.Ally(partyMembers[targetSel.Value - 1]));
        }
        else
        {
            Console.WriteLine("\nEnemy-target spells require combat.");
            Console.ReadKey(true);
            return;
        }

        var result = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = spell.Id,
            Context = SpellUseContext.Exploration,
            Targets = targets,
            PartyTargets = partyMembers,
            MonsterTargets = new List<Adnd.Core.Combat.Sessions.MonsterInstance>()
        });

        foreach (var line in result.Events)
            Console.WriteLine($"\n{line}");

        if (result.Success)
        {
            foreach (var member in partyMembers)
                _repo.Save(member);
            _repo.Save(caster);
        }

        Console.ReadKey(true);
    }

    private void SyncAutoKnownSpells(Character c, SpellcastingState state)
    {
        if (!IsAutoMemorizedClass(state.SpellClass))
            return;

        var classSpells = _spellRepo.LoadByClass(state.SpellClass);
        var maxUnlockedLevel = 0;
        for (int i = state.SlotsPerDay.Count - 1; i >= 0; i--)
        {
            if (state.SlotsPerDay[i] > 0)
            {
                maxUnlockedLevel = i + 1;
                break;
            }
        }

        var shouldKnow = classSpells
            .Where(s => s.Level <= maxUnlockedLevel)
            .Select(s => s.Id)
            .Distinct()
            .ToList();

        var shouldPrepared = shouldKnow
            .Select(id => new PreparedSpell { SpellId = id, Count = 1 })
            .ToList();

        var knownChanged = state.KnownSpellIds.Count != shouldKnow.Count || state.KnownSpellIds.Except(shouldKnow).Any();
        var preparedChanged = state.PreparedSpells.Count != shouldPrepared.Count
                              || state.PreparedSpells.Any(ps => !shouldPrepared.Any(sp => sp.SpellId == ps.SpellId && sp.Count == ps.Count));

        if (knownChanged || preparedChanged)
        {
            state.KnownSpellIds = shouldKnow;
            state.PreparedSpells = shouldPrepared;
            _repo.Save(c);
        }
    }

    private static bool IsAutoMemorizedClass(SpellClass spellClass)
    {
        if (spellClass is SpellClass.Cleric or SpellClass.Druid)
            return true;

        return GameRulesProvider.Current.AutoMemorizeArcaneSpellsDaily
               && spellClass is SpellClass.MagicUser or SpellClass.Illusionist;
    }

    private void ReadAction(Character c)
    {
        Console.WriteLine("\n[Read action not yet implemented]");
        Console.ReadKey(true);
    }

    private void EquipAction(Character c)
    {
        EquipmentHelper.PromptAndEquipItem(c, _repo);
    }

    private void UnequipAction(Character c)
    {
        var equipped = c.Equipment
            .Where(kv => kv.Value != null)
            .ToList();

        if (equipped.Count == 0)
            return;

        Console.WriteLine("\nUnequip from:");
        for (int i = 0; i < equipped.Count; i++)
        {
            var itemName = equipped[i].Value!.Name;
            Console.WriteLine($"{i + 1}. {equipped[i].Key} ({itemName})");
        }

        Console.Write("Choose # (or Enter to cancel): ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > equipped.Count)
            return;

        var slot = equipped[idx - 1].Key;
        if (EquipmentManager.Unequip(c, slot))
        {
            _repo.Save(c);
        }
    }

    private void TradeAction(Character c, Party party)
    {
        var members = party.Members
            .Select(name => _repo.GetAll().FirstOrDefault(x => x.Name == name))
            .Where(x => x != null)
            .Cast<Character>()
            .ToList();

        var giver = members.FirstOrDefault(m => string.Equals(m.Name, c.Name, StringComparison.OrdinalIgnoreCase));
        if (giver == null)
        {
            Console.WriteLine("\nActive character is not in the current party.");
            Console.ReadKey(true);
            return;
        }

        if (giver.Inventory.Count == 0)
        {
            Console.WriteLine("\nNo items to trade.");
            Console.ReadKey(true);
            return;
        }

        var recipients = members
            .Where(m => !string.Equals(m.Name, giver.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (recipients.Count == 0)
        {
            Console.WriteLine("\nNo other party member to trade with.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine("\nTrade with:");
        for (int i = 0; i < recipients.Count; i++)
            Console.WriteLine($"{i + 1}. {recipients[i].Name} ({recipients[i].Class})");
  //      Console.WriteLine($"{i + 1}. {recipients[i].Name}");
//        var recipientIdx = PromptChoice("Trade With", recipients.Select(r => $"{r.Name} ({r.Class})").ToList());

        Console.Write("Choose #: ");
        var recipientSelection = InputHelper.ReadNumber(1, recipients.Count);
        if (!recipientSelection.HasValue)
            return;

        var receiver = recipients[recipientSelection.Value - 1];

        Console.WriteLine("\nSelect item to trade:");
        for (int i = 0; i < giver.Inventory.Count; i++)
        {
            var it = giver.Inventory[i];
            Console.WriteLine($"{i + 1}. {it.Name} (Wt {it.Weight})");
        }

        Console.Write("Choose #: ");
        var itemSelection = InputHelper.ReadNumber(1, giver.Inventory.Count);
        if (!itemSelection.HasValue)
            return;

        var item = giver.Inventory[itemSelection.Value - 1];

        if (!receiver.CanCarry(item))
        {
            Console.WriteLine($"\n{receiver.Name} cannot carry more weight ({receiver.CurrentCarryWeight}/{receiver.MaxCarryWeight}).");
            Console.ReadKey(true);
            return;
        }

        giver.Inventory.Remove(item);
        receiver.Inventory.Add(item);

        _repo.Save(giver);
        _repo.Save(receiver);

        Console.WriteLine($"\nTraded {item.Name} from {giver.Name} to {receiver.Name}.");
        Console.ReadKey(true);
    }

    private void DropAction(Character c)
    {
        Console.WriteLine("\n[Drop action not yet implemented]");
        Console.ReadKey(true);
    }

    private void PoolGoldAction(Character c, Party party)
    {
        var members = party.Members
            .Select(name => _repo.GetAll().FirstOrDefault(x => x.Name == name))
            .Where(x => x != null)
            .Cast<Character>()
            .ToList();

        if (members.Count == 0)
        {
            Console.WriteLine("\nNo party members found.");
            Console.ReadKey(true);
            return;
        }

        var receiver = members.FirstOrDefault(m => string.Equals(m.Name, c.Name, StringComparison.OrdinalIgnoreCase));
        if (receiver == null)
        {
            Console.WriteLine("\nActive character is not in the current party.");
            Console.ReadKey(true);
            return;
        }

        var pooled = 0;
        foreach (var member in members)
        {
            if (string.Equals(member.Name, receiver.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            pooled += member.GoldPieces;
            member.GoldPieces = 0;
            _repo.Save(member);
        }

        receiver.GoldPieces += pooled;
        _repo.Save(receiver);

        Console.WriteLine($"\nPooled {pooled} gp to {receiver.Name}. New total: {receiver.GoldPieces} gp");
        Console.ReadKey(true);
    }

    private void IdentifyAction(Character c)
    {
        Console.WriteLine("\n[Identify action not yet implemented]");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Hands the party over to the viewer and takes instructions back until the player leaves.
    ///
    /// Additive on purpose: the console menu above still does everything it did, so this can be wrong or
    /// absent without stranding anyone. The loop is deliberately dumb -- publish, wait for a command,
    /// apply it, publish again -- because <see cref="TavernSession"/> holds all the decisions and this
    /// only has to keep the two ends talking.
    /// </summary>
    private void TabletopTavern()
    {
        var bridge = new Adnd.Game.Viewer.TabletopViewerBridge();
        var session = new Adnd.Game.Viewer.TavernSession(_repo, _partyRepo, _partyRepo.Load());

        Console.Clear();
        Console.WriteLine("=== TAVERN (tabletop) ===\n");
        Console.WriteLine("The tavern is laid out in the viewer.");
        Console.WriteLine("Click a figurine to move them between the bench and the party.");
        Console.WriteLine("\nPress Esc here, or Leave on the table, to come back.");

        if (!bridge.Enabled)
        {
            Console.WriteLine("\nThe viewer is not running, so there is nothing to click.");
            Console.WriteLine("Press any key.");
            Console.ReadKey(true);
            return;
        }

        bridge.PublishTavern(session.Party(), session.Bench(), session.Prompt());

        while (true)
        {
            // The console keeps its own way out, so a viewer that has crashed or been closed cannot trap
            // the player in a menu that is waiting for a click nobody can make.
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape) return;

            var command = bridge.TryTakeCommand();
            if (command == null) { System.Threading.Thread.Sleep(60); continue; }

            if (command == "back") return;

            if (session.Apply(command))
                bridge.PublishTavern(session.Party(), session.Bench(), session.Prompt());
        }
    }

    private void AddMember(System.Collections.Generic.List<Character> roster, Party party)
    {
        // Only count members that currently resolve to roster characters
        var rosterDict = roster.ToDictionary(c => c.Name, c => c);
        var activeMembers = party.Members.Where(m => rosterDict.ContainsKey(m)).ToList();

        if (activeMembers.Count >= 6)
        {
            Console.WriteLine("Party is full.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("=== ADD MEMBER ===");

        for (int i = 0; i < roster.Count; i++)
        {
            var c = roster[i];
            var cls = c.Classes != null && c.Classes.Count > 0 ? string.Join("/", c.Classes) : c.Class.ToString();
            var inParty = party.Members.Any(m => m == c.Name) ? " (Already in party)" : "";
            Console.WriteLine($"{i + 1}. {c.Name} ({cls}){inParty}");
        }

        Console.Write("\nChoose #: ");
        var sel = InputHelper.ReadNumber(1, roster.Count, 2);
        if (sel.HasValue)
        {
            var chosen = roster[sel.Value - 1];

            if (party.Members.Any(m => m == chosen.Name))
            {
                Console.WriteLine("Character already in party.");
                Console.ReadKey(true);
            }
            else
            {
                party.Members.Add(chosen.Name);
                _partyRepo.Save(party);
                // No confirmation message; return directly to party menu
            }
        }
        else
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
        }
    }

    private void RemoveMember(Party party)
    {
        var rosterDict = _repo.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var activeMembers = party.Members.Where(m => rosterDict.ContainsKey(m)).ToList();

        if (activeMembers.Count == 0)
        {
            Console.WriteLine("Party is empty.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("=== REMOVE MEMBER ===\n");
        Console.WriteLine($"{"#",-3} {"Name",-15} {"Class",-16} {"HP",-8} {"Status",-20}");
        Console.WriteLine(new string('-', 66));

        for (int i = 0; i < activeMembers.Count; i++)
        {
            var c = rosterDict[activeMembers[i]];
            var cls = c.Classes != null && c.Classes.Count > 0
                ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                : c.Class.ToDisplayString();
            var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
            var aliveDead = c.HasStatus(CharacterStatus.Dead) || c.CurrentHitPoints <= 0 ? "Dead" : "Alive";
            var fullStatus = c.Status != CharacterStatus.None ? $"{aliveDead} ({GetStatusDisplay(c)})" : aliveDead;

            Console.WriteLine($"{i + 1,-3} {c.Name,-15} {cls,-16} {hp,-8} {fullStatus,-20}");
        }

        Console.Write("\nChoose #: ");
        var sel = InputHelper.ReadNumber(1, activeMembers.Count);
        if (sel.HasValue)
        {
            var toRemove = activeMembers[sel.Value - 1];
            party.Members.Remove(toRemove);
            _partyRepo.Save(party);
        }
        else
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
        }
    }

    private void ChangeOrderOfMembers(Party party)
    {
        var rosterDict = _repo.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var activeMembers = party.Members.Where(m => rosterDict.ContainsKey(m)).ToList();

        if (activeMembers.Count < 2)
        {
            Console.WriteLine("Need at least two party members to change order.");
            Console.ReadKey(true);
            return;
        }

        var changed = ReorderMembersInteractive(activeMembers, rosterDict, "=== CHANGE PARTY ORDER ===");
        if (!changed)
            return;

        party.Members = activeMembers;
        _partyRepo.Save(party);
    }

    public bool ReorderMembersInteractive(List<string> members, Dictionary<string, Character> rosterDict, string heading)
    {
        if (members == null || members.Count < 2)
            return false;

        var original = members.ToList();

        while (true)
        {
            Console.Clear();
            Console.WriteLine(heading);
            Console.WriteLine();
            Console.WriteLine("Old order:");
            Console.WriteLine($"{"#",-3} {"Name",-15} {"Class",-18} {"HP",-8} {"AC",-4} {"Status",-20}");
            Console.WriteLine(new string('-', 74));

            for (int i = 0; i < original.Count; i++)
            {
                var name = original[i];
                if (!rosterDict.TryGetValue(name, out var c))
                    continue;

                var cls = c.Classes != null && c.Classes.Count > 0
                    ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                    : c.Class.ToDisplayString();
                var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
                var status = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";

                Console.WriteLine($"{i + 1,-3} {c.Name,-15} {cls,-18} {hp,-8} {c.ArmorClass,-4} {status,-20}");
            }

            Console.WriteLine();
            Console.WriteLine("R)eorder");
            Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.L || key == ConsoleKey.Enter)
                return false;

            if (key != ConsoleKey.R)
                continue;

            var picked = new List<int>();
            var used = new HashSet<int>();

            while (true)
            {
                Console.Clear();
                Console.WriteLine(heading);
                Console.WriteLine();
                Console.WriteLine("Type the new order using member numbers (1-6).\n");
                Console.WriteLine("Backspace = undo, Enter = confirm when complete, L = cancel");
                Console.WriteLine();
                Console.WriteLine("Input: " + string.Join(" ", picked.Select(x => (x + 1).ToString())));
                Console.WriteLine();

                Console.WriteLine("Old order:");
                Console.WriteLine($"{"#",-3} {"Name",-15} {"Class",-18} {"HP",-8} {"AC",-4} {"Status",-20}");
                Console.WriteLine(new string('-', 74));

                for (int i = 0; i < original.Count; i++)
                {
                    var name = original[i];
                    if (!rosterDict.TryGetValue(name, out var c))
                        continue;

                    var cls = c.Classes != null && c.Classes.Count > 0
                        ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                        : c.Class.ToDisplayString();
                    var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
                    var status = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";

                    Console.WriteLine($"{i + 1,-3} {c.Name,-15} {cls,-18} {hp,-8} {c.ArmorClass,-4} {status,-20}");
                }

                Console.WriteLine();
                Console.WriteLine("New order (building):");
                Console.WriteLine($"{"#",-3} {"Name",-15} {"Class",-18} {"HP",-8} {"AC",-4} {"Status",-20}");
                Console.WriteLine(new string('-', 74));

                if (picked.Count == 0)
                {
                    Console.WriteLine("(empty)");
                }
                else
                {
                    for (int i = 0; i < picked.Count; i++)
                    {
                        var name = members[picked[i]];
                        if (!rosterDict.TryGetValue(name, out var c))
                            continue;

                        var cls = c.Classes != null && c.Classes.Count > 0
                            ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                            : c.Class.ToDisplayString();
                        var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
                        var status = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";

                        Console.WriteLine($"{i + 1,-3} {c.Name,-15} {cls,-18} {hp,-8} {c.ArmorClass,-4} {status,-20}");
                    }
                }

                var inputKey = Console.ReadKey(true);

                if (inputKey.Key == ConsoleKey.L)
                    break;

                if (inputKey.Key == ConsoleKey.Backspace)
                {
                    if (picked.Count > 0)
                    {
                        var last = picked[^1];
                        picked.RemoveAt(picked.Count - 1);
                        used.Remove(last);
                    }
                    continue;
                }

                if (inputKey.Key == ConsoleKey.Enter)
                {
                    if (picked.Count == members.Count)
                    {
                        var reordered = picked.Select(i => members[i]).ToList();
                        if (reordered.SequenceEqual(original, StringComparer.OrdinalIgnoreCase))
                            return false;

                        members.Clear();
                        members.AddRange(reordered);
                        return true;
                    }

                    continue;
                }

                if (inputKey.Key >= ConsoleKey.D1 && inputKey.Key <= ConsoleKey.D9)
                {
                    int idx = inputKey.Key - ConsoleKey.D1;
                    if (idx >= 0 && idx < members.Count && !used.Contains(idx))
                    {
                        picked.Add(idx);
                        used.Add(idx);
                    }
                }
            }
        }
    }

    private static string GetLevelDisplay(Character c)
    {
        c.EnsureClassProgressions();

        if (c.Classes == null || c.Classes.Count <= 1)
            return c.Level.ToString();

        return string.Join("/", c.Classes.Select(c.GetClassLevel));
    }

    private string GetStatusDisplay(Character c)
    {
        var statuses = new System.Collections.Generic.List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        return string.Join(", ", statuses);
    }
}
