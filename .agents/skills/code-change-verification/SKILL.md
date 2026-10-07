---
name: code-change-verification
description: Проверка значимых Project G code changes перед handoff: build, targeted tests, protocol/server validation и Godot checks.
---

# Code Change Verification

## Standard
Запусти:
`dotnet build Game.slnx`

Затем smallest relevant tests.

Examples:
- network change → `Content.Tests/Shared` + relevant `Content.Tests/Server`;
- server-domain → unit tests + negative validation;
- handshake/transport → integration test;
- Godot change → build + affected scene launch если доступно.

## Review checklist
- authority не утекла в client;
- no Godot dependency in Server/Shared;
- no hidden data in Client/Shared;
- no unrelated changes;
- no generated `.uid`/import noise;
- no protocol ID collision;
- no unbounded parsing;
- no accidental ReliableOrdered for high-frequency state;
- no blocking I/O in fixed tick;
- no obvious per-tick global scans/allocations.

## Report
Не говори “tests pass”, если их не запускал.
Укажи exact commands, results, что не запускалось, почему и residual risk.
