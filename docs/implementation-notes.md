# Implementation Notes: Monster Powers and Spells

Reference for how Monster Manual powers and recent spells are wired into the engine, plus what is still missing.
Status: built, **not yet play-tested** unless stated.

## Adding a spell

1. Add `Adnd.Data\Spells\<id>.json` (keys: Id, Name, Class, Level, Description, RangeType, Targeting, CastContext, EffectType, EffectDescription; add TargetingScope for enemy spells).
2. Add a handler in `Adnd.Core\Spells\Casting\Handlers\` implementing `ISpellEffectHandler` (`CanHandle`, `Resolve`).
3. Register the handler in **all three** lists, otherwise combat reports "Spell effect not implemented":
   - `Adnd.Game\PartyMenu.cs`
   - `Adnd.Game\CampCharacterInspectForm.cs`
   - `Adnd.Game\Combat\CombatCoordinator.cs`
4. Build with `dotnet build --nologo -v q`. The build fails with file-lock errors if the game is running; close it first.

## CombatResolver layout and tests

`CombatResolver` is a partial class in `Adnd.Core\Combat\Resolution`:

- `CombatResolver.cs`: fields, `ResolveRound` (setup, initiative, Time Stop, phases), `FinalizeRound`, shared helpers.
- `.RoundState.cs`: state shared by the turn methods.
- `.PartyTurn.cs`: `ResolvePartyMemberStartOfTurn` (status ticks) and `ResolvePartyMemberAction` (action switch).
- `.MonsterTurn.cs`: `ResolveMonsterTurn` dispatching to stages (`MonsterTurnFlow`: Proceed / EndTurn / EndRound), then `ResolveMonsterAttackSwing` and `ApplyMonsterOnHitEffects`.
- Other partials are grouped by area (`PartyAttack`, `MonsterSpells`, `MonsterPowers`, `MagicWeapons`, `LevelN`, ...).

Characterization tests are in `Adnd.Tests` (xUnit). Run `dotnet test Adnd.Tests --nologo`. Monster hit points use the static `DiceRoller`, so use high hit dice in tests to avoid early deaths.

## Adding a monster power

- Monster stats live in `Adnd.Data\Monsters\LevelN.json`. Hit points come from `HitDice` x d8 (+ `ExtraHitPoints`); for fixed-hp monsters use hp / 4.5 as hit dice.
- Abilities and defenses are matched **by name** (`HasSpecialAbility`, `HasSpecialDefense`, `IsTemplate`). The JSON name must match the string used in code.
- Per-monster turn powers are called from `ResolveMonsterBreathGazesAndPowers` / `ResolveMonsterLatePowers` in `CombatResolver.MonsterTurn.cs` (`TryResolveLevel8Powers`, `...Level9Powers`, `...Level10Powers`, `TryResolveGreenSlime`). Returning true ends that monster's turn.
- Monster spellcasting is table-driven: add a row to `MonsterSpellAttempts` in `CombatResolver.MonsterTurn.cs`.
- On-hit effects go through `TryApplyLevel8OnHit`, `TryApplyPitFiendTail`, `TryApplyLevel10OnHit`.
- Spell reactions of monsters (golems, Beholder, slime) are at the top of `SpellDamageSaveHelper.ApplyToMonster`.
- Weapon immunity: `CanWeaponHarmTargetByMagicRequirement` (defense name "Immune to Weapons" blocks all weapons).
- `session.Outcome = CombatOutcome.Escaped` ends combat with no XP or treasure.
- Do not re-add Pyrotechnics, Produce Flame, ESP or Detect Magic to monster JSON.

## Spells

| Spell | Class / Level | Handler | Behaviour | Gaps |
|---|---|---|---|---|
| Blink | MU 3 | `BlinkHandler` | 1 round per level. 50% chance attacks against the caster miss. Caster rolls d12 (7+) for a d8 direction; behind (3-5) gives +2 to hit. | Rear attack ignores shield/Dex of target. |
| Wall of Stone | MU 5 | `WallOfStoneHandler`, `WallOfStoneRules` | Ends combat as Escaped (no XP/treasure) if all monsters are ordinary. Burrowers (Purple Worm, Umber Hulk, Earth Elemental, Xorn) are delayed 1-2 rounds. Casters/fiends breach with 1-in-3 per round (`MonsterStatus.WalledOff`). | "Destroyed by attacks" is flavour only. Mixed fights can leave blocked monsters alive. |
| Hold Monster | MU 5 | `HoldMonsterHandler` | Up to 4 targets from a group. Saves: normal vs 3-4, -1 vs 2, -3 vs 1. Failed save = paralyzed 2-6 rounds; saved raw but failed by the penalty = slowed. | Target count cannot be chosen. |
| Time Stop | MU 9 | `TimeStopHandler`, `CombatSession.StartTimeStop` | 1d3 extra actions. Monsters and pets skip; only the caster is prompted; others show "TIME STOPPED". | Poison/bleeding still tick. |
| Dispel Magic | Cleric 3 | `DispelMagicHandler` | 50% +5%/level above, -2%/level below. Removes monster buffs (invisible, mirror image, barkskin, illusions) and party harmful magic (charm, confuse, slow, web, sleep). | No area check; items and counterspells not handled. |
| Animal Friendship | Druid 1 | `AnimalFriendshipHandler` | Animal, INT <= 4, up to 2 HD per caster level, save vs spell, then charmed. | Single target only. |
| Word of Recall / Teleport | Cleric 6 / MU 5 | `ReturnToTownHandler` | Return the caster to town. | |

## Monsters

| Monster | Where | Implemented | Gaps |
|---|---|---|---|
| Level 7 and 8 sets | `CombatResolver.Level8.cs` | Smother (Lurker, Trapper), Brain Extraction, Ghost touch/fear, Vampire powers, Mind Blast, demon gates, golem haste | Vampire gaseous form, sunlight, holy symbol |
| Pit Fiend, Storm Giant, Stone Golem | `CombatResolver.Level9.cs` | Gate, symbol of pain, tail constriction; 8d8 lightning + up to 3 call lightnings; slow every other round | Stone to flesh on Stone Golem |
| Beholder, Iron Golem, Lich, Orcus, Demogorgon, arch-devils | `CombatResolver.Level10.cs` | Eye rays; poison breath every 7th round; fear and paralysing touch; gates; Wand of Orcus (house-rule save at -4); gazes, tail drain, tentacle rot; fear/chill gazes, once-per-day symbols, summons | Lich spellcasting, at-will spell lists, (un)holy word, Asmodeus rod |
| Green Slime | `CombatResolver.GreenSlime.cs`, `GreenSlimeAttachment` | Drops on a victim (50%/round); converts in 1-4 rounds, victim set to Lost (no resurrection). Cure Disease, fire or cold kill it; weapons and other spells do nothing. | Wood/metal corrosion (plate armor in 3 rounds); scraping off with items |
| Titans, Lesser/Minor Titan, Nycadaemon | Level9/10 JSON | Stats only | Abilities, Will-o-Wisp spell filtering |

## Art lookup

`Adnd.Game\MonsterArt.cs` searches `Assets\Monsters\Level1..10` and the root for the monster name (spaced, camel-case, slug, with and without commas). Known mismatches are file names that differ from the JSON name (for example `GreenDragon.png` for "Dragon, Green").

## Known issues to check

- Illusionist level 7 spell folder appears to contain Magic-User level 1 spells.
- Second Vampire entry in `Level10.json` is untouched.
- Duplicate "Will O' Wisp" Wizardry entry.

Level 9 vampires should have level 5 cleric spells.
Level 10 vampires should have level 6 magic-user spells
