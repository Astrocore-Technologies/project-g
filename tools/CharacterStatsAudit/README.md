# Character & Stats audit

Offline analysis through the authoritative server calculators. Does not start
a server, open saved characters, edit content, or raise the alpha cap.

```powershell
dotnet run --project tools/CharacterStatsAudit -- D:/projects/project-g D:/projects/project-g/.artifacts/character-stats/new-audit
dotnet run --project tools/CharacterStatsAudit -- D:/projects/project-g D:/projects/project-g/.artifacts/character-stats/reproduced D:/projects/project-g/.artifacts/character-stats/new-audit
```

Output must be a new directory. The optional last argument selects exported
inputs, preserving reproducibility after source edits. `cases.json` holds all
fixtures: levels, allocation weights, gear, signed modifiers, reference weapons,
target and experimental A/B. Each input and JSON/CSV report is kept together.

TTK is target HP divided by expected stationary DPS, not a discrete fight.
Budgets count upfront uses from full capacity without regeneration, cooldowns
or channel drain. HP prices are absolute. EHP ignores regen, evasion and active
defense. Gear applies only listed item modifiers at full condition. Stress
levels are arithmetic only. Activity counts are not hours or permission to
repeat discoveries/one-time quests. Read the report's limitations before using
it for balance decisions.

GUI: BalanceStudio → «Статы / EXP», production scene
`tools/BalanceStudio/CharacterAuditWindow.tscn`.
Full evidence: [C7 report](../../docs/development/character-stats-c7.md).
