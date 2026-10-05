using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Diagnostics;
using Adnd.Core.Items;
using Adnd.Data.Spells;

namespace Adnd.Game.Windows
{
    public class CharacterForm : Form
    {
        private readonly Character _character;
        private readonly List<string> _knownSpellLines;
        private readonly CharacterSavingThrowService _savingThrowService = new();
        private const int MaxSpellsOnSheet = 17;
        private readonly Font _handFont = new Font("Bradley Hand ITC", 18, FontStyle.Regular);
        private readonly Font _handFontSmall14 = new Font("Bradley Hand ITC", 14, FontStyle.Regular);
        private readonly Font _handFontSmall12 = new Font("Bradley Hand ITC", 12, FontStyle.Regular);
        private readonly Font _handFontSmall10 = new Font("Bradley Hand ITC", 10, FontStyle.Regular);

        private readonly Font _handFontSmall8 = new Font("Bradley Hand ITC", 8, FontStyle.Regular);

        private readonly Brush _ink = Brushes.Black;
        private readonly Image _sheetBackground;

        /*
        public CharacterForm(Character character)
        {
            _character = character;
            _knownSpellLines = BuildKnownSpellLines(character);

            //  _sheetBackground = Image.FromFile(
            //       @"C:\Users\rober\source\repos\RetroDungeon\Adnd.Game\Assets\ScenPictures\character_sheet.png");
            string relativePath = Path.GetFullPath(
                Path.Combine("..", "..", "..", "Assets", "ScenPictures", "character_sheet.png")
            );

            _sheetBackground = Image.FromFile(relativePath);

            this.DoubleBuffered = true;
            this.ClientSize = new Size(_sheetBackground.Width, _sheetBackground.Height);
            this.Text = $"{_character.Name} – Character Sheet";
            this.BackgroundImage = _sheetBackground;
            this.BackgroundImageLayout = ImageLayout.None;
        }
        */
        public CharacterForm(Character character)
        {
            _character = character;
            _knownSpellLines = BuildKnownSpellLines(character);

            _sheetBackground = Image.FromFile(
                @"C:\Users\rober\source\repos\RetroDungeon\Adnd.Game\Assets\ScenPictures\character_sheet.png");

            // Window setup
            this.DoubleBuffered = true;
            this.ClientSize = new Size(_sheetBackground.Width, _sheetBackground.Height);
            this.Text = $"{_character.Name} – Character Sheet";
            this.BackgroundImage = _sheetBackground;
            this.BackgroundImageLayout = ImageLayout.None;

            // --- KEY HANDLING (ESC / ENTER CLOSES FORM) ---
            KeyPreview = true;   // Important so the form gets key events first

            this.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };

            Shown += (_, _) =>
            {
                ShowMoreInfoDialog();
                if (_knownSpellLines.Count > MaxSpellsOnSheet)
                    ShowAllSpellsDialog();
            };
        }

        private void ShowMoreInfoDialog()
        {
            var saveVsParalyzation = _savingThrowService.GetSaveTarget(_character, SaveThrowType.ParalyzationPoisonDeath);
            var saveVsPetrification = _savingThrowService.GetSaveTarget(_character, SaveThrowType.PetrificationPolymorph);
            var saveVsRodStaffWand = _savingThrowService.GetSaveTarget(_character, SaveThrowType.RodStaffWand);
            var saveVsBreath = _savingThrowService.GetSaveTarget(_character, SaveThrowType.BreathWeapon);
            var saveVsSpell = _savingThrowService.GetSaveTarget(_character, SaveThrowType.Spell);
            var thac0FromTable = _character.Thac0;
            var thac0StrengthModifier = _character.Thac0StrengthModifier;
            var thac0ItemModifier = _character.Thac0ItemModifier;
            var hasDualWieldWeapon = _character.HasDualWieldOffHandWeapon;
            var dualWieldPrimaryPenalty = _character.DualWieldPrimaryThac0Penalty;
            var dualWieldSecondaryPenalty = _character.DualWieldSecondaryThac0Penalty;
            var damageStrengthModifier = _character.DamageStrengthModifier;
            var damageMainItemModifier = _character.MainHandItemDamageModifier;
            var damageOffHandItemModifier = _character.OffHandItemDamageModifier;
            var weaponBaseDamage = _character.Damage;
            var damageTotal = _character.DamageTotalDisplay;
            var weaponProficiencies = _character.GetWeaponProficienciesDisplay();
            var knownLanguages = _character.GetKnownLanguagesDisplay();
            var mainWeaponName = _character.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainWeapon)
                                 && mainWeapon != null
                                 && mainWeapon.Type == ItemType.Weapon
                ? mainWeapon.Name
                : "Unarmed";
            var offHandWeaponName = _character.Equipment.TryGetValue(EquipmentSlot.OffHand, out var offHandWeapon)
                                    && offHandWeapon != null
                                    && offHandWeapon.Type == ItemType.Weapon
                                    && !ReferenceEquals(mainWeapon, offHandWeapon)
                ? offHandWeapon.Name
                : string.Empty;
            var equippedWeaponDisplay = string.IsNullOrWhiteSpace(offHandWeaponName)
                ? mainWeaponName
                : $"{mainWeaponName} / {offHandWeaponName}";

