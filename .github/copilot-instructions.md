
# Copilot Instructions

## Project Guidelines
- Use the return-style arrow symbol (↵) instead of '<-' in UI labels where applicable.
## Game Mechanics
- At encounter start (before round 1 only), party and monsters roll surprise; if only one side is surprised, it cannot act in round 1. Surprise must be shown in AD&D rule/dice info and encounter dialog.
- When adjusting Blade Barrier in this project, use Cleric level 6 (not level 8).
- Finger of Death should be a Druid spell at level 7 (not a Magic-User spell).
- Slay Living is a level 5 Cleric spell: save vs spell or die; undead are unaffected; a "Death Resistance" ability percentage gives that chance to survive (also applies to Finger of Death).
- Monster spellcasting: a monster with "Level N Mage/Priest/Druid/Illusionist spells" always uses them (100%) for every class and level. Only age-related dragon magic has a configured chance (previously a 50% default applied to several levels).
- Wizardry monster abilities: Run (flees at ≤25% HP, 50%/turn), Help (25%/turn once per combat, calls 1d4 of its own kind; reinforcements can't call more), LvlDrain (drains N levels on hit), Decapitate (5% per hit, save vs Death or die), Stoning (petrify on hit, save vs Petrification), Breath Attack (X) (33%/turn, half current HP to the party, save for half), Fire/Cold Resistance (halves spell damage of that element). Slept is informational (Sleep already works on all non-undead).
- Cure Serious Wounds should exist as a level 4 Druid spell and work exactly like the Cleric version (same healing behavior).
- Improved Phantasmal Force should be configured as a level 2 Illusionist spell (not level 3).
- In combat event text, when a monster has the Paralyzed status, use 'is paralyzed' wording instead of 'is held'.
- InLair chance must be rolled once per encounter group, not once per individual monster.
- Monk open-hand attacks per melee round should be 1.25 at levels 4 and 5 (instead of 1.5); all other monk attack progression values remain unchanged.
- For attack cadence, when the number of attacks is 1.25, the character should get 2 attacks every 4th round.
- Monks of level 5 and above are immune to disease, slow, and haste effects.
- Monks at level 11 and above are immune to poison.
- Slowed monsters should only be able to attack every second round, not every round.

## Artwork Filenames
- When resolving monster artwork filenames, also try variants with commas removed from monster names (e.g., 'Ape, Carnevourus' -> 'ApeCarnevourus').