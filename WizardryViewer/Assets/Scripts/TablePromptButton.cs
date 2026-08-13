using UnityEngine;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// One choice, as a card lying on the table. Holds the command id it stands for and nothing else:
    /// what happens when it is pressed is <see cref="TablePrompt"/>'s business, and whether the choice
    /// is legal was already decided by the game before it was ever offered.
    /// </summary>
    public sealed class TablePromptButton : MonoBehaviour
    {
        private const float FlashSeconds = 0.14f;

        private Renderer _face;
        private Material _rest;
        private Material _lit;
        private float _flashRemaining;
        private bool _hovered;

        /// <summary>The protocol command id sent when this card is pressed, e.g. "fight".</summary>
        public string OptionId { get; private set; }

        public void Configure(string optionId, Renderer face, Material rest, Material lit)
        {
            OptionId = optionId;
            _face = face;
            _rest = rest;
            _lit = lit;
            Apply();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered) return;
            _hovered = hovered;
            Apply();
        }

        /// <summary>Acknowledge a press, so a click that the game is slow to act on still feels heard.</summary>
        public void Flash() => _flashRemaining = FlashSeconds;

        private void Update()
        {
            if (_flashRemaining <= 0f) return;

            _flashRemaining -= Time.deltaTime;
            if (_flashRemaining <= 0f) Apply();
        }

        private void Apply()
        {
            if (_face == null) return;

            // Assigned, never modified: touching a material's properties at runtime would edit the
            // shared asset, and creating one here would leave a player build with its shader stripped.
            var wanted = (_hovered || _flashRemaining > 0f) ? _lit : _rest;
            if (wanted != null) _face.sharedMaterial = wanted;
        }
    }
}
