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

A two-handed item requires both Main Hand and Off Hand to be empty before equipping
it, and then occupies both hands. Unequip it before equipping another item in either
hand; equipping a replacement does not remove existing hand equipment automatically.
Shop purchases and treasure retain this property, and older saved weapons recover
missing two-handed flags from the item catalogue when characters are loaded.

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
