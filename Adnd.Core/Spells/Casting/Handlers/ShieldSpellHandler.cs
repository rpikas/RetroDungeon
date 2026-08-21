using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers
{
    public sealed class ShieldSpellHandler : ISpellEffectHandler
    {
        public bool CanHandle(string spellId) => string.Equals(spellId, "shield_spell", StringComparison.OrdinalIgnoreCase);

        public SpellCastResult Resolve(SpellCastRequest request)
        {
            var spell = request.Spell;
            if (spell == null)
            {
                return SpellCastResult.Failure("Spell not found.");
            }

            var target = request.Caster; // The shield spell targets the caster themselves

            const int shieldAC = 2; //Shield spell provides AC 2

            //Could check if ac lower than 2, but lets skip that for now, 
            var oldAc = target.ArmorClass;
            target.ArmorClass = Math.Min(target.ArmorClass, shieldAC);
            // Log the event
            var events = new List<string>
            {
                $"{target.Name} casts {spell.Name}, increasing their Armor Class from {oldAc} to {target.ArmorClass}."
            };
            return new SpellCastResult
            {
                Success = true,
                Events =
                {
                    $"{target.Name} casts {spell.Name}, changing their Armor Class from {oldAc} to {target.ArmorClass}."
                }
            };
        }

    }
}