            static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

            var thac0StrengthApplied = -thac0StrengthModifier;
            var thac0ItemApplied = -thac0ItemModifier;
            var thac0AfterStrengthAndItems = thac0FromTable + thac0StrengthApplied + thac0ItemApplied;

            var lines = new List<string>
            {
                $"Alignment: {_character.Alignment.ToDisplayString()}",
                $"Gender: {_character.Gender}",
                string.Empty,
                "Saving Throws:",
                $"- Paralyzation/Poison/Death: {saveVsParalyzation}",
                $"- Petrification/Polymorph: {saveVsPetrification}",
                $"- Rod/Staff/Wand: {saveVsRodStaffWand}",
                $"- Breath Weapon: {saveVsBreath}",
                $"- Spell: {saveVsSpell}",
                string.Empty,
                $"THAC0 (table): {thac0FromTable}",
                $"THAC0 modifiers applied: Strength {FormatSigned(thac0StrengthApplied)}, Items {FormatSigned(thac0ItemApplied)}",
                $"THAC0 calculation: {thac0FromTable} {FormatSigned(thac0StrengthApplied)} {FormatSigned(thac0ItemApplied)} = {thac0AfterStrengthAndItems}"
            };

            if (_character.IsDualClassed && _character.DualClass.HasValue && _character.DualClassOriginalClass.HasValue)
            {
                var currentClass = _character.DualClass.Value;
                var currentLevel = Math.Max(1, _character.GetClassLevel(currentClass));
                var previousClass = _character.DualClassOriginalClass.Value;
                var previousLevel = Math.Max(1, _character.DualClassOriginalLevel);

                lines.Insert(2, $"Current class: {currentClass.ToDisplayString()} L{currentLevel}");
                lines.Insert(3, $"Previous class: {previousClass.ToDisplayString()} L{previousLevel}");
                lines.Insert(4, string.Empty);
            }

            if (hasDualWieldWeapon)
            {
                var mainHandFinalThac0 = thac0AfterStrengthAndItems + dualWieldPrimaryPenalty;
                var offHandFinalThac0 = thac0AfterStrengthAndItems + dualWieldSecondaryPenalty;
                lines.Add($"Two-weapon THAC0 penalty: primary +{dualWieldPrimaryPenalty}, secondary +{dualWieldSecondaryPenalty} (DEX {_character.Abilities.Dexterity}).");
                lines.Add($"Final THAC0: primary {thac0AfterStrengthAndItems}+{dualWieldPrimaryPenalty}={mainHandFinalThac0}, secondary {thac0AfterStrengthAndItems}+{dualWieldSecondaryPenalty}={offHandFinalThac0}");
            }
            else
            {
                lines.Add($"Final THAC0: {thac0AfterStrengthAndItems}");
            }

            if (_character.HasMonkClass())
            {
                lines.Add("Monk rule (PHB p.32): no Strength bonus to THAC0 or damage; item bonuses still apply.");
            }

