using System;
using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Characters;

namespace Adnd.Game.Windows
{
    public class CharacterForm : Form
    {
        private readonly Character _character;
        private readonly Font _handFont = new Font("Bradley Hand ITC", 18, FontStyle.Regular);
        private readonly Font _handFontSmall = new Font("Bradley Hand ITC", 14, FontStyle.Regular);
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
            DrawInBox(g, _character.Class.ToString(), new Rectangle(500, 80, 86, 40), _handFontSmall);
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
                DrawInCircle(g, _character.Abilities.Strength.ToString() + "/" + _character.ExceptionalStrengthPercentile, new Rectangle(30, 300, 116, 66));
            }
            DrawInCircle(g, _character.Abilities.Intelligence.ToString(), new Rectangle(200, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Wisdom.ToString(), new Rectangle(350, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Constitution.ToString(), new Rectangle(507, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Dexterity.ToString(), new Rectangle(660, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Charisma.ToString(), new Rectangle(815, 300, 66, 66));

            var creator = new CharacterCreator();

            //STRENGTH MODIFIER
            DrawInBox(g, AbilitiesTables.StrengthTHModifier(_character.Abilities.Strength).ToString(), new Rectangle(40, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthDamageModifier(_character.Abilities.Strength).ToString(), new Rectangle(40, 427, 95, 66));
            DrawInBox(g, AbilitiesTables.StrengthWeightAllowanceModifier(_character.Abilities.Strength).ToString(), new Rectangle(40, 465, 95, 66));
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
                DrawInBox(g, "NA", new Rectangle(350, 395, 95, 66));
            }
            else 
            {
                DrawInBox(g, AbilitiesTables.WisdomBonus(_character.Abilities.Wisdom).ToString(), new Rectangle(350, 395, 95, 66));
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
            DrawInBox(g, AbilitiesTables.DexterityACModifier(_character.Abilities.Dexterity).ToString(), new Rectangle(660, 452, 45, 40));

            //CHARISMA MODIFIER
            DrawInBox(g, AbilitiesTables.CharismaReactionBonus(_character.Abilities.Charisma).ToString()+"%", new Rectangle(805, 390, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaMaxHenchmen(_character.Abilities.Charisma).ToString() , new Rectangle(810, 447, 95, 66));
            DrawInBox(g, AbilitiesTables.CharismaLoyaltyBonus(_character.Abilities.Charisma).ToString() + "%", new Rectangle(805, 505, 95, 66));
            
        }

        private readonly Random _rnd = new Random();
        private void DrawBoldString(Graphics g, string text, Font font, Brush brush, Rectangle rect, StringFormat format)
        {
            // Rita texten 3 gånger med små förskjutningar
            g.DrawString(text, font, brush, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), format);
            g.DrawString(text, font, brush, new Rectangle(rect.X + 1, rect.Y, rect.Width, rect.Height), format);
            g.DrawString(text, font, brush, new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height), format);
        }
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
