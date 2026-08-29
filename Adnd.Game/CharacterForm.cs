using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Characters.Progression;
using Adnd.Data.Spells;

namespace Adnd.Game.Windows
{
    public class CharacterForm : Form
    {
        private readonly Character _character;
        private readonly Font _handFont = new Font("Bradley Hand ITC", 18, FontStyle.Regular);
        private readonly Font _handFontSmall14 = new Font("Bradley Hand ITC", 14, FontStyle.Regular);
        private readonly Font _handFontSmall12 = new Font("Bradley Hand ITC", 12, FontStyle.Regular);
        private readonly Font _handFontSmall10 = new Font("Bradley Hand ITC", 10, FontStyle.Regular);

        private readonly Font _handFontSmall8 = new Font("Bradley Hand ITC", 8, FontStyle.Regular);

        private readonly Brush _ink = Brushes.Black;
        private readonly Image _sheetBackground;

        public CharacterForm(Character character)
        {
            _character = character;

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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            DrawInBox(g, _character.Name, new Rectangle(45, 80, 198, 40));
            DrawInBox(g, _character.Race.ToString(), new Rectangle(340, 80, 98, 40));
            // DrawInBox(g, _character.Class.ToString(), new Rectangle(500, 80, 86, 40));
//            DrawInBox(g, _character.Class.GetClassName().ToString(), new Rectangle(500, 80, 86, 40), _handFontSmall12);
            DrawInBox(g, _character.Class.ToDisplayString().ToString(), new Rectangle(500, 80, 86, 40), false, StringAlignment.Center, _handFontSmall12);
            //c.Classes.Select(cc => cc.ToDisplayString())
            DrawInBox(g, _character.Level.ToString(), new Rectangle(640, 80, 46, 40));  
            DrawInBox(g, _character.CurrentHitPoints.ToString(), new Rectangle(710, 80, 95, 40));
            DrawInBox(g, _character.ArmorClass.ToString(), new Rectangle(844, 80, 45, 40));

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
            DrawInBox(g, AbilitiesTables.StrengthTHModifier(_character.Abilities.Strength).ToString(), new Rectangle(40, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthDamageModifier(_character.Abilities.Strength).ToString(), new Rectangle(40, 427, 95, 66));
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
            if (_character.Class != CharacterClass.Thief)
            {
                DrawInBox(g, "NA", new Rectangle(660, 537, 95, 66));
            }
            else
            {
                DrawInBox(g, AbilitiesTables.DexterityPickingPockets(_character.Abilities.Dexterity).ToString() + "%", new Rectangle(660, 537, 95, 66));
                DrawInBox(g, AbilitiesTables.DexterityOpenLocks(_character.Abilities.Dexterity).ToString() + "%", new Rectangle(660, 578, 95, 66));
                DrawInBox(g, AbilitiesTables.DexterityLocateRemoveTraps(_character.Abilities.Dexterity).ToString() + "%", new Rectangle(560, 618, 95, 66));
                DrawInBox(g, AbilitiesTables.DexterityMoveSilently(_character.Abilities.Dexterity).ToString() + "%", new Rectangle(660, 618, 95, 66));
                DrawInBox(g, AbilitiesTables.DexterityHideInShadows(_character.Abilities.Dexterity).ToString() + "%", new Rectangle(800, 618, 95, 66));
            }

            //CHARISMA MODIFIER
            DrawInBox(g, AbilitiesTables.CharismaReactionBonus(_character.Abilities.Charisma).ToString()+"%", new Rectangle(805, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaMaxHenchmen(_character.Abilities.Charisma).ToString() , new Rectangle(810, 447, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaLoyaltyBonus(_character.Abilities.Charisma).ToString() + "%", new Rectangle(805, 505, 95, 66));

            //Number of attacks per round
            DrawInBox(g, _character.NumberOfAttacks.ToString(), new Rectangle(17, 655, 155, 66));

            //Weight allowance
            DrawInBox(g, _character.CurrentCarryWeight.ToString() + "/" + _character.MaxCarryWeight.ToString(), new Rectangle(7, 765, 155, 66), true, StringAlignment.Center, _handFontSmall12);

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
                var spellRepo = new SpellRepository();
                var spellList = _character.Spellcasting

                    .SelectMany(state =>
                    {
                        var classSpells = spellRepo.LoadByClass(state.SpellClass);
                        return classSpells
                            .Where(s => state.KnownSpellIds.Contains(s.Id))
                            .Select(s => $"L{s.Level} {s.Name}");
                    })
                    .Distinct()
                    .ToList();

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
            }

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
