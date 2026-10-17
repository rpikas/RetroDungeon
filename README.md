After donwload, Extract and opening AdndGame.slnx in Visual studio, right click on AdndGame and select as start up project

<img width="416" height="596" alt="image" src="https://github.com/user-attachments/assets/f850b80c-e9f4-476e-a94e-dcee59838779" />

To get started playing the game:
1. Go to Traning grounds to Create charaters
<img width="981" height="408" alt="image" src="https://github.com/user-attachments/assets/6c4acf1f-a17c-4e71-911e-c0ad1a48483d" />
Depending on selected race abilities are sightly modified.
Depening on Abilities and race selected, different classes can be selected
<img width="490" height="659" alt="image" src="https://github.com/user-attachments/assets/1ee868f9-8d95-4b61-85e2-caeb42f583cb" />
 
4. Go to Gilgamesh to construct a party
5. Go to Boltax to shop equipment
6. Equip the items
 <img width="1117" height="749" alt="image" src="https://github.com/user-attachments/assets/49908756-a2a0-4b1c-9754-b7553ea54b39" />


8. Enter the Maze
 <img width="1212" height="859" alt="image" src="https://github.com/user-attachments/assets/c8090d7a-0684-4b86-8fd2-7f2f22e416bd" />

9. Fight the mosters
<img width="1208" height="788" alt="image" src="https://github.com/user-attachments/assets/c4d3fb61-b779-4c16-874f-af4f30cf9ca7" />

Happy Gaming!

## Two-handed equipment

With Old Style UI enabled, the character inspect equipment action uses a framed
keyboard prompt: **E)quip**, **U)nequip**, or Escape to cancel. Answer alternatives
in old-style dialogs match the main question or message font size. Combat confirmations
(including Disarm Trap), trap party selection, and inspect/combat choice prompts also
use borderless black frames, 22-point titles, and 18-point questions and answers.
Selection prompts accept Enter to select and Escape to go back. For numbered spell
and target prompts, type the option number and press Enter. The elevator also uses
this old-style frame, with a scrollable level list; type the dungeon level and press
Enter, or Escape to cancel. The camp Inspect character chooser and character Inspect
screen also use the borderless old-style frame. The Inspect screen retains its letter
shortcuts and uses matching 18-point details and menu text beneath a 22-point title.
Old-style target-group prompts show the question followed by each numbered group
on its own line. Modern UI keeps its existing dialogs.
The Trap Triggered message uses the same 22-point font for its heading, trap name,
and outcome, with the dialog height expanding to fit wrapped text.

A two-handed item requires
it, and then occupies both hands. Unequip it before equipping another item in either
hand; equipping a replacement does not remove existing hand equipment automatically.
Shop purchases and treasure retain this property, and older saved weapons recover
missing two-handed flags from the item catalogue when characters are loaded.

## Potions

Potions always affect the character using them, both in combat and outside combat.
Using a potion does not ask for an ally target; other ally-targeted items still do.

## Walking effect notifications

The Effects dialog offers **D)on't show again** and **S)how only every 10 steps**.
These choices only change notifications: effects still advance and are saved on each
step. Suppression resets when the active effects change or wear off; expiration,
recovery, and death messages are still shown. Enter or Escape continues without
changing the current notification frequency. These choices are also available in
the tabletop viewer.

## Combat formation

The front rank is the first three party members currently able to fight, in marching
order. Incapacitated members do not occupy these positions: the next available member
can fight in melee and can be targeted by monsters. This includes sleep, paralysis,
petrification, confusion, and forced rest. Poison, blindness, nausea, and slow alone
do not remove a fighter from the front rank. If nobody can fight, monsters can still
attack surviving party members. Recovery restores the normal marching-order priority.

## Thief ability references

Thief Find Traps and Disarm Traps checks and results link to
`PHB28TheifSpeicilaAbilitesChance.png` in the rule/dice log when references are enabled.
Thief scroll-deciphering checks using Read Languages also reference this table.

Treasure-type roll entries link to `MM105TreasureTypes.png`, and main magic-item
category rolls link to `DMG121MagicItemsMainTable.png` when rule references are enabled.
The following subtable rolls link to their respective images, including treasure
that rolls directly on a subtable:
- Swords (G): `DMG124TreasureTableG_Swords.png`
- Potions (A): `DMG121TreasureTableA_Potions.png`
- Miscellaneous Weapons (H): `DMG125TreasureTableHMiscellaneousWeapons.png`
- Rings (C): `DMG122TreasureTableC_Rings.png`
- Rods, Staves & Wands (D): `DMG125TreasureTableD_RodsStavesAndWands.png`
- Scrolls (B): `DMG121TreasureTableB_Scrolls.png`

## Conditional sword bonuses

The +1 swords versus magic-using/enchanted creatures, lycanthropes/shape changers,
regenerating creatures, and reptiles act as +2, +3, +3, and +4 respectively against
qualifying monsters, for both to-hit and damage. These are total bonuses, replacing
rather than stacking with the normal +1. Spellcasting includes cleric/druid spells
and the monster catalogue's Spell Use, Magic Use, and Spellcasting abilities.
Applicable totals are logged on each attack attempt, even on a miss; the enhanced
bonus also counts toward the target's minimum magical-weapon requirement.

## Purple Worm

A bite deals 2d12 damage. A real bite hit at least 4 above the required roll, or a
natural 20, swallows the victim. All playable races fit the worm's swallowing limit.
The victim has six full subsequent melee rounds before death; killing that exact
worm releases its victims, even while other groups remain. Bodies recovered after
death can still be raised until digestion completes at 12 turns (120 melee rounds).
Swallowed characters fight only their swallowing worm from inside, using an equipped
slashing or piercing weapon against AC 9. Each attack loses 1 damage in the first
round inside, 2 in the second, and so on, down to zero damage. They do not occupy an
outside front-rank slot and cannot be targeted by outside attacks or spells.
The encounter UI and logs show the deadline and damage penalty. Fleeing or abandoning
the encounter, or allowing the swallowing worm to flee, leaves any unrecovered victims
permanently Lost; live worms and their victims are not persisted outside encounters.

The sting deals 2d4 damage, followed on every hit by a save vs. poison or immediate
death; level-11+ monk poison immunity still applies. It is available in rear defense
against a backstab, or with at least four exposed opponents in a spacious area.
For maze encounters, spacious means the party is surrounded by a clear 3-by-3 area
of floor, not a narrow corridor. The current playable races are not large opponents.

## Carrion stench nausea

Each monster group with Nausea causes one saving throw vs. poison per living party
member per battle, before attacks begin. A failed save gives -2 to hit that group's
nausea-causing monsters for the rest of the battle, without stacking or rerolling for
each monster or round. Exposure and affected attack rolls are logged. The penalty is
encounter-only and is removed on victory, defeat, or escape; it does not change the
character's saved THAC0 or attack bonuses.

## Monster healing in combat

Monster casters only choose healing spells when a living monster (including the caster)
can regain HP. Otherwise, they cast an available non-healing spell or attack normally.
Damage that cannot be healed by spells, such as Sword of Wounding damage, does not
make a monster an eligible healing target.

## The tabletop viewer (optional)

`WizardryViewer/` is a Unity project that watches the game and lays it out as a miniatures
table — the party as figures on 1-inch tiles, walking the corridors and fighting in real time,
on a second monitor. The game works exactly as before without it: it publishes a snapshot to
`127.0.0.1:8787` each turn and nothing minds if no one is listening.

Needs Unity 6000.4. See `WizardryViewer/README.md` to build it, then start both halves with:

```powershell
.\play-wizardry.ps1
```
