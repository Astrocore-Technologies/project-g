---
name: server-simulation
description: Реализация authoritative movement, combat, AI, skills, effects, NPC, region/world simulation на fixed tick.
---

# Server Simulation

## Core invariant
Server owns truth. Client sends intent.

## Workflow
1. Проверь gameplay contract.
2. Сделай simulation state независимым от LiteNetLib/Godot.
3. Не смешивай transport и domain simulation.
4. Gameplay rules выполняются на fixed server tick.
5. Validate commands до mutation.
6. Simulation выпускает state/events, networking их сериализует.

## Movement
Для click-to-move:
- client sends destination intent;
- server validates ownership/rate/destination;
- server moves entity;
- server sends authoritative snapshots;
- client может predict/reconcile.

Не принимай client final position как truth.

## Combat validation
Где применимо: ownership, alive/incap state, cooldown, resource cost, range, shape/LOS, target validity, sequence/rate, PvP/world rules.

Не доверяй client damage, hit result, RNG, cooldown completion или inventory.

## Performance
Hot tick paths: no global scans, unnecessary LINQ, transient allocations, blocking I/O.
Используй spatial relevance и lower-frequency AI где возможно.

## Testability
Domain rules должны тестироваться без Godot и real UDP socket.
