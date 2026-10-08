# ADR 0009 — отдельный Content.Database

Статус: принято пользователем («для бд сделал отдельно Content.Database»).

## Решение

`Content.Database` — библиотека .NET 10, а не новый процесс/service. В ней находятся SQLite/PostgreSQL adapters,
соединения, SQL, embedded migrations, ownership leases, revision fencing и атомарные batch commits.
Она не зависит от Server, Shared, Client или Godot. `Content.Server` ссылается на эту библиотеку.

Сервер хранит и проверяет игровые модели: CharacterState, SavedInventory и content references.
DatabaseCharacterStore адаптирует серверный ICharacterStore к ICharacterDatabase.
Database получает bounded JSON-документы, persistent GUID и expected revision; runtime network IDs туда не попадают.
Сервер увеличивает revision только после успешного commit, как на этапе 8.

## Рассмотренные варианты

1. SQL/providers внутри Server: меньше проектов, но смешивает игровую модель и инфраструктуру хранения.
2. Отдельная библиотека Database + серверный adapter: выбран по инструкции пользователя; односторонние зависимости,
   независимые migrations/providers, небольшое дополнительное преобразование документов.

Перенос игровых моделей в Database или ссылка Database → Server не нужны.

## Совместимость

Стратегия БД не меняется: SQLite на время разработки, PostgreSQL adapter сохранён, live PostgreSQL не проверялся.
Schema 2 добавляет отдельную inventory table; character JSON остаётся v1 без переписывания существующих персонажей.
Inventory model v1 содержит instances с persistent GUID, definition ID и equipped slot.
Первый вход персонажа без inventory row создаёт стартовый набор один раз, последующие входы не создают новые items.
Character + inventory записываются одной транзакцией под существующим owner/revision fence.
Неизвестные schema/model versions приводят к отказу, не сбросу данных.

Это additive migration, без удаления/замены таблицы characters. Downgrade старого сервера после migration
не поддерживается; перед обновлением сохраняйте резервную копию SQLite через её backup mechanism либо после штатной остановки сервера.

Новых network/process boundaries и изменений single-shard нет. Клиент не получает SQL, GUID instances или definition catalog.
Проверки: migration v1→v2 сохраняет identity/HP; fencing/rollback; reconnect instances; отсутствие game/Godot dependencies.
