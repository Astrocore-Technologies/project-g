---
name: network-protocol-change
description: Безопасное добавление/изменение LiteNetLib wire messages, packet layouts, protocol IDs, delivery methods, handshake behavior и serialization в Project G.
---

# Network Protocol Change

## Перед изменением
Проверь `Content.Shared/Network/`, relevant client/server sender/receiver и protocol tests.

Определи тип сообщения:
- handshake/control;
- gameplay command;
- frequent snapshot/state;
- critical reliable event.

## Rules
- Stable numeric message IDs; не reuse ID с новым смыслом.
- Packet size bounded.
- Validate available bytes before read.
- Exact layouts должны reject truncated/trailing garbage где уместно.
- No Godot types in wire contracts.
- No server-only secrets.
- Prefer compact numeric IDs.
- Sequence/tick fields там, где важен порядок.
- `DeliveryMethod` выбирается осознанно.

Обычно:
- handshake/inventory/progression/critical events → reliable;
- frequent movement snapshots → unreliable/sequenced где подходит.

Не превращай весь gameplay stream в ReliableOrdered.

## Versioning
Breaking change → protocol bump или явный compatibility path. Не переинтерпретируй старые bytes молча.

## Tests
- round-trip;
- truncated packet;
- oversized packet;
- invalid enum/ID;
- trailing data;
- negative server validation для untrusted commands.

## Handoff
Укажи message IDs, fields order, delivery method, compatibility effect и реально запущенные tests.
