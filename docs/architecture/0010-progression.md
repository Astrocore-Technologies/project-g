# ADR 0010 — серверная прогрессия и отдельная durable модель

Статус: реализовано для этапа 11; приёмка игрового прототипа ожидается.

## Решение

Сервер начисляет EXP/освоение из подтверждённых fixed-tick событий, а открытия — при движении через
маркеры с persistent bit flags. Клиент передаёт только bounded intentions. Повтор команды не создаёт награду.
Изученные навыки, уровни освоения и восемь bar slots отделены от доступных execution profiles;
cooldown привязан к stable definition ID и сохраняется при смене слота/экипировки.

SavedProgression принадлежит Server. Database получает opaque JSON в новой таблице schema 5,
в общей owner/revision-fenced batch-транзакции. Старый character JSON v1 остаётся совместимым.
Это повторяет библиотечную границу ADR 0009 и publish-after-commit ADR 0008.
Сервер не сканирует idle игроков для начисления EXP; работает с moving set, событиями и pending intentions.

## Альтернативы

Встроить поля в character JSON потребовало бы смены его модели и миграции существующего strict документа.
Отдельная модель выбрана для additive migration без переписывания HP/identity/инвентаря/Эхо.
Клиентские счётчики и grant RPC отвергнуты: они не подтверждают результат и позволяют повторные награды.

## Последствия

Protocol v14, schema v5, progression model v1. Откат к серверу schema v4 не поддерживается.
SQLite используется для executable тестов; PostgreSQL SQL/provider собирается, live PostgreSQL не проверяется.
Временный баланс и источники наград описаны в docs/design/progression-prototype.md,
постоянные правила hidden professions, death penalty и boss contribution этим ADR не определяются.
