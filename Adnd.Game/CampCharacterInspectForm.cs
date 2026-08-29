using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Data.Characters;
using Adnd.Data.Party;
using Adnd.Data.Spells;
using Adnd.Game.Viewer;

namespace Adnd.Game;

public sealed class CampCharacterInspectForm : Form
{
    private readonly string _characterName;
    private readonly List<string> _partyMembers;
    private readonly CharacterRepository _characterRepository = new("Data/Characters");
    private readonly PartyRepository _partyRepository = new("Data/Party");
    private readonly SpellRepository _spellRepository = new("Data/Spells");
    private readonly SpellCastingService _spellCastingService;

    private readonly TextBox _detailsBox;
    private readonly FlowLayoutPanel _buttonsPanel;
    private readonly Label _oldStyleFooterLabel;
    private readonly Button _layOnHandsButton;

    /// <summary>
    /// Where this screen puts its questions so the tabletop can answer them. Null when nobody is watching the
    /// table, in which case everything here behaves exactly as it did: keyboard and mouse only.
    /// </summary>
    private readonly Action<ViewerPrompt?>? _publish;

    /// <summary>Live while this screen is open, so the table's clicks reach these buttons.</summary>
    private ViewerControlPump? _tableMenu;

    public CampCharacterInspectForm(string characterName, List<string> partyMembers,
                                    Action<ViewerPrompt?>? publish = null)
    {
        _characterName = characterName;
        _partyMembers = partyMembers;
        _publish = publish;

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
            new EntangleHandler(),
            new FaerieFireHandler(),
            new BladeBarrierHandler(),
            new MagicMissileHandler(),
            new ChromaticOrbHandler(),
            new ShockingGraspHandler(),
            new MelfsAcidArrowHandler(),
            new HoldMonsterHandler(),
            new BlessHandler(),
            new SleepHandler(),
            new InvisibilityHandler(),
            new ShieldSpellHandler(),
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
        _spellCastingService = new SpellCastingService(resolver, _spellRepository.LoadAll());

        Text = $"Inspect - {characterName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1020, 700);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        _detailsBox = new TextBox
        {
            Left = 12,
            Top = 12,
            Width = 996,
            Height = 560,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            // ForeColor = Color.White
            ForeColor = GameRulesProvider.Current.DefaultColor
        };

        _buttonsPanel = new FlowLayoutPanel
        {
            Left = 12,
            Top = 584,
            Width = 996,
            Height = 100,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        _buttonsPanel.Controls.Add(MakeButton("R)ead", (_, _) => MemorizeSpellAction()));
        _buttonsPanel.Controls.Add(MakeButton("E)quip", (_, _) => EquipAction()));
        _buttonsPanel.Controls.Add(MakeButton("T)rade", (_, _) => TradeAction()));
        _buttonsPanel.Controls.Add(MakeButton("D)rop", (_, _) => DropAction()));
        _buttonsPanel.Controls.Add(MakeButton("P)ool Gold", (_, _) => PoolGoldAction()));
        _buttonsPanel.Controls.Add(MakeButton("I)dentify", (_, _) => NotImplemented("Identify")));
        _buttonsPanel.Controls.Add(MakeButton("S)pell", (_, _) => CastSpellAction()));
        _buttonsPanel.Controls.Add(MakeButton("U)se Item", (_, _) => UseItemAction()));
        _buttonsPanel.Controls.Add(MakeButton("C)haracter Sheet", (_, _) => ShowCharacterSheetAction()));

        _layOnHandsButton = MakeButton("L)ay on Hands", (_, _) => LayOnHandsAction());
        _buttonsPanel.Controls.Add(_layOnHandsButton);
        _buttonsPanel.Controls.Add(MakeButton("L↵eave", (_, _) => Close()));

        _oldStyleFooterLabel = new Label
        {
            Left = 12,
            Top = 600,
            Width = 996,
            Height = 84,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 24f, FontStyle.Bold),
            Text = "R)EAD  T)RADE  P)OOL GOLD  S)PELL  L↵EAVE\nE)QUIP  D)ROP   I)DENTIFY  U)SE ITEM C)HARACTER SHEET",
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        Controls.Add(_detailsBox);
        Controls.Add(_buttonsPanel);
        Controls.Add(_oldStyleFooterLabel);

        ApplyUiStyleMode();
        RefreshView();

        KeyPreview = true;
        KeyDown += CampCharacterInspectForm_KeyDown;

        // The whole camp screen was unreachable from the table: you could not open it, and once open you could
        // not press any of these buttons. The pump starts on Shown so it is the newest one -- the maze is
        // underneath -- and every action republishes the menu afterwards, because whatever the action asked
        // will have replaced the question on the table with its own.
        Shown += (_, _) => StartTableMenu();
        FormClosed += (_, _) => _tableMenu?.Dispose();
    }

    /// <summary>What the table may press here. Read and Identify are left out: they are not implemented.</summary>
    private List<(string Id, string Label)> MenuActions()
    {
        var actions = new List<(string Id, string Label)>
        {
            ("read", "Read (memorize)"),
            ("equip", "Equip an item"),
            ("trade", "Trade"),
            ("drop", "Drop an item"),
            ("pool", "Pool gold"),
            ("identify", "Identify"),
            ("spell", "Spell"),
            ("useItem", "Use item"),
            ("characterSheet", "Character Sheet"),

        };

        var c = GetCharacter();
        if (c?.IsPaladin() == true)
            actions.Add(("layOnHands", c.LayOnHandsUsedToday ? "Lay on Hands (used today)" : "Lay on Hands"));

        actions.Add(("leave", "Leave"));
        return actions;
    }

    private void StartTableMenu()
    {
        if (_publish is null)
            return;

        PublishMenu();
        _tableMenu = ViewerControlPump.Start(this, NoKeys, _ => { }, command =>
        {
            switch (command)
            {
                case "read": MemorizeSpellAction(); break;
                case "equip": EquipAction(); break;
                case "trade": TradeAction(); break;
                case "drop": DropAction(); break;
                case "pool": PoolGoldAction(); break;
                case "identify": NotImplemented("Identify"); break;
                case "spell": CastSpellAction(); break;
                case "useItem": UseItemAction(); break;
                case "characterSheet": ShowCharacterSheetAction(); break;
                case "leave": Close(); return;
                default: return;
            }

            RefreshView();
            PublishMenu();
        });
    }

    /// <summary>Puts this screen's own menu back on the table.</summary>
    private void PublishMenu()
    {
        if (_publish is null)
            return;

        var actions = MenuActions();
        var options = new List<ViewerPromptOption>(actions.Count);
        foreach (var (id, label) in actions)
            options.Add(new ViewerPromptOption(id, label));

        _publish(new ViewerPrompt("choice", $"Camp -- {_characterName}", ViewerIds.Character(_characterName), options));
    }

    private void CampCharacterInspectForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!GameRulesProvider.Current.UIOldStyle)
            return;

