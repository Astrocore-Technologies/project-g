---
name: architecture-decision
description: Оценка и документирование фундаментального архитектурного изменения Project G до реализации.
---

# Architecture Decision

## Trigger
Используй перед сменой engine/runtime, network library/model, DB strategy, service/process boundaries, authority, region/world distribution, persistence/deployment architecture.

## Workflow
1. Сформулируй problem.
2. Зафиксируй constraints из `AGENTS.md` и gameplay contract.
3. Дай минимум 2 реалистичных option.
4. Сравни complexity, ops, scaling, latency, testability, migration cost, mobile effect, failure modes.
5. Дай recommendation.
6. Назови труднообратимые последствия.
7. Получи approval пользователя, если меняется accepted foundation.
8. После approval создай/обнови ADR.

## ADR template
```md
# ADR XXXX: Title

## Status
Proposed / Accepted / Superseded

## Context

## Decision

## Alternatives considered

## Consequences

## Migration / rollout

## Deferred
```

Не реализуй fundamental replacement до approval.
