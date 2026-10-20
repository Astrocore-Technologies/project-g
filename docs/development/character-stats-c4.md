# Character & Stats C4 — полный сброс характеристик

Дата: 2026-10-20. Срез C4 реализован; пользовательская приёмка впереди.
Рабочая БД не открывалась и не мигрировалась. C5–C7 этим срезом не закрываются.

## Поведение и границы

Прямой ответ пользователя: **100 монет временно** за второй и последующие
полные сбросы. Первый успешный сброс на персонаже бесплатен. Это тестовая цена,
не финальная калибровка под 15–30 минут заработка из §4.0 GDD 03.

Услуга доступна у явно настроенного трактирщика, живым персонажем вне In Combat.
Сервер проверяет loaded/ownership, sequence и rate, service/NPC, 3D-дистанцию и LOS,
состояние действия/движения/обмена. Произвольной команды «сбросить где угодно» нет.
Игрок получает расчёт, видит цену, итоговый пул, текущие/будущие первичные и
производные показатели, предупреждение и явное подтверждение. Отмена расчёта
ничего не меняет. Пустой сброс не расходует бесплатное использование.

Вложенные очки возвращаются в общий свободный пул. StartingStats, earned points,
achievement bonus и ранее свободные очки сохраняются. В основном player content
StartingStats — по 10. Уровень, EXP, профессия, навыки и предметы не меняются.
Доли текущих HP/MP/Stamina сохраняются через C2 equipment/resource transition;
cooldown и ACR не сбрасываются. Дополнительного cooldown услуги нет: можно сразу
перераспределить очки обычным окном персонажа.

Сброс использует только существующие правила совместимости: в текущих item/skill
definitions нет stat requirements. Ограничения оружия и слотов не снимаются.
Ввод будущих stat requirements потребует их обычной enforcement policy и списка
предупреждений в quote. Tournament Roster Lock пока отсутствует; всегда разрешающей
заглушки нет, поддержка турниров не заявляется. Альтернативный платёж предметом
отложен по GDD полной модели, alpha использует монеты.

## Сервер, данные и сохранения

`Content.Server/World/ServerWorld.Respec.cs` — очередь намерений и owner-bound
quote на 15 с. Quote хранит исходный ledger/build, progression configuration,
equipment/wallet image, текущие и ожидаемые derived stats и service revision.
При подтверждении всё пересчитывается/сверяется. Изменение билда, кошелька,
снаряжения либо истёкшее предложение требует обновить расчёт. Регенерация и
движение времени не фиксируют текущий запас ресурсов в quote.

`Content.Server/Items/InventorySimulation.Respec.cs` заранее готовит новый
equipment/resource profile и кошелёк до mutation. Reset, debit, allocation ledger,
ресурсы, receipt и audit сохраняются существующим fenced atomic checkpoint.
Снимки и Accepted публикуются после commit; отказ checkpoint останавливает
симуляцию без публикации частичного сброса.

Optional `SavedRespec` v1 внутри SavedProgression v1 хранит Completed ordinal,
последний QuoteId, фактический платёж и возвращённые очки. Operation выдаёт сервер
как следующий ordinal, клиент только подтверждает quote. Повтор последней пары
operation/QuoteId возвращает AlreadyProcessed, старые операции отвергаются.
Receipt сохраняется при reconnect и regional travel; предложения сбрасываются
вместе с сетевой сессией. Reset не использует watermark других economy операций.

До C4 этой услуги не существовало: для персонажа с достоверным C1 allocation ledger
отсутствующий receipt означает отсутствие успешного сброса. Старый персонаж без
ledger или с несовместимым starting profile получает MigrationRequired; вложения
по сумме статов не угадываются. Невалидный receipt не подменяется бесплатным.
SQL schema 12, CharacterState v2, SavedProgression v1 и Allocation v1 сохранены.
Respec — additive optional model; необратимой миграции нет.

Настройки: `Content.Server/Data/respec.json` (version/revision, PaidCoins=100,
QuoteSeconds=15, stable tavern IDs). Ссылки, бюджеты и возможность оплаты при
текущем coin limit проверяются при загрузке. Override: `--Respec:ServicePath=...`;
для отдельных старых стендов без услуги `--Respec:Enabled=false`.

**Размещение временное:** используется NPC стартовой таверны C3 в лагере gardens,
пока городская таверна не включена в server graph. Это prototype городской услуги,
не объявление лагеря постоянным поселением. Основная городская сцена не менялась.
Постоянное размещение остаётся за работой над картой; переносу tavern binding
по-прежнему нужна явная content migration C3.

## Protocol v80

Старый v79 отвергается handshake. Новые сообщения редкие ReliableOrdered;
quote/result отправляются только владельцу. Частые snapshots не менялись.

