# Dual-class Test Matrix

Focused checks for human dual-classing behavior.

## 1) Eligibility pass/fail

- Human, valid original prime requisites (>=15), valid target prime requisites (>=17), valid alignment:
  - `TryStartDualClass` returns `Success = true`.
- Non-human character:
  - `Success = false`, reason says only humans can dual-class.
- Original class prime requisites below 15:
  - `Success = false`, reason references original class prime requisite.
- Target class prime requisites below 17:
  - `Success = false`, reason references target class prime requisite.
- Alignment-invalid target class (e.g., Paladin non-LawfulGood):
  - `Success = false`, reason references alignment.
- Already dual-classed character:
  - `Success = false`, reason references already dual-classed.

## 2) Switch execution

- On success:
  - `IsDualClassed = true`
  - `DualClassOriginalClass` set to previous primary class
  - `DualClassOriginalLevel` set to previous class level
  - `DualClass` set to chosen target class
  - `DualClassState = TrainingNewClass`
  - `Classes` contains only target class
  - target class progression starts at Level 1 / XP 0
  - HP values remain unchanged

## 3) XP blocked/unblocked transitions

- In `TrainingNewClass`, trigger old-class action (spell/turn undead/backstab/assassinate/trap inspect-disarm), then finish adventure rewards:
  - awarded XP becomes 0
  - reward log contains dual-class XP block note.
- In `TrainingNewClass`, no old-class actions used:
  - XP awarded normally.
- After leveling new class above old class level (`SurpassedOriginal`):
  - old-class action no longer blocks XP.

## 4) Surpass threshold state change

- If new class level equals original level:
  - state remains `TrainingNewClass`.
- If new class level becomes greater than original level:
  - state transitions to `SurpassedOriginal`.

## 5) Persistence / migration safety

- Load old character JSON without dual-class fields:
  - dual-class runtime fields normalize to safe defaults (`None/false/0`).
- Load dual-class character JSON with partial/missing dual fields:
  - normalization repairs state safely and clears adventure-scoped usage flag.

## 6) Training Grounds UI flow

- `U) Dual-class` option visible.
- Choosing character + target class then confirming with `Y`:
  - invokes backend `TryStartDualClass` and saves on success.
- Confirmation text includes both standardized warnings:
  - "No old-class progression."
  - "Using old-class functions blocks adventure XP until new class level surpasses old class level."