            lines.Add(string.Empty);
            lines.Add($"Weapon equipped: {equippedWeaponDisplay}");
            lines.Add($"Weapon proficiencies: {weaponProficiencies}");
            lines.Add($"Languages: {knownLanguages}");

            if (_character.IsBard())
            {
                var bardLevel = _character.GetBardLevel();
                var bardProgress = BardRules.GetProgressForLevel(bardLevel);
                lines.Add($"Bard college: {bardProgress.College}");
                lines.Add($"Bard charm chance: {bardProgress.CharmPercentage}%");
                lines.Add($"Legend/Lore & item knowledge chance: {bardProgress.LegendLoreItemKnowledgePercentage}%");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "118BardTabeII",
                    $"{_character.Name} bard profile: L{bardLevel}, college {bardProgress.College}, charm {bardProgress.CharmPercentage}%, legend/lore {bardProgress.LegendLoreItemKnowledgePercentage}%, additional languages known {bardProgress.AdditionalLanguagesKnown}.");
            }

            lines.Add($"Weapon base damage: {weaponBaseDamage}");
            lines.Add($"Damage modifiers: Strength {FormatSigned(damageStrengthModifier)}");
            lines.Add($"Damage total: {damageTotal}");

            RuleApplicationInfo.PublishLinked(
                "DMG",
                "79Savethrow",
                $"{_character.Name} saving throws: Paralyzation/Poison/Death {saveVsParalyzation}, Petrification/Polymorph {saveVsPetrification}, Rod/Staff/Wand {saveVsRodStaffWand}, Breath Weapon {saveVsBreath}, Spell {saveVsSpell}.");

            RuleApplicationInfo.PublishLinked(
                "DMG",
                "74ToHitTables",
                $"{_character.Name} THAC0 from table: {thac0FromTable}. Applied modifiers: Strength {FormatSigned(thac0StrengthApplied)}, Items {FormatSigned(thac0ItemApplied)}. Calculation: {thac0FromTable} {FormatSigned(thac0StrengthApplied)} {FormatSigned(thac0ItemApplied)} = {thac0AfterStrengthAndItems}.");

            if (_character.HasMonkClass())
            {
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "32MonkStrBonus",
                    $"Monk rule applied for {_character.Name}: no Strength bonus to THAC0 or damage; item modifiers still apply.");
            }

            RuleApplicationInfo.Publish(
                $"{_character.Name} damage with equipped weapon(s): weapon {equippedWeaponDisplay}; base damage {weaponBaseDamage}; modifiers Strength {FormatSigned(damageStrengthModifier)}, Item(main) {FormatSigned(damageMainItemModifier)}, Item(offhand) {FormatSigned(damageOffHandItemModifier)}; total {damageTotal}.");

            RuleApplicationInfo.PublishLinked(
                "PHB",
                "37WeaponProficiency",
                $"{_character.Name} weapon proficiencies on character sheet: {weaponProficiencies}.");

            if (hasDualWieldWeapon)
            {
                var mainHandFinalThac0 = thac0AfterStrengthAndItems + dualWieldPrimaryPenalty;
                var offHandFinalThac0 = thac0AfterStrengthAndItems + dualWieldSecondaryPenalty;
                RuleApplicationInfo.PublishLinked(
                    "DMG",
                    "70TwoWeapons",
                    $"{_character.Name} dual-wield THAC0 penalties (DEX {_character.Abilities.Dexterity}): primary +{dualWieldPrimaryPenalty}, secondary +{dualWieldSecondaryPenalty}. Final THAC0 primary {mainHandFinalThac0}, secondary {offHandFinalThac0}.");
            }

            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, lines),
                "More Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        public static (string first, string second, string third) SplitCommaString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return ("", "", "");

            // Dela upp på kommatecken
            var parts = input.Split(',')
                             .Select(p => p.Trim())
                             .ToList();

            // Första 4
            string first = string.Join(", ", parts.Take(4));

            // Element 5–8
            string second = string.Join(", ", parts.Skip(4).Take(4));

            // Resten
            string third = string.Join(", ", parts.Skip(8));

            return (first, second, third);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            string first4WeaponProficiency, second4WeaponProficiency, third4WeaponProficiency;
            string first4KnownLanguages, second4KnownLanguages, third4KnownLanguages;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            DrawInBox(g, _character.Name, new Rectangle(45, 80, 198, 40));
            DrawInBox(g, _character.Race.ToString(), new Rectangle(340, 80, 98, 40));
            // DrawInBox(g, _character.Class.ToString(), new Rectangle(500, 80, 86, 40));
