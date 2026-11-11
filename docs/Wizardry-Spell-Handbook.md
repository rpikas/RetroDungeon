# Wizardry Spell Handbook

A player's guide to the Wizardry Mage and Priest spells in RetroDungeon. The Mage and Priest each have seven spell circles. Cast from camp (exploration spells) or in combat (combat spells). The table is defined in `Adnd.Core\Wizardry\WizardrySpells.cs`; this guide describes what each spell does in the game.

**Status:** ✔ works · ✘ listed in the spell book but has no effect yet.

**Where:** C = combat only, E = outside combat only (camp/dungeon), B = both.

## General rules

- **Damage and saves:** a damage spell that hits a monster allows a save; a successful save halves the damage. Sleeping or paralyzed monsters take double damage.
- **Single target / group / all:** single-target spells hit one monster, group spells hit every monster in the chosen group, and the "all enemies" spells (Tiltowait, Malikto) hit every group.
- **Armor Class:** lower is better. Battle-long AC spells end when the fight ends. Repeated casts stack, except Maporfic, which only refreshes.
- **Silence:** a silenced monster cannot cast spells.
- **Death:** a Dead character can be healed back by Di, or at the Temple. Ashes need Kadorto or the Temple. Lost characters are gone.

---

## Mage spells

### Circle 1

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Dumapic** | E | Reports your level, coordinates and the direction you face. Dungeon only. | ✔ |
| **Halito** | C | Fireball: 1d8 damage to one monster. | ✔ |
| **Katino** | C | Puts a monster group to sleep. Sleepers take double damage. | ✔ |
| **Mogref** | C | Caster's AC −2 for the battle. | ✔ |

### Circle 2

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Dilto** | C | Darkness on a monster group: their AC worsens by 2 for the battle, so they are easier to hit. | ✔ |
| **Melito** | C | Sparks: 1d8 damage to every monster in a group. | ✔ |
| **Sopic** | C | Caster's AC −4 for the battle. | ✔ |

### Circle 3

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Mahalito** | C | Firestorm: 4d6 damage to a group. | ✔ |
| **Molito** | C | Energy blast: 3d6 damage to a group. | ✔ |

### Circle 4

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Dalto** | C | Blizzard: 6d6 cold damage to a group. | ✔ |
| **Lahalito** | C | Inferno: 6d6 fire damage to a group. | ✔ |
| **Morlis** | C | Lesser fear: a group may flee the battle. | ✔ |

### Circle 5

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Madalto** | C | Arctic frost: 8d8 cold damage to a group. | ✔ |
| **Makanito** | C | Suffocates every monster with fewer than 35 HP; they die instantly. | ✔ |
| **Mamorlis** | C | Greater fear: all opponents may flee. | ✔ |

### Circle 6

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Haman** | B | Lesser divine intervention. Level 13+ only. See "Divine intervention" below. | ✔ |
| **Lakanito** | C | Suffocates a whole group regardless of HP; they die instantly. | ✔ |
| **Masopic** | C | Party AC −4 for the battle (stacks with Matu and Bamatu). | ✔ |
| **Zilwan** | C | Destroys one undead monster instantly. | ✔ |

### Circle 7

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Mahaman** | B | Greater divine intervention. Level 13+ only. See "Divine intervention" below. | ✔ |
| **Malor** | E | Teleports the whole party. The caster is asked for squares north/east (negative = south/west) and levels down (negative = up), measured from the party's position, like the Helm of Malor. Items and gold are kept. Fizzles if the level does not exist or the destination is not open ground; a fizzle costs no spell slot. Dungeon only. | ✔ |
| **Tiltowait** | C | 10d10 damage to all enemies. | ✔ |

---

## Divine intervention (Haman and Mahaman)

Only a character of level 13 or higher can cast these. The caster loses one experience level, and the gods then grant one effect at random from those that make sense in the situation:

