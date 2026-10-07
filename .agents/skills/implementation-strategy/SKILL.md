---
name: implementation-strategy
description: Планирование любой нетривиальной Project G feature/refactor перед реализацией. Использовать при multi-file изменениях, сетевых последствиях, persistence, performance или пересечении Client/Server/Shared.
---

# Implementation Strategy

## Workflow
1. Прочитай `AGENTS.md`.
2. Прочитай relevant section `docs/design/gameplay-contract.md`.
3. Прочитай relevant ADR.
4. Изучи только затронутые файлы.
5. Определи Client / Server / Shared / Persistence / Data / Tests boundaries.
6. Назови authoritative owner поведения.
7. Определи protocol, persistence, performance и mobile последствия.
8. Выбери smallest vertical slice.
9. Определи verification до редактирования.

## Перед реализацией кратко зафиксируй
- Goal.
- Files/areas touched.
- Client/Server/Shared split.
- Protocol impact.
- Persistence impact.
- Performance/mobile risks.
- Tests/verification.
- Open design question, если есть.

## Stop conditions
Не реализуй без решения пользователя, если требуется unresolved permanent gameplay rule, новый fundamental service boundary, irreversible migration, смена authority или accepted technology.

Prototype assumptions помечай явно и не превращай в stable architecture.
