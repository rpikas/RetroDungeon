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
        private readonly Brush _ink = Brushes.Black;
        private readonly Image _sheetBackground;

        public CharacterForm(Character character)
        {
            _character = character;

            _sheetBackground = Image.FromFile(
                @"C:\Users\rober\source\repos\RetroDungeon\Adnd.Game\Assets\ScenPictures\character_sheet.png");

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
            DrawInBox(g, _character.Class.ToString(), new Rectangle(500, 80, 86, 40));
            DrawInBox(g, _character.Level.ToString(), new Rectangle(640, 80, 46, 40));
            DrawInBox(g, _character.CurrentHitPoints.ToString(), new Rectangle(710, 80, 95, 40));
            DrawInBox(g, _character.ArmorClass.ToString(), new Rectangle(844, 80, 45, 40));

            // Ability circles: write only the value inside each circle, not labels.
            DrawInCircle(g, _character.Abilities.Strength.ToString(), new Rectangle(40, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Intelligence.ToString(), new Rectangle(200, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Wisdom.ToString(), new Rectangle(350, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Constitution.ToString(), new Rectangle(507, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Dexterity.ToString(), new Rectangle(660, 300, 66, 66));
            DrawInCircle(g, _character.Abilities.Charisma.ToString(), new Rectangle(815, 300, 66, 66));
        }

        private void DrawInBox(Graphics g, string text, Rectangle rect)
        {
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, _handFont, _ink, rect, format);
        }

        private void DrawInCircle(Graphics g, string text, Rectangle circleBounds)
        {
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, _handFont, _ink, circleBounds, format);
        }
    }
}
