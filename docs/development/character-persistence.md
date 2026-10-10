# Этап 8 — SQLite на время разработки

## Запуск

Из корня `D:\projects\project-g`:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Content.Server
```

Сохранённые изменения сцен регионов экспортируются автоматически перед запуском мира.
Если изменений нет, экспорт пропускается. Ошибки карты останавливают запуск с диагностикой;
отдельно выполнять `Export-Regions.ps1` перед игрой не требуется.
Подробности: [экспорт регионов](region-export.md).

PostgreSQL и Docker не нужны. Development использует SQLite: `.data/project-g-development.db`.
Миграция schema v4 выполняется до открытия UDP; character, inventory и Echo model v1 хранятся отдельно.
SQL/providers/migrations находятся в `Content.Database`, игровые модели и adapter — в `Content.Server/Persistence`.
WAL + FULL commits сохраняют подтверждённое состояние;
БД, WAL/SHM и lock-файлы исключены из Git. Пересборка `bin`/`.artifacts` их не удаляет.
При другом cwd задайте абсолютный `Persistence__SqlitePath`, иначе будет выбран другой файл.
Не удаляйте `.data` для обычного reconnect и не меняйте provider/path без переноса данных.

Staging/Production пока не запускают Development-вход. Конфигурация `Persistence:MaxSessions` — 32, допустимо 1..64.
Никакого автоматического fallback при ошибке БД нет.

## Два клиента

Перезапустите сервер и Godot-клиенты на протоколе v13.
Для независимых персонажей задайте user arguments: `--identity=alice` / `--identity=bob`.
В командной строке Godot user arguments идут после `--`, например `... -- --identity=alice`.
Из editor можно задать `DevelopmentIdentityProfile` на NetworkClient или аргументы запуска.

Токен находится в Godot user data: `user://development-identities/<profile>.token`.
Не удаляйте файл для обычного reconnect: удаление создаёт новую Development identity, не восстанавливает старую.
Не публикуйте токен. Один профиль не может быть online дважды; после disconnect дождитесь финального сохранения.

## Development: оживить персонажа

При HP=0 откройте `I` и нажмите «Оживить (Development)». Сервер восстанавливает текущий Max HP на месте,
сохраняет его в SQLite и только затем рассылает CombatState. Мана, экипировка и cooldown не сбрасываются.
Живого персонажа кнопка не лечит. Это тестовый инструмент, не окончательная gameplay-механика respawn.
Кнопка отображается по серверной capability; запрос разрешён только после входа, своему персонажу,
с loopback-адреса и при IHostEnvironment=Development. Staging/Production/missing environment запрещают команду.
Полезная нагрузка ReliableOrdered: ID 33 — uint32 ненулевой sequence; ID 34 — byte CanRevive (0/1).
Будущие gameplay respawn/death penalties не определяются этим инструментом; модель БД не меняется.

## Приёмка

1. Alice и Bob входят, видят друг друга и перемещаются. Двигайте Alice, потратьте ману и получите урон.
2. Отключите Alice, подключите тем же профилем: позиция/HP/мана сохраняются, runtime ID может измениться.
3. Перезапустите сервер: тот же профиль восстанавливает того же персонажа из SQLite.
4. Второй одновременный вход Alice получает CharacterInUse; Bob продолжает играть.
5. Неверный существующий токен получает InvalidIdentity; клиент не заменяет его новым.
6. При сбое БД сервер прекращает симуляцию; состояние без commit не подтверждается.

Базовые статы сохраняются, UI распределения — отдельный этап.
Этап 9.1: `I` — инвентарь, «Надеть»/«Снять» — подтверждаемая сервером экипировка.
Меч и броня выдаются один раз, включая первый вход старого персонажа после additive migration.
Перед обновлением сделайте backup БД при штатно остановленном сервере; не удаляйте её для получения предметов.
Этап 9.2: две коробки X=-5/Z=1 и X=-5/Z=4; подойти, курсор на предмет, `G` — ручной подбор.
Claim и inventory сохраняются атомарно; забранный тестовый предмет не появляется снова после рестарта.
Offline время уменьшает cooldown, но не восстанавливает HP/ману. HP=0 сохраняется; respawn не добавлен.
Прерванные casts/effects/routes не восстанавливаются; оплаченная мана/cooldown не возвращаются.
NPC и босс перезапускаются вместе с ареной: их persistence пока нет.
Этап 10: Мира следует за персонажем и помогает в бою; T — ручная Signature в точку под курсором.
UUID, слот, координаты и cooldown Эхо сохраняются без повторной выдачи. См. `docs/design/first-echo-prototype.md`.

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