| ID | Сообщение | Layout тела |
| --- | --- | --- |
| 129 | RespecCommand | 29 B: sequence u32, action u8, runtime NPC u64, QuoteId u64, Operation u64 |
| 130 | RespecQuote | 549 B: sequence/tick, NPC/QuoteId/Operation, price/coins/returned/available int32, TTL float32, firstFree bool, 7+7 primary doubles, 24+24 derived doubles |
| 131 | RespecResult | 25 B: sequence/tick, operation u64, outcome u8, paid/returned int32 |

Порядок primary: STR/AGI/VIT/INT/DEX/LUK/WIS; derived — существующий CharacterStat.
Самый большой пакет с type и regional envelope — 561 B, меньше MTU 1200.
Reader проверяет точную длину, enum, finite, bounds и согласованность free/price/ordinal;
trailing, truncated, oversized и NaN packets отвергаются. Сервер не принимает цену,
возвращаемые очки, результаты или координаты от клиента.

## Рабочие сцены и проверка игроком

- `Content.Client/UI/Screens/RespecView.tscn` — редактируемые summary, comparison scroll,
  предупреждение, checkbox и кнопки. Тема — существующая rework-theme.
- `Scripts/Progression/RespecPresentation.cs` — transport/pending/expiry и привязка
  данных, а `Scripts/UI/RespecView.cs` отвечает за отображение.
- `Tests/UI/RespecPreview.tscn` — автономный preview той же рабочей View без мира,
  сети, identity и базы. `Tests/UI/RespecSmoke.tscn` — actual live route, требует
  отдельный сервер: `--server-port=<port> --capture-dir=<existing-directory>`.

В игре: распределить хотя бы одно очко → F2 у трактирщика → «Сброс характеристик…»
→ проверить расчёт (список производных прокручивается) → checkbox → подтвердить.
После результата «Распределить очки» открывает обычное окно персонажа.
Второй расчёт показывает 100 монет; при нехватке подтверждение недоступно.

## Фактические проверки

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  — успешно, 0 ошибок. Проверки tests после финальных дополнений также собраны.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Respec|FullyQualifiedName~StatAllocationTests|FullyQualifiedName~Tavern'`
  — **49 passed / 0 failed / 0 skipped**; `.artifacts/character-stats/c4-target-final.trx`.
- Проверены полный refund с achievement bonus, first-free/reconnect/paid/replay,
  отсутствие нового cooldown, сохранение HP/MP/Stamina fractions и cooldowns,
  stale/expired/forged quote, flood, dead/combat/distance/legacy/empty/no funds.
  UDP+SQLite проверяют owner-only quote/result и commit barrier как бесплатного,
  так и платного сброса. Искусственный отказ записи сохраняет прежние деньги,
  билд и право бесплатного использования; успешного ответа нет.
- Content validation с `--RegionExports:VerifySources=false` проходит:
  schema 25 / balance 19 / 8 weapons / 18 abilities / 5 creatures; respec catalog
  и tavern references проверены. Обычная проверка исходных карт сохраняет внешнее
  ограничение отсутствующего GardenValleys.tscn; рабочие карты не восстанавливались.
- Godot 4.7.1 Vulkan/Mobile, RTX 4060: `RESPEC_PREVIEW_OK` и `RESPEC_SMOKE_OK`.
  Реальные F2/клики/checkbox/cancel/confirm/redistribution, wheel scroll, Escape,
  1280×720 и 1920×1080. Осмотрены фактические кадры: gold frame/serif/dark skin,
  русские строки без clipping, читаемые free/paid/error states.
  Артефакты: `.artifacts/character-stats/c4-ui-fcd37e666d454bf18c78986920f69cf5/`.
  Сервер, SQLite и identity отдельные, серверные бинарники скопированы в run directory.
  Read-only проверка SQLite после UI-сценария: Completed=1, PaidCoins=0,
  ReturnedPoints=3, после нового распределения StatPoints=16, VIT investment=2.

Полная последовательная регрессия: **2048 passed / 0 failed / 7 skipped / 2055 total**,
7 мин 12 с. Команда: `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=c4-regression-final.trx' --results-directory .artifacts/character-stats`.
Артефакт: `.artifacts/character-stats/c4-regression-final.trx`. Parallel collections
отключались только временным output-only `xunit.runner.json`, удалённым в `finally`.
Пропущены 6 opt-in PostgreSQL tests и 1 working development DB copy test без fixtures.
Известные NU1903 (SQLitePCLRaw 2.1.11), NU1900 и Godot interpolation deprecation
остаются предупреждениями прежнего окружения. PostgreSQL/working-copy opt-in fixtures
не заявляются проверенными без запуска. Пользовательская игровая приёмка впереди.