| Effect | Haman | Mahaman | Where |
|---|---|---|---|
| Augmented magic: the party's damage and healing spells are 50% stronger for the battle | ✔ | ✔ | C |
| Cure party: all conditions removed from every living member | ✔ | ✔ | B |
| Heal party: Madi on everyone (full HP, conditions cleared) | ✔ | ✔ | B |
| Silence enemies: every monster is silenced | ✔ | ✔ | C |
| Teleport enemies: every monster is sent away from the battle | ✔ | ✔ | C |
| Protect party: AC −20 for the battle | | ✔ | C |
| Raise the dead: every dead member gets the Di spell (only offered when someone is dead) | | ✔ | B |

The gods always respond once the level is paid (no failure chance was specified); the 50% boost is a game choice.

---

## Priest spells

### Circle 1

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Badios** | C | Harm: 1d8 damage to one monster. | ✔ |
| **Dios** | B | Heal: restores 1d8 HP to one character. | ✔ |
| **Kalki** | C | Party AC −1 for the battle. | ✔ |
| **Milwa** | E | Light: the party sees one step further for 6 turns + 1 turn per caster level (10 steps per turn). Like the AD&D Light spell. | ✔ |
| **Porfic** | C | Shield: caster's AC −4 for the battle. | ✔ |

### Circle 2

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Calfo** | E | X-ray vision: at a treasure chest, names the trap type. Correct 95% of the time; otherwise a wrong trap is named. | ✔ |
| **Matu** | C | Party AC −2 for the battle. | ✔ |
| **Manifo** | C | Paralyzes a monster group. Paralyzed monsters take double damage. | ✔ |
| **Montino** | C | Silences a monster group. | ✔ |

### Circle 3

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Bamatu** | C | Party AC −4 for the battle. | ✔ |
| **Dialko** | B | Cures paralysis and wakes a sleeping character. | ✔ |
| **Latumapic** | B | Identifies monsters. | ✘ |
| **Lomilwa** | E | More light: the party sees two steps further until it leaves the dungeon or enters a dark zone. Like the AD&D Continual Light spell. | ✔ |

### Circle 4

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Badial** | C | 2d8 damage to one monster. | ✔ |
| **Dial** | B | Restores 2d8 HP to one character. | ✔ |
| **Latumofis** | B | Cures poison on one character. | ✔ |
| **Maporfic** | B | Big shield: party AC −2 until the party leaves the dungeon. A "Maporfic" sign shows at the top of the dungeon view while it is active. Does not stack. | ✔ |

### Circle 5

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Badi** | C | Death: works like the AD&D Slay Living. Undead are unaffected. Any other monster rolls a save vs spell (1d20, needs its save number or higher to survive). If it fails, a monster with Death Resistance gets that percentage chance to survive, and magic resistance can negate the spell. Otherwise it dies instantly. | ✔ |
| **Badialma** | C | 3d8 damage to one monster. | ✔ |
| **Di** | B | Life: raises a dead character, like AD&D Raise Dead. The target rolls a d20 against its Constitution survival chance. Success: back with 1 HP and −1 Constitution. Failure: turned to ashes. Cannot target ashes or lost characters. | ✔ |
| **Dialma** | B | Restores 3d8 HP to one character. | ✔ |
| **Kandi** | E | Locate soul: lists dead, ashes and lost party members and their location. Dead members travel with the party, so the location is the party's current position, level and facing. Dungeon only. | ✔ |
| **Litokan** | C | Flame tower: 3d8 fire damage to a group. | ✔ |

### Circle 6

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Loktofeit** | E | Recall: teleports the whole party to town. Every member loses all equipment and carried items and 90% of their coins. Outside combat only. | ✔ |
| **Lorto** | C | Blades: 6d6 damage to a group. | ✔ |
| **Mabadi** | C | Reduces one monster to 1d8 HP (never raises its HP). | ✔ |
| **Madi** | B | Fully heals one character and clears all conditions except death. | ✔ |

### Circle 7

| Spell | Where | Effect | Status |
|---|---|---|---|
| **Kadorto** | B | Resurrection: like AD&D Resurrection, on a dead or ashes character. Same Constitution roll as Di. Success: back at full HP and −1 Constitution. Failure: the character is lost forever. Lost characters and those who died of old age cannot be restored. | ✔ |
| **Malikto** | C | Word of death: 12d6 damage to all enemies. | ✔ |