        switch (e.KeyCode)
        {
            case Keys.R:
                MemorizeSpellAction();
                break;
            case Keys.T:
                TradeAction();
                break;
            case Keys.P:
                PoolGoldAction();
                break;
            case Keys.S:
                CastSpellAction();
                break;
            case Keys.E:
                EquipAction();
                break;
            case Keys.D:
                DropAction();
                break;
            case Keys.I:
                NotImplemented("Identify");
                break;
            case Keys.C:
                ShowCharacterSheetAction();
                break;
            case Keys.U:
                UseItemAction();
                break;
            case Keys.L:
                if (GetCharacter()?.IsPaladin() == true)
                {
                    LayOnHandsAction();
                    break;
                }
                Close();
                return;
            case Keys.Enter:
            case Keys.Escape:
                Close();
                return;
            default:
                return;
        }

        RefreshView();
        PublishMenu();
    }

    private void ApplyUiStyleMode()
    {
        var useOldStyle = GameRulesProvider.Current.UIOldStyle;

        _buttonsPanel.Visible = !useOldStyle;
        _oldStyleFooterLabel.Visible = useOldStyle;

        _detailsBox.BorderStyle = useOldStyle ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
        _detailsBox.Font = useOldStyle
            ? new Font("Consolas", 20f, FontStyle.Bold)
            : new Font("Consolas", 10f, FontStyle.Regular);
        _detailsBox.Height = useOldStyle ? 576 : 560;
    }

    /// <summary>Says something on both surfaces, then puts this screen's menu back up.</summary>
    private void SayOnBoth(string title, string text)
    {
        ViewerMessage.Say(this, title, text, _publish);
        PublishMenu();
    }

    /// <summary>A pump needs a key map; this screen answers through commands only.</summary>
    private static readonly IReadOnlyDictionary<string, Keys> NoKeys = new Dictionary<string, Keys>();

    private Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, Width = 92, Height = 32 };
        button.Click += onClick;
        return button;
    }

    private Character? GetCharacter() => _characterRepository.GetAll().FirstOrDefault(c => string.Equals(c.Name, _characterName, StringComparison.OrdinalIgnoreCase));

    private List<Character> GetPartyCharacters()
    {
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return _partyMembers.Where(roster.ContainsKey).Select(name => roster[name]).ToList();
    }

    private void RefreshView()
    {
        var c = GetCharacter();
        if (c == null)
        {
            _detailsBox.Text = "Character no longer exists.";
            _layOnHandsButton.Visible = false;
            return;
        }

        _layOnHandsButton.Visible = c.IsPaladin();
        _layOnHandsButton.Text = c.LayOnHandsUsedToday ? "L)ay Hands (used)" : "L)ay on Hands";
        _oldStyleFooterLabel.Text = c.IsPaladin()
            ? "R)EAD  T)RADE  P)OOL GOLD  S)PELL  L↵EAVE\nE)QUIP D)ROP I)DENTIFY U)SE C)HARACTER L)AY ON HANDS"
            : "R)EAD  T)RADE  P)OOL GOLD  S)PELL  L↵EAVE\nE)QUIP  D)ROP   I)DENTIFY  U)SE ITEM  C)HARACTER SHEET";

        if (GameRulesProvider.Current.UIOldStyle)
        {
            _detailsBox.Text = BuildOldStyleInspectView(c);
            return;
        }

        var lines = new List<string>
        {
            c.ToString(),
            string.Empty,
            "=== EQUIPPED ITEMS ==="
        };

        foreach (var kv in c.Equipment)
            lines.Add(kv.Value == null ? $" - {kv.Key}: (empty)" : $" - {kv.Key}: {kv.Value.Name}");

        lines.Add(string.Empty);
        lines.Add("=== INVENTORY ===");
        if (c.Inventory.Count == 0)
            lines.Add(" (empty)");
        else
            for (int i = 0; i < c.Inventory.Count; i++)
                lines.Add($"{i + 1}. {c.Inventory[i].Name}");

        lines.Add($"Carry Weight: {c.CurrentCarryWeight}/{c.MaxCarryWeight}");

        lines.Add(string.Empty);
        lines.Add("=== SPELLCASTING ===");

        if (c.Spellcasting == null || c.Spellcasting.Count == 0)
        {
            lines.Add(" (no spellcastings)");
        }
        else
        {
            foreach (var state in c.Spellcasting)
            {
                SyncAutoKnownSpells(c, state);
                var all = _spellRepository.LoadByClass(state.SpellClass);
                var known = all.Where(s => state.KnownSpellIds.Contains(s.Id)).ToList();
                lines.Add($" - {state.SpellClass}: known {known.Count}, prepared {state.PreparedSpells.Sum(ps => ps.Count)}");

                for (int lvl = 0; lvl < state.SlotsPerDay.Count; lvl++)
                {
                    var max = state.SlotsPerDay[lvl];
                    if (max <= 0)
                        continue;
                    var used = lvl < state.SlotsUsed.Count ? state.SlotsUsed[lvl] : 0;
                    lines.Add($"   L{lvl + 1} slots: {Math.Max(0, max - used)}/{max}");
                }

                if (known.Count > 0)
                    lines.Add("   Known: " + string.Join(", ", known.Select(s => $"L{s.Level} {s.Name}")));
            }
        }

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void LayOnHandsAction()
    {
        var paladin = GetCharacter();
        if (paladin == null)
            return;

        if (!paladin.IsPaladin())
        {
            SayOnBoth("Lay on Hands", $"{paladin.Name} is not a paladin.");
            return;
        }

        if (paladin.LayOnHandsUsedToday)
        {
            SayOnBoth("Lay on Hands", $"{paladin.Name} has already used Lay on Hands today.");
            return;
        }

        var targets = GetPartyCharacters()
            .Where(t => !t.HasStatus(CharacterStatus.Dead)
                        && !t.HasStatus(CharacterStatus.Ashes)
                        && !t.HasStatus(CharacterStatus.Lost))
            .ToList();

        if (targets.Count == 0)
        {
            SayOnBoth("Lay on Hands", "No valid target for Lay on Hands.");
            return;
        }

        var targetIdx = PromptChoice("Lay on Hands Target", targets.Select(t => $"{t.Name} ({t.CurrentHitPoints}/{t.MaxHitPoints} HP)").ToList());
        if (!targetIdx.HasValue)
            return;

        var target = targets[targetIdx.Value];
        var healAmount = Math.Max(0, paladin.GetPaladinLevel()) * 2;
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;

        paladin.LayOnHandsUsedToday = true;

        _characterRepository.Save(target);
        if (!string.Equals(target.Name, paladin.Name, StringComparison.OrdinalIgnoreCase))
            _characterRepository.Save(paladin);

        RefreshView();
        SayOnBoth("Lay on Hands", healed > 0
            ? $"{paladin.Name} heals {target.Name} for {healed} HP."
            : $"{target.Name} is already at full health.");
    }

    private static string BuildOldStyleInspectView(Character c)
    {
        var classText = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(cls => cls.ToDisplayString().ToUpperInvariant())) : c.Class.ToDisplayString().ToUpperInvariant();
        var raceText = c.Race.ToDisplayString().ToUpperInvariant();
        var statusText = c.Status == CharacterStatus.None ? "OK" : c.Status.ToString().ToUpperInvariant();
        var levelText = c.Classes.Count > 1
            ? string.Join("/", c.Classes.Select(c.GetClassLevel))
            : c.Level.ToString();

        var sb = new System.Text.StringBuilder();
        var age = Math.Max(14, c.Level + 13);
        static string RowWithRightColumn(string left, string middle, string rightLabel, string rightValue)
            => $"{left,-20}{middle,-26}{rightLabel,-5}{rightValue,4}";

        sb.AppendLine($"{c.Name.ToUpperInvariant(),-8} L {levelText,-3} {classText,-14} {raceText}");
        sb.AppendLine();

        var strDisplay = c.Abilities.Strength == 18 && c.ExceptionalStrengthPercentile.HasValue
            ? $"18/{c.ExceptionalStrengthPercentile?.ToString() ?? "00"}"
            : c.Abilities.Strength.ToString();
        sb.AppendLine(RowWithRightColumn(
            $"STRENGTH     {strDisplay,2}",
            $"GOLD      {c.GoldPieces,6}",
            "LEVEL",
            levelText));
        sb.AppendLine(RowWithRightColumn(
            $"INTELLIGENCE {c.Abilities.Intelligence,2}",
            $"XP        {c.Experience,6}",
            "AGE",
            age.ToString()));
        sb.AppendLine($"WISDOM       {c.Abilities.Wisdom,2}");
        sb.AppendLine(RowWithRightColumn(
            $"DEXTERITY    {c.Abilities.Dexterity,2}",
            $"H.P.   {c.CurrentHitPoints,3}/{c.MaxHitPoints,-3}",
            "A.C.",
            c.ArmorClass.ToString()));
        sb.AppendLine($"CONSTITUTION {c.Abilities.Constitution,2}");
        sb.AppendLine($"CHARISMA     {c.Abilities.Charisma,2}    STATUS {statusText}");
        sb.AppendLine($"CARRY WT     {c.CurrentCarryWeight,3}/{c.MaxCarryWeight,-3}");
        sb.AppendLine();

        if (c.Spellcasting != null && c.Spellcasting.Count > 0)
        {
            foreach (var state in c.Spellcasting)
            {
                var slots = new List<string>();
                for (int i = 0; i < state.SlotsPerDay.Count; i++)
                {
                    var max = state.SlotsPerDay[i];
                    var used = i < state.SlotsUsed.Count ? state.SlotsUsed[i] : 0;
                    slots.Add(Math.Max(0, max - used).ToString());
                }
                sb.AppendLine($"{state.SpellClass.ToDisplayString().ToUpperInvariant(),-12} {string.Join("/", slots)}");                
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void EquipAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var equipped = c.Equipment.Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();

        var equipable = c.Inventory
            .Where(it => it.Type == ItemType.Weapon || it.Type == ItemType.Shield || it.Slot.HasValue)
            .Where(it => it.AllowedClasses.Count == 0 || c.Classes.Any(cls => it.AllowedClasses.Contains(cls)))
            .ToList();

        if (equipable.Count > 0 && equipped.Count > 0)
        {
            var actionIdx = PromptChoice("Equip", new List<string> { "Equip item", "Unequip item" });
            if (!actionIdx.HasValue)
                return;

            if (actionIdx.Value == 1)
            {
                UnequipAction();
                return;
            }
        }
        else if (equipable.Count == 0 && equipped.Count > 0)
        {
            UnequipAction();
            return;
        }

        if (equipable.Count == 0)
        {
            SayOnBoth("Equip", "No equipable items in inventory.");
            return;
        }

        var idx = PromptChoice("Equip Item", equipable.Select(i => $"{i.Name} [{GetDisplaySlot(i)}]").ToList());
        if (!idx.HasValue)
            return;

        var item = equipable[idx.Value];

        EquipmentSlot? targetSlot = null;
        if (item.Type == ItemType.Weapon)
        {
            var slotChoice = PromptChoice("Equip Weapon To", new List<string> { "Main Hand", "Off Hand" });
            if (!slotChoice.HasValue)
                return;

            targetSlot = slotChoice.Value == 0 ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;
        }
        else if (item.Type == ItemType.Shield)
        {
            targetSlot = EquipmentSlot.OffHand;
        }
        else if (item.Slot.HasValue)
        {
            targetSlot = item.Slot.Value;
        }

        if (!targetSlot.HasValue)
            return;

        var originalSlot = item.Slot;
        item.Slot = targetSlot.Value;

        var ok = EquipmentManager.Equip(c, item);

        item.Slot = originalSlot;

        if (ok)
        {
            _characterRepository.Save(c);
            RefreshView();
        }
        else
        {
            SayOnBoth("Equip", $"{c.Name} cannot equip {item.Name}.");
        }
    }

    private static string GetDisplaySlot(Item item)
    {
        if (item.Type == ItemType.Weapon)
            return "MainHand/OffHand";

        if (item.Type == ItemType.Shield)
            return "OffHand";

        return item.Slot?.ToString() ?? "-";
    }

    private void UnequipAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var equipped = c.Equipment.Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();
        if (equipped.Count == 0)
        {
            SayOnBoth("Unequip", "No equipped items.");
            return;
        }

        var idx = PromptChoice(
            "Unequip",
            equipped.Select(slot =>
            {
                var item = c.Equipment[slot];
                return item == null ? slot.ToString() : $"{slot} ({item.Name})";
            }).ToList());
        if (!idx.HasValue)
            return;

        if (EquipmentManager.Unequip(c, equipped[idx.Value]))
        {
            _characterRepository.Save(c);
            RefreshView();
        }
    }

    private void DropAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        if (c.Inventory.Count == 0)
        {
            SayOnBoth("Drop", "Inventory empty.");
            return;
        }

        var idx = PromptChoice("Drop Item", c.Inventory.Select(i => i.Name).ToList());
        if (!idx.HasValue)
            return;

        c.Inventory.RemoveAt(idx.Value);
        _characterRepository.Save(c);
        RefreshView();
    }

    private void PoolGoldAction()
    {
        var members = GetPartyCharacters();
        var receiver = members.FirstOrDefault(m => string.Equals(m.Name, _characterName, StringComparison.OrdinalIgnoreCase));
        if (receiver == null)
            return;

        var pooled = 0;
        foreach (var member in members)
        {
            if (string.Equals(member.Name, receiver.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            pooled += member.GoldPieces;
            member.GoldPieces = 0;
            _characterRepository.Save(member);
        }

        receiver.GoldPieces += pooled;
        _characterRepository.Save(receiver);
        RefreshView();
        SayOnBoth("Pool Gold", $"Pooled {pooled} gp to {receiver.Name}.");
    }

    private void MemorizeSpellAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        if (!CanUseMemorizeAction(c))
        {
            SayOnBoth("Memorize", "This character cannot memorize spells.");
            return;
        }

        var states = c.Spellcasting;
        if (states == null || states.Count == 0)
            return;

        var stateIdx = states.Count == 1 ? 0 : PromptChoice("Spellcasting Type", states.Select(s => s.SpellClass.ToString()).ToList());
        if (!stateIdx.HasValue)
            return;

        var state = states[stateIdx.Value];
        SyncAutoKnownSpells(c, state);

        var classSpells = _spellRepository.LoadByClass(state.SpellClass);
        var knownSpells = classSpells
            .Where(s => state.KnownSpellIds.Contains(s.Id))
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();

        if (knownSpells.Count == 0)
            return;

        var spellIdx = PromptChoice("Memorize Spell", knownSpells.Select(s => $"L{s.Level} {s.Name}").ToList());
        if (!spellIdx.HasValue)
            return;

        var chosen = knownSpells[spellIdx.Value];
        var levelIndex = chosen.Level - 1;
        if (levelIndex < 0 || levelIndex >= state.SlotsPerDay.Count || state.SlotsPerDay[levelIndex] <= 0)
            return;

        var preparedForLevel = state.PreparedSpells
            .Join(classSpells, p => p.SpellId, sp => sp.Id, (p, sp) => new { p.Count, sp.Level })
            .Where(x => x.Level == chosen.Level)
            .Sum(x => x.Count);

        if (preparedForLevel >= state.SlotsPerDay[levelIndex])
            return;

        var prepared = state.PreparedSpells.FirstOrDefault(ps => ps.SpellId == chosen.Id);
        if (prepared == null)
            state.PreparedSpells.Add(new PreparedSpell { SpellId = chosen.Id, Count = 1 });
        else
            prepared.Count += 1;

        _characterRepository.Save(c);
        RefreshView();
    }

    private void CastSpellAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var states = c.Spellcasting;
        if (states == null || states.Count == 0)
            return;

        var stateIdx = states.Count == 1 ? 0 : PromptChoice("Spellcasting Type", states.Select(s => s.SpellClass.ToString()).ToList());
        if (!stateIdx.HasValue)
            return;

        var state = states[stateIdx.Value];
        SyncAutoKnownSpells(c, state);

        var allForClass = _spellRepository.LoadByClass(state.SpellClass)
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
                return knows;
            })
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();

        if (castable.Count == 0)
            return;

        var spellIdx = PromptChoice("Cast Spell", castable.Select(s => $"L{s.Level} {s.Name}").ToList());
        if (!spellIdx.HasValue)
            return;

        var spell = castable[spellIdx.Value];
        var partyMembers = GetPartyCharacters();
        if (partyMembers.Count == 0)
            return;

        var caster = partyMembers.FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase)) ?? c;
        var targets = new List<SpellCastTarget>();

        if (spell.RangeType == SpellRangeType.Self)
        {
            targets.Add(SpellCastTarget.Ally(caster));
        }
        else if (spell.RangeType == SpellRangeType.Ally)
        {
            var targetIdx = PromptChoice("Choose Ally Target", partyMembers.Select(p =>
            {
                var status = FormatStatus(p);
                return $"{p.Name} (HP {p.CurrentHitPoints}/{p.MaxHitPoints}, Status: {status})";
            }).ToList());
            if (!targetIdx.HasValue)
                return;

            targets.Add(SpellCastTarget.Ally(partyMembers[targetIdx.Value]));
        }
        else
        {
            SayOnBoth("Cast Spell", "Enemy-target spells require combat.");
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

        // The icon told success from failure; the events themselves already say which it was, and the table has
        // no icons to show anyway.
        SayOnBoth("Cast Spell", string.Join(Environment.NewLine, result.Events));

        if (result.Success)
        {
            foreach (var member in partyMembers)
                _characterRepository.Save(member);
            _characterRepository.Save(caster);
            RefreshView();
        }
    }
    /*
    public void ShowCharaterSheet(Character character)
    {
        using var sheet = new Adnd.Game.Windows.CharacterForm(character);
        sheet.ShowDialog(this);
        PublishToViewer();
    }
    */
    private void ShowCharacterSheetAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;
        //    using var sheetForm = new ShowCharaterSheet(c);
        using var sheet = new Adnd.Game.Windows.CharacterForm(c);
        sheet.ShowDialog(this);
    }

    private void UseItemAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var partyMembers = GetPartyCharacters();
        if (partyMembers.Count == 0)
            return;

        var user = partyMembers.FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase)) ?? c;

        var usableItems = user.Inventory
            .Select((item, index) => new { item, index, spell = _spellCastingService.FindSpellFromItem(item) })
            .Where(x => x.spell != null)
            .ToList();

        if (usableItems.Count == 0)
        {
            SayOnBoth("Use Item", "No usable magical items.");
            return;
        }

        var itemIdx = PromptChoice("Use Item", usableItems.Select(x => $"{x.item.Name} (casts {x.spell!.Name})").ToList());
        if (!itemIdx.HasValue)
            return;

        var selected = usableItems[itemIdx.Value];
        var spell = selected.spell!;
        var targets = new List<SpellCastTarget>();

        if (spell.RangeType == SpellRangeType.Self)
        {
            targets.Add(SpellCastTarget.Ally(user));
        }
        else if (spell.RangeType == SpellRangeType.Ally)
        {
            var targetIdx = PromptChoice("Choose Ally Target", partyMembers.Select(p =>
            {
                var status = FormatStatus(p);
                return $"{p.Name} (HP {p.CurrentHitPoints}/{p.MaxHitPoints}, Status: {status})";
            }).ToList());

            if (!targetIdx.HasValue)
                return;

            targets.Add(SpellCastTarget.Ally(partyMembers[targetIdx.Value]));
        }

        var result = _spellCastingService.CastFromItem(new SpellCastRequest
        {
            Caster = user,
            SpellId = spell.Id,
            Context = SpellUseContext.Exploration,
            Targets = targets,
            PartyTargets = partyMembers,
            MonsterTargets = new List<Adnd.Core.Combat.Sessions.MonsterInstance>()
        });

        if (!result.Success)
        {
            SayOnBoth("Use Item", string.IsNullOrWhiteSpace(result.Error) ? "Could not use item." : result.Error);
            return;
        }

        if (selected.item.Type is ItemType.Potion or ItemType.Scroll)
        {
            user.Inventory.RemoveAt(selected.index);
            result.Events.Add($"{selected.item.Name} is consumed.");
        }

        foreach (var member in partyMembers)
            _characterRepository.Save(member);
        _characterRepository.Save(user);

        RefreshView();
        SayOnBoth("Use Item", string.Join(Environment.NewLine, result.Events));
    }

    private static string FormatStatus(Character c)
    {
        var statuses = new List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        if (c.HasStatus(CharacterStatus.Blind)) statuses.Add("Blind");
        if (c.HasStatus(CharacterStatus.Diseased)) statuses.Add("Diseased");
        if (c.HasStatus(CharacterStatus.Feeblemind)) statuses.Add("Feeblemind");
        if (c.HasStatus(CharacterStatus.Slowed)) statuses.Add("Slowed");

        return statuses.Count == 0 ? "-" : string.Join(", ", statuses);
    }

    private void SyncAutoKnownSpells(Character c, SpellcastingState state)
    {
        if (!IsAutoMemorizedClass(state.SpellClass))
            return;

        var classSpells = _spellRepository.LoadByClass(state.SpellClass);
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
            _characterRepository.Save(c);
        }
    }

    private static bool IsAutoMemorizedClass(SpellClass spellClass)
    {
        if (spellClass is SpellClass.Cleric or SpellClass.Druid)
            return true;

        return GameRulesProvider.Current.AutoMemorizeArcaneSpellsDaily
               && spellClass is SpellClass.MagicUser
               or SpellClass.Illusionist;
    }

    private static bool CanUseMemorizeAction(Character c)
    {
        return c.Classes.Any(cls => cls == CharacterClass.MagicUser
                                    || cls == CharacterClass.Illusionist
                                    || cls == CharacterClass.Ranger);
    }

    private static void NotImplemented(string actionName)
    {
        ViewerMessage.Say(null, actionName, $"[{actionName} action not yet implemented]", null);
    }

    private int? PromptChoice(string title, List<string> options)
    {
        using var form = new Form();
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(560, 420);
        form.MinimizeBox = false;
        form.MaximizeBox = false;

        var list = new ListBox
        {
            Left = 12,
            Top = 12,
            Width = 536,
            Height = 330,
            Font = new Font("Consolas", 10f)
        };

        foreach (var option in options)
            list.Items.Add(option);

        var ok = new Button { Text = "OK", Left = 392, Top = 354, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 473, Top = 354, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.Add(list);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        // Nine callers come through here -- equip, unequip, drop, trade, which spell, which target -- so this is
        // the one place that has to know about the table, and all nine become answerable from it at once.
        var outcome = ViewerDialog.RunPick(form, this, title, ViewerIds.Character(_characterName), options, _publish);

        if (outcome.Picked.HasValue)
            return outcome.Picked;

        if (outcome.Result != DialogResult.OK)
            return null;

        return list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }

    private void TradeAction()
    {
        var giver = GetCharacter();
        if (giver == null)
            return;

        if (giver.Inventory.Count == 0)
        {
            SayOnBoth("Trade", "No items to trade.");
            return;
        }

        var members = GetPartyCharacters();
        var recipients = members
            .Where(m => !string.Equals(m.Name, giver.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (recipients.Count == 0)
        {
            SayOnBoth("Trade", "No other party member to trade with.");
            return;
        }

        var recipientIdx = PromptChoice("Trade With", recipients.Select(r => $"{r.Name} ({r.Class})").ToList());
        if (!recipientIdx.HasValue)
            return;

        var receiver = recipients[recipientIdx.Value];

        var itemIdx = PromptChoice("Choose Item", giver.Inventory.Select(i => $"{i.Name} (Wt {i.Weight})").ToList());
        if (!itemIdx.HasValue)
            return;

        var item = giver.Inventory[itemIdx.Value];

        if (!receiver.CanCarry(item))
        {
            SayOnBoth("Trade", $"{receiver.Name} cannot carry more weight ({receiver.CurrentCarryWeight}/{receiver.MaxCarryWeight}).");
            return;
        }

        giver.Inventory.Remove(item);
        receiver.Inventory.Add(item);

        _characterRepository.Save(giver);
        _characterRepository.Save(receiver);

        RefreshView();

        SayOnBoth("Trade", $"Traded {item.Name} from {giver.Name} to {receiver.Name}.");
    }
}