//            DrawInBox(g, _character.Class.GetClassName().ToString(), new Rectangle(500, 80, 86, 40), _handFontSmall12);
            DrawInBox(g, _character.GetClassDisplayName(_character.Class), new Rectangle(500, 80, 86, 40), false, StringAlignment.Center, _handFontSmall12);
            //c.Classes.Select(cc => cc.ToDisplayString())
            DrawInBox(g, _character.Level.ToString(), new Rectangle(640, 80, 46, 40));  
            DrawInBox(g, _character.CurrentHitPoints.ToString(), new Rectangle(710, 80, 95, 40));
            DrawInBox(g, _character.ArmorClass.ToString(), new Rectangle(844, 80, 45, 40));

            (first4KnownLanguages, second4KnownLanguages, third4KnownLanguages) = SplitCommaString(_character.GetKnownLanguagesDisplay());
            DrawInBox(g, first4KnownLanguages, new Rectangle(45, 180, 400, 40), false, StringAlignment.Near, _handFontSmall10);
            DrawInBox(g, second4KnownLanguages, new Rectangle(45, 195, 400, 40), false, StringAlignment.Near, _handFontSmall10);
            DrawInBox(g, third4KnownLanguages, new Rectangle(45, 210, 400, 40), false, StringAlignment.Near, _handFontSmall10);

            (first4WeaponProficiency, second4WeaponProficiency, third4WeaponProficiency) = SplitCommaString(_character.GetWeaponProficienciesDisplay());
            DrawInBox(g, first4WeaponProficiency, new Rectangle(500, 180, 640, 36), false, StringAlignment.Near, _handFontSmall10);
            DrawInBox(g, second4WeaponProficiency, new Rectangle(500, 195, 640, 36), false, StringAlignment.Near, _handFontSmall10);
            DrawInBox(g, third4WeaponProficiency, new Rectangle(500, 210, 640, 36), false, StringAlignment.Near, _handFontSmall10);

            // Ability circles: write only the value inside each circle, not labels.
            if (_character.ExceptionalStrengthPercentile == null)
            {
                DrawInCircle(g, _character.Abilities.Strength.ToString(), new Rectangle(40, 300, 66, 66));
            }
            else
            {
                DrawInCircle(g, _character.Abilities.Strength.ToString() + "/" + _character.ExceptionalStrengthPercentile, new Rectangle(25, 300, 116, 66));
            }
            DrawInCircle(g, _character.Abilities.Intelligence.ToString(), new Rectangle(200, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Wisdom.ToString(), new Rectangle(350, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Constitution.ToString(), new Rectangle(507, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Dexterity.ToString(), new Rectangle(660, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Charisma.ToString(), new Rectangle(815, 300, 66, 66));

            //STRENGTH MODIFIER
            DrawInBox(g, AbilitiesTables.StrengthTHModifier(_character.Abilities.Strength, _character.ExceptionalStrengthPercentile).ToString(), new Rectangle(40, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthDamageModifier(_character.Abilities.Strength, _character.ExceptionalStrengthPercentile).ToString(), new Rectangle(40, 427, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthWeightAllowanceModifier(_character.Abilities.Strength).ToString(), new Rectangle(35, 465, 111, 66));
            DrawInBox(g, AbilitiesTables.StrengthOpenDoors(_character.Abilities.Strength, _character.ExceptionalStrengthPercentile).ToString(), new Rectangle(40, 507, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthBendBars(_character.Abilities.Strength).ToString() + "%", new Rectangle(30, 542, 95, 66));

            //INTELLIGENCE MODIFIER
            if ((_character.Class != CharacterClass.MagicUser) && (_character.Class != CharacterClass.Illusionist) && (_character.Class != CharacterClass.Ranger))
            {
                DrawInBox(g, "NA", new Rectangle(190, 395, 95, 66));
            }
            else
            {
                DrawInBox(g, AbilitiesTables.IntelligenceChanceToLearn(_character.Abilities.Intelligence).ToString() + "%", new Rectangle(190, 395, 95, 66));
                DrawInBox(g, AbilitiesTables.IntelligenceMinimumSpells(_character.Abilities.Intelligence).ToString(), new Rectangle(190, 442, 95, 66));
                DrawInBox(g, AbilitiesTables.IntelligenceMaximumSpells(_character.Abilities.Intelligence).ToString(), new Rectangle(190, 490, 95, 66));
            }

            //WISDOM MODIFIER
            if ((_character.Class != CharacterClass.Cleric) && (_character.Class != CharacterClass.Druid) && (_character.Class != CharacterClass.Paladin))
            {
                DrawInBox(g, "NA", new Rectangle(350, 390, 95, 66));
            }
            else 
            {
                DrawInBox(g, AbilitiesTables.WisdomBonus(_character.Abilities.Wisdom).ToString(), new Rectangle(345, 390, 95, 66), false, StringAlignment.Center, _handFontSmall12);
                DrawInBox(g, AbilitiesTables.WisdomSpellFailure(_character.Abilities.Wisdom).ToString(), new Rectangle(350, 427, 95, 66));
                DrawInBox(g, AbilitiesTables.WisdomMagicAttackAdjustment(_character.Abilities.Wisdom).ToString(), new Rectangle(350, 460, 95, 66));

            }

            //CONSTITUTION MODIFIER
            DrawInBox(g, AbilitiesTables.ConstitutionHpBonus(_character.Abilities.Constitution,true).ToString(), new Rectangle(507, 390, 95, 66));//todo: check if fighter or not
            DrawInBox(g, AbilitiesTables.ConstitutionResurrectionSurvival(_character.Abilities.Constitution).ToString(), new Rectangle(507, 432, 95, 66));
            DrawInBox(g, AbilitiesTables.ConstitutionSystemShock(_character.Abilities.Constitution).ToString(), new Rectangle(507, 470, 95, 66));

            //DEXTERITY MODIFIER
            DrawInBox(g, AbilitiesTables.DexterityAttackingAdjustment(_character.Abilities.Dexterity).ToString(), new Rectangle(660, 390, 95, 66));
            //DEXTERITY ARMOR CLASS ADJUSTMENT
            DrawInBox(g, AbilitiesTables.DexterityACModifier(_character.Abilities.Dexterity).ToString() , new Rectangle(670, 452, 95, 40));

            //DEXTERITY OPEN LOCKS etc. (all dexterity related Theif skills)
            if (_character.GetClassLevel(CharacterClass.Thief) <= 0 && !_character.Classes.Contains(CharacterClass.Bard))
            {
                DrawInBox(g, "NA", new Rectangle(660, 537, 95, 66));
            }
            else
            {
                var thiefLevel = _character.GetClassLevel(CharacterClass.Thief) > 0
                    ? _character.GetClassLevel(CharacterClass.Thief)
                    : Math.Max(1, _character.DualClassOriginalLevel > 0 ? _character.DualClassOriginalLevel : _character.GetClassLevel(CharacterClass.Bard));
                DrawInBox(g, AbilitiesTables.ThiefPickPockets(thiefLevel, _character.Race, _character.Abilities.Dexterity).ToString("0.#") + "%", new Rectangle(660, 537, 95, 66));
                DrawInBox(g, AbilitiesTables.ThiefOpenLocks(thiefLevel, _character.Race, _character.Abilities.Dexterity).ToString("0.#") + "%", new Rectangle(660, 578, 95, 66));
                DrawInBox(g, AbilitiesTables.ThiefFindRemoveTraps(thiefLevel, _character.Race, _character.Abilities.Dexterity).ToString("0.#") + "%", new Rectangle(560, 618, 95, 66));
                DrawInBox(g, AbilitiesTables.ThiefMoveSilently(thiefLevel, _character.Race, _character.Abilities.Dexterity).ToString("0.#") + "%", new Rectangle(660, 618, 95, 66));
                DrawInBox(g, AbilitiesTables.ThiefHideInShadows(thiefLevel, _character.Race, _character.Abilities.Dexterity).ToString("0.#") + "%", new Rectangle(800, 618, 95, 66));
            }

            //CHARISMA MODIFIER
            DrawInBox(g, AbilitiesTables.CharismaReactionBonus(_character.Abilities.Charisma).ToString()+"%", new Rectangle(805, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaMaxHenchmen(_character.Abilities.Charisma).ToString() , new Rectangle(810, 447, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaLoyaltyBonus(_character.Abilities.Charisma).ToString() + "%", new Rectangle(805, 505, 95, 66));

            //Number of attacks per round
            DrawInBox(g, _character.NumberOfAttacks.ToString(), new Rectangle(17, 655, 155, 66));

            //Weight allowance
            DrawInBox(g, _character.CurrentCarryWeight.ToString() + "/" + _character.MaxCarryWeight.ToString(), new Rectangle(7, 760, 155, 66), true, StringAlignment.Center, _handFontSmall12);

            int xpBonus = XpBonusCalculator.GetXpModifier(_character.Class, _character.Abilities);
            //XP
            DrawInBox(g, xpBonus.ToString()+"%", new Rectangle(630, 761, 155, 66), true, StringAlignment.Center, _handFontSmall14);
            DrawInBox(g, _character.Experience.ToString() + " XP", new Rectangle(630, 785, 155, 66), true, StringAlignment.Center, _handFontSmall10);

            //Gold
            DrawInBox(g, _character.GoldPieces.ToString() + " GP", new Rectangle(800, 765, 155, 66), true, StringAlignment.Center, _handFontSmall12);

            //Equipped Items
            var monoFont = new Font("Consolas", 8);   // eller "Courier New"

            var itemList = _character.Equipment
                .Where(kv => kv.Value != null)
                .Select(kv =>
                {
                    var name = kv.Value!.Name.PadRight(18);
                    var weight = kv.Value.Weight.ToString().PadRight(5);
                    var slot = kv.Key.ToString().PadRight(15);
                    var cost = kv.Value.Cost.ToString().PadRight(5);

                    return $"{name}{weight}{slot}{cost}";
                })
                .ToList();

            if (itemList.Count == 0)
            {
                itemList.Add("(none equipped)");
            }

            int EuipedItemYOffset = 0;
            int additionalItemLineSpacingEvery4thRowCounter = 0;

            foreach (var item in itemList)
            {
                DrawInAlignedBox(
                    g,
                    item,
                    new Rectangle(-150, 850 + EuipedItemYOffset, 350, 66), // bredare box
                    false,
                    StringAlignment.Near,
                    monoFont   // ← viktig ändring
                );

                EuipedItemYOffset += 14;
                additionalItemLineSpacingEvery4thRowCounter++;

                if (additionalItemLineSpacingEvery4thRowCounter % 4 == 0)
                {
                    EuipedItemYOffset += 1;
                }
            }


            var equippedItems = _character.Equipment
                .Where(kv => kv.Value != null)
                .Select(kv => kv.Value!)
                .ToHashSet();

            var unequippedItems = _character.Inventory
                .Where(item => !equippedItems.Contains(item))
                .ToList();

            //not equipped non-magic items
            int NotEuipedItemYOffset = 0;
            var notEquippedItems = unequippedItems
                    //.Where(item => item.MagicBonus <= 0 && (item.SpecialAbilities == null || item.SpecialAbilities.Count == 0))
                      .Where(item => item.MagicBonus <= 0 && (item.SpecialAbilities == null || !item.SpecialAbilities.Any()))
                 .Select(item => item.Name)
                .ToList();

            foreach(var item in notEquippedItems)
            {
                DrawInAlignedBox(g, item, new Rectangle(263, 849 + NotEuipedItemYOffset, 177, 66), false, StringAlignment.Near, _handFontSmall8);
                NotEuipedItemYOffset += 14;
                additionalItemLineSpacingEvery4thRowCounter++;
                if (additionalItemLineSpacingEvery4thRowCounter % 4 == 0)
                {
                    NotEuipedItemYOffset += 1;
                }
            }

            //not equipped magic items
            int MagicItemYOffset = 0;
            var MagicItems = unequippedItems
                .Where(item => item.MagicBonus > 0 || (item.SpecialAbilities != null && item.SpecialAbilities.Count > 0))
                .Select(item => item.Name)
                .ToList();

            foreach (var item in MagicItems)
            {
                DrawInAlignedBox(g, item, new Rectangle(420, 740 + MagicItemYOffset, 175, 66), false, StringAlignment.Near, _handFontSmall8);
                MagicItemYOffset += 14;
                additionalItemLineSpacingEvery4thRowCounter++;
                if (additionalItemLineSpacingEvery4thRowCounter % 4 == 0)
                {
                    MagicItemYOffset += 1;
                }
            }




            //spells
            if ((_character.Class == CharacterClass.MagicUser) || (_character.Class == CharacterClass.Illusionist) || (_character.Class == CharacterClass.Ranger)
                || (_character.Class == CharacterClass.Cleric) || (_character.Class == CharacterClass.Druid) || (_character.Class == CharacterClass.Paladin))
            {
                var spellList = _knownSpellLines.Take(MaxSpellsOnSheet).ToList();

                int yOffset = 0;
                int additionalLineSpacingEvery4thRowCounter = 0; // Additional spacing for spells with longer names
                foreach (var spell in spellList)
                {
                    DrawInAlignedBox(g, spell, new Rectangle(100, 554 + yOffset, 177, 66), false, StringAlignment.Near, _handFontSmall8);
                    yOffset += 13; // Adjust this value to control the spacing between spells
                    additionalLineSpacingEvery4thRowCounter++;
                    if (additionalLineSpacingEvery4thRowCounter % 4 == 0)
                    {
                        yOffset += 1; // Additional spacing for every 4th spell
                    }
                }

                if (_knownSpellLines.Count > MaxSpellsOnSheet)
                {
                    var hiddenCount = _knownSpellLines.Count - MaxSpellsOnSheet;
                    DrawInAlignedBox(g, $"(+{hiddenCount} more; see spell list)", new Rectangle(100, 554 + yOffset + 2, 220, 66), false, StringAlignment.Near, _handFontSmall8);
                }
            }

        }

        private static string BuildDualClassSheetLine(Character c)
        {
            if (!c.IsDualClassed || !c.DualClass.HasValue)
                return string.Empty;

            var former = c.DualClassOriginalClass?.ToDisplayString() ?? "Unknown";
            var formerLevel = Math.Max(0, c.DualClassOriginalLevel);
            return $"Dual-class: {c.DualClass.Value.ToDisplayString()} (from {former} L{formerLevel}) - {c.DualClassState}";
        }

        private static List<string> BuildKnownSpellLines(Character character)
        {
            if (character.Spellcasting == null || character.Spellcasting.Count == 0)
                return new List<string>();

            var spellRepo = new SpellRepository();
            return character.Spellcasting
                .SelectMany(state =>
                {
                    var classSpells = spellRepo.LoadByClass(state.SpellClass);
                    return classSpells
                        .Where(s => state.KnownSpellIds.Contains(s.Id))
                        .Select(s => $"L{s.Level} {s.Name}");
                })
                .Distinct()
                .ToList();
        }

        private void ShowAllSpellsDialog()
        {
            using var form = new Form
            {
                Text = $"{_character.Name} - All Spells",
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(480, 520),
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog
            };

            var list = new ListBox
            {
                Left = 12,
                Top = 12,
                Width = 456,
                Height = 460,
                Font = new Font("Consolas", 10f)
            };

            foreach (var spell in _knownSpellLines)
                list.Items.Add(spell);

            var close = new Button
            {
                Text = "Close",
                Left = 393,
                Top = 482,
                Width = 75,
                DialogResult = DialogResult.OK
            };

            form.Controls.Add(list);
            form.Controls.Add(close);
            form.AcceptButton = close;
            form.CancelButton = close;

            form.ShowDialog(this);
        }

        private readonly Random _rnd = new Random();
        private void DrawBoldString(Graphics g, string text, Font font, Brush brush, Rectangle rect, StringFormat format)
        {
            // Rita texten 3 gånger med små förskjutningar
            g.DrawString(text, font, brush, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), format);
            g.DrawString(text, font, brush, new Rectangle(rect.X + 1, rect.Y, rect.Width, rect.Height), format);
            g.DrawString(text, font, brush, new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height), format);
        }

        /*
        private void DrawInBox(Graphics g, string text, Rectangle rect, Font font = null)
        {
            font ??= _handFont; // default: stora fonten

            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            float angle = (float)(_rnd.NextDouble() * 8 - 4);
            int dx = _rnd.Next(-3, 4);
            int dy = _rnd.Next(-3, 4);

            var state = g.Save();

            g.TranslateTransform(rect.X + rect.Width / 2 + dx,
                                 rect.Y + rect.Height / 2 + dy);
            g.RotateTransform(angle);

            DrawBoldString(
                g,
                text,
                font,
                _ink,
                new Rectangle(-rect.Width / 2, -rect.Height / 2, rect.Width, rect.Height),
                format);

            g.Restore(state);
        }
        */


        private void DrawInBox(Graphics g, string text, Rectangle rect, bool offsetFont = false, StringAlignment lineAlignment = StringAlignment.Center, Font font = null)
        {
            font ??= _handFont; // default: stora fonten

            var format = new StringFormat
            {
                Alignment = lineAlignment,
                LineAlignment = lineAlignment
            };

            float angle = (float)(_rnd.NextDouble() * 8 - 4);
            int dx = _rnd.Next(-3, 4);
            int dy = _rnd.Next(-3, 4);

            var state = g.Save();


            if (offsetFont)
            {
                g.TranslateTransform(rect.X + rect.Width / 2 + dx,
                                 rect.Y + rect.Height / 2 + dy);
                g.RotateTransform(angle);
            }
            else
            {
                g.TranslateTransform(rect.X + rect.Width / 2 , rect.Y + rect.Height / 2);
            }
            DrawBoldString(
                g,
                text,
                font,
                _ink,
                new Rectangle(-rect.Width / 2, -rect.Height / 2, rect.Width, rect.Height),
                format);

            g.Restore(state);
        }
        
        private void DrawInAlignedBox(Graphics g, string text, Rectangle rect, bool offsetFont = false,
                       StringAlignment lineAlignment = StringAlignment.Near, Font font = null)
        {
            font ??= _handFont;

            var format = new StringFormat
            {
                Alignment = StringAlignment.Near,      // LEFT
                LineAlignment = StringAlignment.Center // vertical center
            };

            float angle = (float)(_rnd.NextDouble() * 8 - 4);
            int dx = _rnd.Next(-3, 4);
            int dy = _rnd.Next(-3, 4);

            var state = g.Save();

            if (offsetFont)
            {
                g.TranslateTransform(rect.X + rect.Width / 2 + dx,
                                     rect.Y + rect.Height / 2 + dy);
                g.RotateTransform(angle);
            }
            else
            {
                g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            }

            // LEFT‑aligned rectangle
            var drawRect = new Rectangle(0, -rect.Height / 2, rect.Width, rect.Height);

            DrawBoldString(g, text, font, _ink, drawRect, format);

            g.Restore(state);
        }


        private void DrawInCircle(Graphics g, string text, Rectangle circleBounds)
        {
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            float angle = (float)(_rnd.NextDouble() * 8 - 4);
            int dx = _rnd.Next(-3, 4);
            int dy = _rnd.Next(-3, 4);

            var state = g.Save();

            g.TranslateTransform(circleBounds.X + circleBounds.Width / 2 + dx,
                                 circleBounds.Y + circleBounds.Height / 2 + dy);
            g.RotateTransform(angle);

            DrawBoldString(
                g,
                text,
                _handFont,
                _ink,
                new Rectangle(-circleBounds.Width / 2, -circleBounds.Height / 2,
                              circleBounds.Width, circleBounds.Height),
                format);

            g.Restore(state);
        }

    }
}
