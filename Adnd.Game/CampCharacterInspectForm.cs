using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Data.Characters;
using Adnd.Data.Items;
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
    private readonly ItemRepository _itemRepository = new("Data/Items");
    private readonly SpellCastingService _spellCastingService;

    private readonly TextBox _detailsBox;
    private readonly FlowLayoutPanel _buttonsPanel;
    private readonly Label _oldStyleFooterLabel;
    private readonly Button _layOnHandsButton;
    private readonly Button _monkBodyHealButton;

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
            new ProtectionFromEvilHandler(),
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
            new IceStormHandler(),
            new LightningBoltHandler(),
            new WallOfFireHandler(),
            new WallOfThornsHandler(),
            new FeeblemindHandler(),
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
            new FearHandler(),
            new ParalyzationHandler(),
            new PhantasmalForceHandler()
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
        _buttonsPanel.Controls.Add(MakeButton("I)dentify", (_, _) => IdentifyAction()));
        _buttonsPanel.Controls.Add(MakeButton("S)pell", (_, _) => CastSpellAction()));
        _buttonsPanel.Controls.Add(MakeButton("U)se Item", (_, _) => UseItemAction()));
        _buttonsPanel.Controls.Add(MakeButton("C)haracter Sheet", (_, _) => ShowCharacterSheetAction()));

        _layOnHandsButton = MakeButton("L)ay on Hands", (_, _) => LayOnHandsAction());
        _buttonsPanel.Controls.Add(_layOnHandsButton);
        _monkBodyHealButton = MakeButton("H)eal Body", (_, _) => MonkBodyHealAction());
        _buttonsPanel.Controls.Add(_monkBodyHealButton);
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
        CheckUnreadScrollFadeForCurrentCharacter();
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
        if (c?.IsMonk() == true && c.GetMonkLevel() >= 7)
            actions.Add(("monkBodyHeal", c.MonkBodyHealUsedToday ? "Body Heal (used today)" : "Body Heal"));

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
                case "identify": IdentifyAction(); break;
                case "spell": CastSpellAction(); break;
                case "useItem": UseItemAction(); break;
                case "characterSheet": ShowCharacterSheetAction(); break;
                case "layOnHands": LayOnHandsAction(); break;
                case "monkBodyHeal": MonkBodyHealAction(); break;
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
                IdentifyAction();
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

    private void CheckUnreadScrollFadeForCurrentCharacter()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var messages = new List<string>();
        ApplyUnreadScrollFadeForCharacter(c, messages);
        if (messages.Count > 0)
            SayOnBoth("Scrolls", string.Join(Environment.NewLine, messages));
    }

    private static bool IsArcaneReader(Character c)
    {
        return c.Spellcasting.Any(s => s.SpellClass is SpellClass.MagicUser or SpellClass.Illusionist)
               || c.Classes.Any(cls => cls is CharacterClass.MagicUser or CharacterClass.Illusionist);
    }

    private static bool IsDivineReader(Character c)
    {
        return c.Spellcasting.Any(s => s.SpellClass is SpellClass.Cleric or SpellClass.Druid)
               || c.Classes.Any(cls => cls is CharacterClass.Cleric or CharacterClass.Druid);
    }

    private static bool IsThiefReader(Character c)
    {
        return c.Classes.Any(cls => cls == CharacterClass.Thief)
               || c.Class == CharacterClass.Thief;
    }

    private static bool TryThiefReadMagic(Character c, out double chancePercent, out int roll)
    {
        var thiefLevel = Math.Max(1, c.GetClassLevel(CharacterClass.Thief));
        chancePercent = Math.Clamp(AbilitiesTables.ThiefReadLanguages(thiefLevel, c.Race, c.Abilities.Dexterity), 0d, 100d);
        roll = Random.Shared.Next(1, 101);
        return roll <= chancePercent;
    }

    private static bool CanDetermineScrollContents(Character c, out string? attemptMessage)
    {
        attemptMessage = null;

        // Core-rules approximation: standard spell readers can decode automatically.
        if (IsArcaneReader(c) || IsDivineReader(c))
            return true;

        // Thief special handling: roll against thief read-magic/read-languages skill when trying.
        if (IsThiefReader(c))
        {
            var success = TryThiefReadMagic(c, out var chancePercent, out var roll);
            attemptMessage = success
                ? $"{c.Name} tries thief Read Magic {chancePercent:0.#}% and succeeds (roll {roll})."
                : $"{c.Name} tries thief Read Magic {chancePercent:0.#}% and fails (roll {roll}).";
            return success;
        }

        attemptMessage = "The scroll's magical cipher is unreadable. Read Magic or Comprehend Languages is required to determine contents.";
        return false;
    }

    private static int RollScrollUnreadFadeChancePercent()
    {
        // Core rule: 5% to 30%, or d6 choice. We use 5% steps from a d6.
        var d6 = Random.Shared.Next(1, 7);
        return d6 * 5;
    }

    private void ApplyUnreadScrollFadeForCharacter(Character c, List<string> messages)
    {
        if (c.Inventory.Count == 0)
            return;

        var removed = new List<string>();
        var changed = false;
        for (var i = c.Inventory.Count - 1; i >= 0; i--)
        {
            var item = c.Inventory[i];
            if (item.Type != ItemType.Scroll)
                continue;

            if (item.ScrollContentsKnown)
                continue;

            if (!item.ScrollUnreadFadeChecked)
            {
                item.ScrollUnreadFadeChancePercent = RollScrollUnreadFadeChancePercent();
                item.ScrollUnreadFadeChecked = true;
                changed = true;
            }

            var chance = Math.Clamp(item.ScrollUnreadFadeChancePercent, 0, 100);
            var roll = Random.Shared.Next(1, 101);
            if (roll <= chance)
            {
                removed.Add($"{item.Name} fades unread (roll {roll} <= {chance}%).");
                c.Inventory.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
            _characterRepository.Save(c);

        if (removed.Count > 0)
        {
            messages.AddRange(removed);
        }
    }

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
            _monkBodyHealButton.Visible = false;
            return;
        }

        c.RefreshRingProtectionEffects();
        c.RefreshRingWizardryEffects();

        _layOnHandsButton.Visible = c.IsPaladin();
        _layOnHandsButton.Text = c.LayOnHandsUsedToday ? "L)ay Hands (used)" : "L)ay on Hands";
        _monkBodyHealButton.Visible = c.IsMonk() && c.GetMonkLevel() >= 7;
        _monkBodyHealButton.Text = c.MonkBodyHealUsedToday ? "H)eal Body (used)" : "H)eal Body";
        _oldStyleFooterLabel.Text = c.IsPaladin()
            ? "R)EAD  T)RADE  P)OOL GOLD  S)PELL  L↵EAVE\nE)QUIP D)ROP I)DENTIFY U)SE C)HARACTER L)AY ON HANDS"
            : c.IsMonk() && c.GetMonkLevel() >= 7
                ? "R)EAD  T)RADE  P)OOL GOLD  S)PELL  L↵EAVE\nE)QUIP D)ROP I)DENTIFY U)SE C)HARACTER H)EAL BODY"
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

    private void MonkBodyHealAction()
    {
        var monk = GetCharacter();
        if (monk == null)
            return;

        if (!monk.IsMonk() || monk.GetMonkLevel() < 7)
        {
            SayOnBoth("Body Heal", $"{monk.Name} cannot use Body Heal yet.");
            return;
        }

        if (monk.MonkBodyHealUsedToday)
        {
            SayOnBoth("Body Heal", $"{monk.Name} has already used Body Heal today.");
            return;
        }

        if (monk.HasStatus(CharacterStatus.Dead) || monk.HasStatus(CharacterStatus.Ashes) || monk.HasStatus(CharacterStatus.Lost))
        {
            SayOnBoth("Body Heal", $"{monk.Name} cannot use Body Heal right now.");
            return;
        }

        var healAmount = monk.RollMonkBodyHealAmount();
        var before = monk.CurrentHitPoints;
        monk.CurrentHitPoints = Math.Min(monk.MaxHitPoints, monk.CurrentHitPoints + healAmount);
        var healed = monk.CurrentHitPoints - before;

        monk.MonkBodyHealUsedToday = true;
        _characterRepository.Save(monk);

        RefreshView();
        SayOnBoth("Body Heal", healed > 0
            ? $"{monk.Name} heals for {healed} HP."
            : $"{monk.Name} is already at full health.");
    }

    private static string BuildOldStyleInspectView(Character c)
    {
        var classText = c.GetClassesDisplayText("/").ToUpperInvariant();
        var raceText = c.Race.ToDisplayString().ToUpperInvariant();
        var alignmentText = c.Alignment.ToAbbreviation();
        var statusText = c.Status == CharacterStatus.None ? "OK" : c.Status.ToString().ToUpperInvariant();
        var levelText = c.Classes.Count > 1
            ? string.Join("/", c.Classes.Select(c.GetClassLevel))
            : c.Level.ToString();

        var sb = new System.Text.StringBuilder();
        var ageText = $"{Math.Max(0, c.Age)}y {Math.Max(0, c.AgeDays)}d";
        static string RowWithRightColumn(string left, string middle, string rightLabel, string rightValue)
            => $"{left,-20}{middle,-26}{rightLabel,-5}{rightValue,4}";

        sb.AppendLine($"{c.Name.ToUpperInvariant(),-8} L {levelText,-3} {classText,-14} {raceText} {alignmentText}");
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
            ageText));
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
            var isRangedWeapon = IsRangedWeaponForEquip(item);

            if (isRangedWeapon)
            {
                targetSlot = EquipmentSlot.Range;
            }
            else
            {
                var slotChoice = PromptChoice("Equip Weapon To", new List<string> { "Main Hand", "Off Hand" });
                if (!slotChoice.HasValue)
                    return;

                targetSlot = slotChoice.Value == 0 ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;
            }
        }
        else if (item.Type == ItemType.Misc && item.Quantity > 0 && !string.IsNullOrWhiteSpace(item.AmmoType))
        {
            targetSlot = EquipmentSlot.Ammo;
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
            c.RefreshRingProtectionEffects();
            c.RefreshRingWizardryEffects();
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
        {
            var isRangedWeapon = IsRangedWeaponForEquip(item);

            if (isRangedWeapon)
                return "Range";

            return "MainHand/OffHand";
        }

        if (item.Type == ItemType.Misc && item.Quantity > 0 && !string.IsNullOrWhiteSpace(item.AmmoType))
            return "Ammo";

        if (item.Type == ItemType.Shield)
            return "OffHand";

        return item.Slot?.ToString() ?? "-";
    }

    private static bool IsRangedWeaponForEquip(Item item)
    {
        if (item.Type != ItemType.Weapon)
            return false;

        if (item.Slot == EquipmentSlot.Range)
            return true;

        if (item.RequiresAmmo || !string.IsNullOrWhiteSpace(item.AmmoType))
            return true;

        var name = item.Name ?? string.Empty;
        if (name.Contains("Crossbow", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bow", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sling", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Javelin", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dart", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Throwing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
            c.RefreshRingProtectionEffects();
            c.RefreshRingWizardryEffects();
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

        if (IsAutoMemorizedClass(state.SpellClass))
        {
            ShowKnownSpellsWithMoreInfo(knownSpells);
            return;
        }

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
        if (caster.BreakRingInaudibilityForSpeaking())
            _characterRepository.Save(caster);

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
            .Select((item, index) => new
            {
                item,
                index,
                spell = _spellCastingService.FindSpellFromItem(item),
                grantsRegeneration = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Regeneration"),
                grantsFireResistancePotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Fire Resistance"),
                grantsGiantStrengthPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Giant Strength"),
                grantsHeroismPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Heroism"),
                grantsSuperHeroismPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Super-Heroism"),
                grantsInvulnerabilityPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Invulnerability"),
                grantsLevitationPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Levitate") || string.Equals(item.Name, "Potion of Levitation", StringComparison.OrdinalIgnoreCase),
                grantsSpeedPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Haste") || string.Equals(item.Name, "Potion of Speed", StringComparison.OrdinalIgnoreCase),
                isPotionOfHealing = string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase)
            })
            .Where(x => x.spell != null || x.grantsRegeneration || x.grantsFireResistancePotion || x.grantsGiantStrengthPotion || x.grantsHeroismPotion || x.grantsSuperHeroismPotion || x.grantsInvulnerabilityPotion || x.grantsLevitationPotion || x.grantsSpeedPotion || x.isPotionOfHealing)
            .ToList();

        var hasEquippedRingInvisibility = user.TryGetEquippedRingOfInvisibility(out var equippedRingOfInvisibility)
            && equippedRingOfInvisibility != null;

        if (usableItems.Count == 0 && !hasEquippedRingInvisibility)
        {
            SayOnBoth("Use Item", "No usable magical items.");
            return;
        }

        if (usableItems.Count == 0 && hasEquippedRingInvisibility)
        {
            if (user.HasActiveRingInvisibility)
            {
                user.DeactivateRingInvisibility();
                _characterRepository.Save(user);
                RefreshView();
                SayOnBoth("Use Item", $"{user.Name} deactivates Ring of Invisibility and becomes visible.");
            }
            else
            {
                user.ActivateRingInvisibility();
                _characterRepository.Save(user);
                RefreshView();
                SayOnBoth("Use Item", user.HasActiveRingInvisibilityInaudibility
                    ? $"{user.Name} activates Ring of Invisibility and becomes invisible and inaudible."
                    : $"{user.Name} activates Ring of Invisibility and becomes invisible.");
            }
            return;
        }

        var itemIdx = PromptChoice("Use Item", usableItems.Select(x =>
            x.item.Type == ItemType.Scroll && !x.item.ScrollContentsKnown
                ? $"{x.item.Name} (unread magical scroll)"
                : x.spell != null
                ? $"{x.item.Name} (casts {x.spell!.Name})"
                : x.grantsRegeneration
                    ? $"{x.item.Name} (grants regeneration)"
                    : x.grantsFireResistancePotion
                        ? $"{x.item.Name} (grants fire resistance)"
                        : x.grantsGiantStrengthPotion
                            ? $"{x.item.Name} (grants giant strength)"
                            : x.grantsHeroismPotion
                                ? $"{x.item.Name} (grants heroism)"
                                : x.grantsSuperHeroismPotion
                                    ? $"{x.item.Name} (grants super-heroism)"
                                : x.grantsInvulnerabilityPotion
                                    ? $"{x.item.Name} (grants invulnerability)"
                                : x.grantsLevitationPotion
                                    ? $"{x.item.Name} (grants levitation)"
                                : x.grantsSpeedPotion
                                    ? $"{x.item.Name} (grants speed)"
                                : $"{x.item.Name} (heals 2d4+2)").ToList());
        if (!itemIdx.HasValue)
            return;

        if (hasEquippedRingInvisibility)
        {
            var toggleChoices = new List<string>
            {
                user.HasActiveRingInvisibility
                    ? "Toggle Ring of Invisibility (currently ON)"
                    : "Toggle Ring of Invisibility (currently OFF)"
            };

            var ringChoice = PromptChoice("Ring Power", toggleChoices);
            if (ringChoice.HasValue)
            {
                if (user.HasActiveRingInvisibility)
                {
                    user.DeactivateRingInvisibility();
                    _characterRepository.Save(user);
                    RefreshView();
                    SayOnBoth("Ring of Invisibility", $"{user.Name} deactivates ring invisibility and becomes visible.");
                }
                else
                {
                    user.ActivateRingInvisibility();
                    _characterRepository.Save(user);
                    RefreshView();
                    SayOnBoth("Ring of Invisibility", user.HasActiveRingInvisibilityInaudibility
                        ? $"{user.Name} activates ring invisibility and is now invisible and inaudible."
                        : $"{user.Name} activates ring invisibility and is now invisible.");
                }
                return;
            }
        }

        var selected = usableItems[itemIdx.Value];
        var spell = selected.spell;
        var grantsRegenerationUntilDungeonExit = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Regeneration");
        var grantsFireResistancePotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Fire Resistance");
        var grantsGiantStrengthPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Giant Strength");
        var grantsHeroismPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Heroism");
        var grantsSuperHeroismPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Super-Heroism");
        var grantsInvulnerabilityPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Invulnerability");
        var grantsLevitationPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Levitate") || string.Equals(selected.item.Name, "Potion of Levitation", StringComparison.OrdinalIgnoreCase);
        var grantsSpeedPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(selected.item, "Haste") || string.Equals(selected.item.Name, "Potion of Speed", StringComparison.OrdinalIgnoreCase);
        var isPotionOfHealing = string.Equals(selected.item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase);
        var targets = new List<SpellCastTarget>();

        if (spell != null && spell.RangeType == SpellRangeType.Self)
        {
            targets.Add(SpellCastTarget.Ally(user));
        }
        else if (spell != null && spell.RangeType == SpellRangeType.Ally)
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

        string? scrollRevealEvent = null;
        string? decipherAttemptEvent = null;
        if (selected.item.Type == ItemType.Scroll && !selected.item.ScrollContentsKnown)
        {
            if (!CanDetermineScrollContents(user, out var decipherAttemptMessage))
            {
                SayOnBoth("Use Item", string.IsNullOrWhiteSpace(decipherAttemptMessage)
                    ? "The scroll's magical cipher is unreadable."
                    : decipherAttemptMessage);
                return;
            }

            if (!string.IsNullOrWhiteSpace(decipherAttemptMessage))
                decipherAttemptEvent = decipherAttemptMessage;

            selected.item.ScrollContentsKnown = true;
            selected.item.ScrollUnreadFadeChecked = true;
            selected.item.ScrollUnreadFadeChancePercent = 0;
            scrollRevealEvent = $"{user.Name} deciphers {selected.item.Name}. Read Magic is no longer required for later invocation.";
        }

        var result = spell != null
            ? _spellCastingService.CastFromItem(new SpellCastRequest
            {
                Caster = user,
                SpellId = spell.Id,
                Context = SpellUseContext.Exploration,
                Targets = targets,
                PartyTargets = partyMembers,
                MonsterTargets = new List<Adnd.Core.Combat.Sessions.MonsterInstance>(),
                IsScrollSpell = selected.item.Type == ItemType.Scroll,
                SourceItemName = selected.item.Name
            })
            : new Adnd.Core.Spells.Casting.SpellCastResult { Success = true };

        if (!string.IsNullOrWhiteSpace(decipherAttemptEvent))
            result.Events.Insert(0, decipherAttemptEvent);

        if (!string.IsNullOrWhiteSpace(scrollRevealEvent))
            result.Events.Insert(0, scrollRevealEvent);

        if (selected.item.Type == ItemType.Scroll)
            user.Inventory.RemoveAt(selected.index);

        if (spell != null && !result.Success)
        {
            var failureText = result.Events != null && result.Events.Count > 0
                ? string.Join(Environment.NewLine, result.Events)
                : (string.IsNullOrWhiteSpace(result.Error) ? "Could not use item." : result.Error);
            SayOnBoth("Use Item", failureText);
            foreach (var member in partyMembers)
                _characterRepository.Save(member);
            _characterRepository.Save(user);
            return;
        }

        if (grantsGiantStrengthPotion && !user.IsFighterClassed())
        {
            SayOnBoth("Use Item", "Potion of Giant Strength can only be used by fighters.");
            return;
        }

        if (grantsHeroismPotion && !user.IsFighterClassed())
        {
            SayOnBoth("Use Item", "Potion of Heroism can only be used by fighters.");
            return;
        }

        if (grantsSuperHeroismPotion && !user.IsFighterClassed())
        {
            SayOnBoth("Use Item", "Potion of Super-Heroism can only be used by fighters.");
            return;
        }

        if (grantsInvulnerabilityPotion && !user.IsFighterClassed())
        {
            SayOnBoth("Use Item", "Potion of Invulnerability can only be used by fighters.");
            return;
        }

        if (spell == null && !grantsRegenerationUntilDungeonExit && !grantsFireResistancePotion && !grantsGiantStrengthPotion && !grantsHeroismPotion && !grantsSuperHeroismPotion && !grantsInvulnerabilityPotion && !grantsLevitationPotion && !grantsSpeedPotion && !isPotionOfHealing)
        {
            SayOnBoth("Use Item", $"{selected.item.Name} has no usable effect.");
            return;
        }

        if (selected.item.Type == ItemType.Potion)
        {
            user.Inventory.RemoveAt(selected.index);
            result.Events.Add($"{selected.item.Name} is consumed.");
        }

        if (grantsRegenerationUntilDungeonExit
            && !user.Inventory.Any(item => Adnd.Core.Items.ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (Potion)")))
        {
            user.Inventory.Add(new Item
            {
                Name = "Regeneration (Potion Effect)",
                Type = ItemType.MagicItem,
                IsShopBuyable = false,
                SpecialAbilities = new List<string> { "Regeneration (Potion)" }
            });
            result.Events.Add($"{user.Name} begins regenerating until leaving the dungeon.");
        }

        if (grantsFireResistancePotion)
        {
            user.SetPotionFireResistanceFullDose();
            result.Events.Add($"{user.Name} drinks Potion of Fire Resistance (full dose): normal fire immunity, +4 saves vs fire, -2 per fire die for 10 rounds.");
        }

        if (grantsGiantStrengthPotion)
        {
            var roll = Random.Shared.Next(1, 21);
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

            result.Events.Add($"{user.Name} drinks Potion of Giant Strength (roll {roll}): {profile.Type} strength until dungeon exit (+{profile.Carry} carry, +{profile.Damage} damage). Rock hurling stored: range {profile.RockRange}\" damage {profile.RockDamage}, bend bars/lift gates {profile.BendBars}%.");
        }

        if (isPotionOfHealing)
        {
            var heal = Random.Shared.Next(1, 5) + Random.Shared.Next(1, 5) + 2;
            var before = user.CurrentHitPoints;
            user.CurrentHitPoints = Math.Min(user.MaxHitPoints, user.CurrentHitPoints + heal);
            var actual = Math.Max(0, user.CurrentHitPoints - before);
            result.Events.Add(actual > 0
                ? $"{user.Name} drinks Potion of Healing and recovers {actual} HP (rolled {heal} on 2d4+2)."
                : $"{user.Name} drinks Potion of Healing (rolled {heal} on 2d4+2), but is already at full health.");
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
                result.Events.Add($"{user.Name} drinks Potion of Heroism, but gains no extra life energy at level {level}.");
            }
            else
            {
                var rolled = 0;
                for (var i = 0; i < profile.Dice; i++)
                    rolled += Random.Shared.Next(1, 11);

                var bonusHp = rolled + profile.Bonus;
                user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                result.Events.Add($"{user.Name} drinks Potion of Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit.");
            }
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
                result.Events.Add($"{user.Name} drinks Potion of Super-Heroism, but gains no extra life energy at level {level}.");
            }
            else
            {
                var rolled = 0;
                for (var i = 0; i < profile.Dice; i++)
                    rolled += Random.Shared.Next(1, 11);

                var bonusHp = rolled + profile.Bonus;
                user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                result.Events.Add($"{user.Name} drinks Potion of Super-Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit.");
            }
        }

        if (grantsInvulnerabilityPotion)
        {
            var rounds = Random.Shared.Next(5, 21);
            user.SetPotionInvulnerability(rounds);
            result.Events.Add($"{user.Name} drinks Potion of Invulnerability: +2 AC and +2 saves, and immunity to non-magical attacks from creatures with no magical properties or with fewer than 4 Hit Dice, for {rounds} rounds.");
        }

        if (grantsLevitationPotion)
        {
            user.SetPotionLevitation();
            result.Events.Add($"{user.Name} drinks Potion of Levitation and can levitate like the Levitate spell. Carry capacity becomes 6,000 gp equivalent until leaving the dungeon.");
        }

        if (grantsSpeedPotion)
        {
            user.Age = Math.Max(0, user.Age + 1);
            result.Events.Add($"{user.Name} drinks Potion of Speed: movement and combat capability are doubled for 5-20 combat rounds; ages 1 year permanently.");
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
                                    || cls == CharacterClass.Cleric
                                    || cls == CharacterClass.Druid
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

    private void ShowKnownSpellsWithMoreInfo(List<Spell> knownSpells)
    {
        using var form = new Form();
        form.Text = "Known Spells";
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

        foreach (var spell in knownSpells)
            list.Items.Add($"L{spell.Level} {spell.Name}");

        var moreInfo = new Button { Text = "More info", Left = 392, Top = 354, Width = 75 };
        var close = new Button { Text = "Close", Left = 473, Top = 354, Width = 75, DialogResult = DialogResult.Cancel };

        moreInfo.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= knownSpells.Count)
                return;

            var spell = knownSpells[list.SelectedIndex];
            var details = new List<string>
            {
                $"Name: {spell.Name}",
                $"Class: {spell.SpellClass}",
                $"Level: {spell.Level}",
                $"Cast context: {spell.CastContext}",
                $"Range type: {spell.RangeType}",
                $"Targeting: {spell.Targeting}",
                $"Target scope: {spell.TargetingScope}",
                $"Effect type: {spell.EffectType}",
                $"Description: {spell.Description}"
            };

            if (!string.IsNullOrWhiteSpace(spell.EffectDescription))
                details.Add($"Effect: {spell.EffectDescription}");

            ViewerMessage.Show(form, $"Spell Info - {spell.Name}", string.Join(Environment.NewLine, details));
        };

        form.Controls.Add(list);
        form.Controls.Add(moreInfo);
        form.Controls.Add(close);
        form.CancelButton = close;

        form.ShowDialog(this);
    }

    private void IdentifyAction()
    {
        var c = GetCharacter();
        if (c == null)
            return;

        var entries = new List<(string Label, Item Item)>();

        foreach (var kv in c.Equipment)
        {
            if (kv.Value != null)
                entries.Add(($"Equipped [{kv.Key}] {kv.Value.Name}", kv.Value));
        }

        foreach (var item in c.Inventory)
            entries.Add(($"Inventory {item.Name}", item));

        if (entries.Count == 0)
        {
            SayOnBoth("Identify", "No items to identify.");
            return;
        }

        var allItems = _itemRepository.LoadAll().ToList();

        using var form = new Form();
        form.Text = "Identify";
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

        foreach (var entry in entries)
            list.Items.Add(entry.Label);

        var moreInfo = new Button { Text = "More info", Left = 392, Top = 354, Width = 75 };
        var cancel = new Button { Text = "Cancel", Left = 473, Top = 354, Width = 75, DialogResult = DialogResult.Cancel };

        moreInfo.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= entries.Count)
                return;

            var selected = entries[list.SelectedIndex].Item;
            if (selected.Type == ItemType.Scroll
                && !selected.ScrollContentsKnown
                && !CanDetermineScrollContents(c, out var decipherAttemptMessage))
            {
                ViewerMessage.Show(form, "Item Info", string.IsNullOrWhiteSpace(decipherAttemptMessage)
                    ? "Magical cipher conceals this scroll's contents."
                    : decipherAttemptMessage);
                return;
            }

            if (selected.Type == ItemType.Scroll && !selected.ScrollContentsKnown)
            {
                selected.ScrollContentsKnown = true;
                selected.ScrollUnreadFadeChecked = true;
                selected.ScrollUnreadFadeChancePercent = 0;
                _characterRepository.Save(c);
            }

            var fromJson = allItems.FirstOrDefault(i => string.Equals(i.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
            var item = fromJson ?? selected;
            var specialAbilities = fromJson?.SpecialAbilities ?? item.SpecialAbilities;

            var details = new List<string>
            {
                $"Name: {item.Name}",
                $"Type: {item.Type}",
                $"Slot: {(item.Slot?.ToString() ?? "None")}",
                $"Cost: {item.Cost}",
                $"Weight: {item.Weight}",
                $"Status: {item.Status}",
                $"Shop buyable: {item.IsShopBuyable}",
                $"Armor class bonus: {item.ArmorClassBonus}",
                $"To hit bonus: {item.ToHitBonus}",
                $"Magic bonus: {item.MagicBonus}",
                $"Damage: {item.Damage}",
                $"Damage vs large: {item.DamageVsLarge}",
                $"Damage type: {(item.Type == ItemType.Weapon ? (string.IsNullOrWhiteSpace(item.DamageType) ? "Unknown" : item.DamageType) : "N/A")}",
                $"Special abilities: {(specialAbilities.Count > 0 ? string.Join(", ", specialAbilities) : "None")}",
                $"Description: {item.Description}",
                $"Source: {item.Source}",
                $"Version: {item.Version}"
            };

            if (string.Equals(item.Name, "Ring of Wizardry", StringComparison.OrdinalIgnoreCase))
            {
                var wizardryProfile = DescribeRingWizardryProfile(specialAbilities);
                details.Add($"Ring wizardry profile: {wizardryProfile}");
            }

            if (item.AllowedClasses.Count > 0)
                details.Add("Allowed classes: " + string.Join(", ", item.AllowedClasses));

            ViewerMessage.Show(form, $"Item Info - {item.Name}", string.Join(Environment.NewLine, details));
        };

        form.Controls.Add(list);
        form.Controls.Add(moreInfo);
        form.Controls.Add(cancel);
        form.CancelButton = cancel;

        form.ShowDialog(this);
    }

    private static string DescribeRingWizardryProfile(List<string> specialAbilities)
    {
        var profileEntry = specialAbilities
            .FirstOrDefault(a => a.StartsWith("RingWizardryProfile:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(profileEntry))
            return "Unrevealed (wear/equip to determine).";

        var idx = profileEntry.IndexOf(':');
        var key = idx >= 0 && idx < profileEntry.Length - 1
            ? profileEntry[(idx + 1)..].Trim().ToUpperInvariant()
            : string.Empty;

        return key switch
        {
            "W1" => "Doubles 1st-level spells/day.",
            "W2" => "Doubles 2nd-level spells/day.",
            "W3" => "Doubles 3rd-level spells/day.",
            "W12" => "Doubles 1st- and 2nd-level spells/day.",
            "W4" => "Doubles 4th-level spells/day.",
            "W5" => "Doubles 5th-level spells/day.",
            "W123" => "Doubles 1st- through 3rd-level spells/day.",
            "W45" => "Doubles 4th- and 5th-level spells/day.",
            _ => $"Unknown profile ({key})."
        };
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
