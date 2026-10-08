# Этап 8 — SQLite на время разработки

## Запуск

Из корня `D:\projects\project-g`:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Content.Server
```

PostgreSQL и Docker не нужны. Development использует SQLite: `.data/project-g-development.db`.
Миграция schema/model v1 выполняется до открытия UDP. WAL + FULL commits сохраняют подтверждённое состояние;
БД, WAL/SHM и lock-файлы исключены из Git. Пересборка `bin`/`.artifacts` их не удаляет.
При другом cwd задайте абсолютный `Persistence__SqlitePath`, иначе будет выбран другой файл.
Не удаляйте `.data` для обычного reconnect и не меняйте provider/path без переноса данных.

Staging/Production пока не запускают Development-вход. Конфигурация `Persistence:MaxSessions` — 32, допустимо 1..64.
Никакого автоматического fallback при ошибке БД нет.

## Два клиента

Перезапустите Godot-клиенты на протоколе v9.
Для независимых персонажей задайте user arguments: `--identity=alice` / `--identity=bob`.
В командной строке Godot user arguments идут после `--`, например `... -- --identity=alice`.
Из editor можно задать `DevelopmentIdentityProfile` на NetworkClient или аргументы запуска.

Токен находится в Godot user data: `user://development-identities/<profile>.token`.
Не удаляйте файл для обычного reconnect: удаление создаёт новую Development identity, не восстанавливает старую.
Не публикуйте токен. Один профиль не может быть online дважды; после disconnect дождитесь финального сохранения.

## Приёмка

1. Alice и Bob входят, видят друг друга и перемещаются. Двигайте Alice, потратьте ману и получите урон.
2. Отключите Alice, подключите тем же профилем: позиция/HP/мана сохраняются, runtime ID может измениться.
3. Перезапустите сервер: тот же профиль восстанавливает того же персонажа из SQLite.
4. Второй одновременный вход Alice получает CharacterInUse; Bob продолжает играть.
5. Неверный существующий токен получает InvalidIdentity; клиент не заменяет его новым.
6. При сбое БД сервер прекращает симуляцию; состояние без commit не подтверждается.

Базовые статы сохраняются, UI распределения — отдельный этап.
Offline время уменьшает cooldown, но не восстанавливает HP/ману. HP=0 сохраняется; respawn не добавлен.
Прерванные casts/effects/routes не восстанавливаются; оплаченная мана/cooldown не возвращаются.
NPC и босс перезапускаются вместе с ареной: их persistence пока нет.

## Автотесты

```powershell
dotnet build Game.slnx --artifacts-path .artifacts/stage8 -m:1 -p:NuGetAudit=false
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage8 --no-build
```

Тесты используют **реальный серверный SQLite store** и отдельные файлы в output `sqlite-tests/<uuid>/characters.db`;
пользовательская `.data` не затрагивается. Проверяются модели, durable rows, transactional rollback/replay,
reconnect/restart, duplicate login, HP=0/мана/cooldown, failure/shutdown и отсутствие публикации до commit.
Crash-тест запускает настоящий сервер на изолированном порту/файле, завершает только свой дочерний процесс
и проверяет восстановление подтверждённых координат/маны после нового запуска.
SQLite ADO.NET I/O синхронный ([документация](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async));
сервер выполняет его на worker, не блокируя network polling. NuGet-аудит в приведённой сборке отключён.

## PostgreSQL позже / дополнительная проверка

Адаптер PostgreSQL сохранён как будущий вариант ([Npgsql async I/O](https://www.npgsql.org/doc/basic-usage.html)).
Для явного Development запуска с ним нужны `Persistence__Provider=Postgres` и `Persistence__ConnectionString`.
Учётные данные вводите через скрытый ввод, не сохраняйте в tracked config или shell history.
Переключение provider не переносит SQLite-персонажей: экспорт/import и cutover будут отдельным шагом.
SQLite-тесты не проверяют PostgreSQL миграцию/advisory locks; для них нужен отдельный live DB прогон.
