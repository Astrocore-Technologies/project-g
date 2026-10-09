# ADR 0010 — серверная прогрессия и отдельная durable модель

Статус: реализовано для этапа 11; приёмка игрового прототипа ожидается.

## Дополнение: HUD и распределение (2026-10-09)

Protocol v27 сохраняет ID 40/41/42 и owner-only `StatPreview` (83).
`ProgressionCommand` после существующих sequence/action/statIndex/skillId/slot содержит шесть int32
только для новых actions PreviewStats (3) и AllocateStats (4), в порядке STR/AGI/VIT/INT/DEX/LUK.
Размер тела — 33 байта; старые actions по-прежнему 9 байт. Каждый вклад неотрицателен,
сумма не более 100000 и не больше свободных очков; пустой пакет разрешён только для предпросмотра.
Предпросмотр: sequence uint32, tick uint32, 17 double текущих и 17 double будущих показателей
в порядке `CharacterStat` (последний — BlockDamage); 280 байт без message ID, только конечные значения.
Команды, результаты и предпросмотр — ReliableOrdered, только владельцу. Совместимый клиент/сервер
требуют совместного обновления; формулы и скрытые данные клиенту не передаются.

Предпросмотр не меняет персонажа, не отменяет канал взаимодействия и не требует сохранения.
Применение использует существующие ownership/sequence/rate/alive/busy проверки, пересчитывает текущую
экипировку и атомарно списывает сумму. Текущие HP/мана и cooldown не восстанавливаются.
Используется существующий publish-after-commit барьер; схема базы и модели сохранений не меняются.
Это предпросмотр, не блокировка экипировки: итог рассчитывается по состоянию на момент применения.

Клиент хранит отдельный черновик и версию запроса, отбрасывает устаревшие ответы,
обновляет предпросмотр при смене экипировки/состояния предметов, блокирует повторное применение,
показывает ошибки/таймаут. Закрытие окна сохраняет черновик; «Отменить» очищает его без команды записи.
HUD показывает HP/ману/опыт слева сверху, карту справа сверху, 8 слотов снизу, отдельно рывок и Эхо.
Подробности и навыки — во вкладках окна K. Визуальные ассеты пока заменены нейтральными панелями.

Проверка дополнения: `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false` — 0 ошибок,
предупреждения NuGet NU1903 (существующий SQLitePCLRaw) и NU1900 (audit endpoint).
`dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore` — 457 passed, 7 skipped.
Content validation пройдена. `Tests/UI/PlayerHudSmoke.tscn` выполнена в Godot 4.7.1 на отдельном
Development server/profile: headless и D3D12 Mobile, ресурсы, предпросмотр без мутации,
отмена, реальные GUI-клики, применение, resize 1280×720/1920×1080 и освобождение сцены.
Live PostgreSQL и копирование пользовательской development-базы — opt-in проверки, не запускались.

## Решение

### Дополнение: личное поручение (2026-10-09)

Первый delivery slice использует текущие границы, без нового сервиса, БД или quest framework.
Server загружает одну bounded definition, помещает двух статических NPC в существующий AOI,
проверяет намерения на fixed tick и хранит результат в optional `SavedProgression.DeliveryQuest`.
Receipt v1 содержит stable definition ID и Active/Completed, без session sequence/runtime entity ID.
Отсутствие поля совместимо со старыми сохранениями; explicit null, неизвестная версия/definition и
недопустимый status отвергаются. Смена stable ID требует миграции контента; старый бинарник до дополнения
не умеет читать новый receipt. SQL schema не меняется.

Выдача/списание материалов, receipt и EXP входят в один существующий fenced character checkpoint.
Reply, journal, inventory и progression публикуются после commit по ADR 0008/0009.
Повторное принятие/сдача не мутирует состояние. При путешествии receipt остаётся частью персонажа;
NPC есть только в заданном регионе, журнал доступен в обоих. Pending actions очищаются при logout/rebind.

Protocol v28 требует совместного обновления клиента и сервера. Новые ReliableOrdered сообщения:

| ID | Сообщение | Поля тела / границы |
| --- | --- | --- |
| 86 | QuestNpcSpawn | entity u64, tick u32, position 2×float, name/role: каждое ≤32 символов/96 UTF-8 bytes; ≤216 bytes |
| 87 | QuestCommand | sequence u32, action u8, NPC u64; 13 bytes; Journal требует NPC=0, другие действия NPC≠0 |
| 88 | QuestReply | sequence/tick u32, NPC u64, outcome/choices u8, speaker ≤32/96, text ≤200/600; ≤718 bytes |
| 89 | QuestJournal | owner u64, tick u32, status u8, title ≤48/144, objective ≤180/540, material ≤24/72, required/carried u16, EXP i32; ≤783 bytes |

Размеры без message ID и регионального envelope. Все парсеры проверяют хвост/усечение, enums, конечные
координаты и bounds. NPC — AOI-only, удаляется обычным PlayerDespawn. Reply/journal — owner-only,
без полного каталога/условий; журнал непринятого поручения пуст. Команды ограничены одной pending
на игрока/tick; старые sequence не исполняются. Snapshot transport не изменён.

Godot показывает серверные NPC и modal UI, отправляя только намерения. Кэш узлов, отписка событий,
очистка региональной сессии; нет глобальных world scans или новых per-frame сетевых сообщений.
Позиции остаются в существующем серверном JSON для этого малого прототипа; универсальный редактор
NPC/квестов и перенос размещения в scene-authoring pipeline здесь не вводятся.

Проверка дополнения:

- `dotnet build Game.slnx --no-restore -m:1 -p:OutputPath=D:/projects/project-g/.artifacts/quest-tests/` — 0 ошибок.
  Отдельная сборка `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` — 0 ошибок/предупреждений.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore -p:OutputPath=D:/projects/project-g/.artifacts/quest-tests/`
  — 476 passed, 7 skipped. Включены 10 новых quest tests: wire truncation/bounds, проверки намерений,
  однократность, legacy/corrupt receipt, SQLite reopen/rollback, owner-only AOI и publish-after-commit.
  Live PostgreSQL и копия пользовательской development-БД не запускались (opt-in).
- `dotnet .artifacts/quest-tests/Content.Server.dll --validate-content` — успешно.
- `Tests/UI/QuestSmoke.tscn` — headless и D3D12 Mobile на отдельном Development server/profile.
  Финальный графический прогон проверил F2 и ЛКМ по NPC, реальные GUI-кнопки принятия/сдачи,
  L/журнал, отсутствие движения и парирования через modal, reconnect и cleanup. Скриншоты проверены при 1280×720.
- `git diff --check` — успешно. Существующие предупреждения NuGet: NU1903 для SQLitePCLRaw 2.1.11,
  NU1900 при недоступном audit endpoint. Editor import сообщил об устаревшей ссылке в `MainMenu.tscn`
  на `res://_NC/App/MainMenu.cs` и shutdown-предупреждениях GodotTools; игровые smoke-прогоны прошли без этих ошибок.
  Ссылка главного меню впоследствии исправлена на `res://Scripts/App/MainMenu.cs` после переноса файлов пользователем.

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
