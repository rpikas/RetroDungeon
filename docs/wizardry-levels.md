# Wizardry level guide

Places of interest per dungeon level, as implemented. Squares are written E/N (east/north). In the code a square is `Point(X, Y)` = (N, E); the tables are in `Adnd.Game\MazeForm.DungeonData.cs` and the scripts in `MazeForm.LevelScripts.cs`.

How the engine models doors, secret doors and one-way doors is in `implementation-notes.md`.

## Level 1

| Square (E/N) | What |
|---|---|
| 0E/0N | Entrance stairs (exit to town) |
| 0E/10N | Stairs down to Level 2 |
| 10E/8N | Elevator to Levels 2-4 |
| 13E/3N | Bronze statue: "Will you search?" gives Key of Bronze |
| 13E/18N | Silver statue: "Will you search?" gives Key of Silver |
| 13E/5N | Gem encrusted statue (Murphy's shrine): Murphy's Ghost attacks |
| 9E/12N | Sign "AREA OUT OF BOUNDS!" |
| 9E/19N | Wizard room: the wizard teleports the party back to the castle |

## Level 2

| Square (E/N) | What |
|---|---|
| 12E/7N | Stairs up to Level 1 |
| 8E/12N | Silver smog: only passable with the Key of Silver |
| 8E/7N | Bronze smog: only passable with the Key of Bronze |
| 9E/18N | Statue of Bear: "Will you search?" gives it |
| 12E/4N | Statue of Frog: "Will you search?" gives it |
| 4E/11N | Indestructible door: needs the Statue of Bear |
| 4E/12N | Indestructible door: needs the Statue of Frog |
| 4E/16N | Bronze statue: "Will you search?" gives the Key of Gold |

## Level 3

| Square (E/N) | What |
|---|---|
| 15E/11N | Stairs up to Level 2 |
| 1E/8N | Stairs down to Level 4 |
| 5E/19N, 15E/19N, 0E/11N, 5E/11N, 0E/7N | Pits (traps) |

## Level 4

Three areas. The stairs area is reached from Level 3; the training grounds are reached only by the Level 1 elevator.

| Square (E/N) | What |
|---|---|
| 10E/18N | Stairs up to Level 3 |
| 17E/12N | Image of a bear: blocks the way without the Statue of Bear |
| 17E/7N | Stairs down to Level 5 |
| 10E/8N | Elevator from Level 1 |
| 10E/0N | Private express elevator (the door sign is at 10E/2N) |
| 10E/14N | Testing Ground Control Center |
| 12E/15N | Monster Allocation Center |
| 8E/15N | Treasure Depository |

## Levels 5-7

The stairs follow the map pictures in `Assets\Dungeon`, not the coordinates in some online lists.

| Level | Square (E/N) | What |
|---|---|---|
| 5 | 0E/0N | Stairs up to Level 4 |
| 5 | 8E/8N | Stairs down to Level 6 |
| 5 | 10E/0N | Private express elevator |
| 6 | 8E/16N | Stairs down to Level 7 |
| 6 | 10E/0N | Private express elevator |
| 7 | 10E/10N | Stairs up to Level 6 |
| 7 | 18E/9N | Stairs down to Level 8 |
| 7 | 10E/0N | Private express elevator |

## Levels 8-10

| Level | Square (E/N) | What |
|---|---|---|
| 8 | 10E/0N | Private express elevator (no stairs) |
| 9 | 10E/0N | Private express elevator |
| 9 | 8E/2N | Chute down to Level 10 |
| 10 | 0E/10N, 0E/19N, 10E/19N, 10E/11N, 19E/13N, 14E/11N | Teleporters from guard-rooms to the next hallway |
| 10 | 17E/3N | Forced encounter with Werdna |

Not implemented: the non-magic area on Level 5 (entrance 13E/6N), and a real Malor spell to leave Level 10.
