# GDD 03 — C5c: торговля, повторяемая поставка и «Дозор лагеря»

Дата: 2026-10-20. Первый набор прямо утверждён пользователем. Баланс временный.
Реализованы реальные server producers, сохранение и owner UI; это не завершение
всего GDD 03 или будущих каталогов квестов/событий. Приёмка пользователем впереди.

## Что работает

- NPC-скупщик: 1 личный EXP за каждую действительно проданную руду. Прежняя
  цена в монетах сохранена. Quote показывает EXP до подтверждения; сам quote
  не платит. Списание руды, монеты, EXP и economy operation receipt — одна запись.
  Retry и reconnect не умножают награду. P2P/рынок EXP пока не дают: отдельные
  антиабуз-правила ещё не выбраны. Скрытого ограничения EXP/час нет.
- «Древесина для лагеря»: принять у Лады, доставить Орену собственные 3 древесины,
  получить 20 личного EXP. Номер каждого нового заказа растёт. Повтор старого
  номера не расходует груз нового заказа. Бесплатных материалов, срока,
  штрафа и cooldown нет. Прежнее одноразовое поручение отдельно и сохранено.
- «Дозор лагеря»: авторизованный оператор запускает подготовленное событие
  в явном регионе. Следующая допустимая победа над обычным Monster завершает
  запуск, добавляет общий пул 20 EXP к обычным 20 EXP и использует тот же
  C5a/b допуск, равное деление, cap и дроби героя/экземпляра Эхо. Манекен,
  босс, присутствие, environmental kill без допустимых получателей и ручное
  завершение старых world operations бонус не выдают. Если получателей нет,
  событие ждёт следующей допустимой победы. После завершения можно запустить
  следующее; cooldown пока нет. Величина/версия бонуса фиксируются при запуске.

Все операции выполняются на fixed tick. Клиент отправляет намерение, не число
награды. Проверяются ownership, sequence, состояние, NPC, дистанция/LOS,
запас материалов и номер заказа. Результат публикуется после durability barrier.

## Настройки и размещение

| Что редактировать | Источник |
| --- | --- |
| 1 EXP/руду, 20 EXP «Дозора», текущие PvE/exploration | `Content.Server/Data/experience-rewards.json`, revision 2 |
| 3 древесины/20 EXP, публичные тексты, region, точки | `Content.Server/Data/camp-services.json` |
| Расположение в Godot | `Content.Client/Scenes/Regions/Linvel/Services/StartingCampServices.tscn` |
| Старые рецепты, цены, исходное поручение | `Content.Server/Data/prototype.json` |
| Блок заказа/события | `Content.Client/UI/Components/ActivityOrderView.tscn` |
| Рабочие окна | `Content.Client/UI/Screens/DialogueView.tscn`, `JournalView.tscn` |

На актуальных региональных картах старый crafting/economy/quest был выключен.
Теперь существующие сервисы включены в стартовом лагере `linvel_gardens`, отдельно
от города и legacy prototype. Верстак (-416,14.12,230), Лада (-418,14.11,234),
Орен (-400,14.11,234), дерево (-414,14.14,224), руда (-409,14.13,228).
Definitions проверяют ссылки и walkable floor до открытия UDP. Клиентские
ресурсные узлы и верстак получают настоящую высоту, а не Y=0.

Экспортирует placement существующий catalog, сохраняя вручную заданные тексты,
материал, количество и награду; сцену не перезаписывает:

```powershell
& '<Godot_console.exe>' --headless --path Content.Client --script res://Tools/Services/ExportCampServices.gd -- --catalog=D:/projects/project-g/Content.Server/Data/camp-services.json --output=D:/projects/project-g/Content.Server/Data/camp-services.json
```

`Activities:Enabled` по умолчанию true; отдельный catalog задаётся
`Activities:ServicesPath`. Балансные условия активной поставки нельзя менять
под прежней revision. Несовместимая revision сохранённого заказа отклоняется
с требованием явной миграции, без скрытого списания или переоценки.

Live-DM по умолчанию выключен. Используется прежняя same-process console,
`LiveDm:Enabled` и deployment SHA-256 hashes в `LiveDm:Operators`; секрет не
хранится в content или репозитории. Формат подготовленной операции:
`actor|credential|StartCampWatch|reason|region_id`. Нужен явный регион.
Очередь bounded; строка/секрет в журнал не выводятся. Произвольного EXP grant,
нового admin endpoint или исполнения скриптов нет.

## Совместимость и сохранение

Protocol **v82**: IDs 132/133/134 для ActivityCommand/State/Result, редкие
ReliableOrdered owner-only сообщения. Высоты resource/station и EXP quote
расширяют старые DTO. Strict bounds, enums, truncation/trailing и packet budget
проверяются. Клиент и сервер обновляются вместе; snapshots не меняют delivery.

Optional versioned `SavedProgression.DeliveryOrder` хранит последний номер и
статус; `SavedWorldNode.CampWatch` — последний запуск, active, reward revision
и pool. Документы bounded, неизвестные версии/некорректные поля отклоняются.
Cargo, reward, receipt, region state и audit используют прежний атомарный
checkpoint. SQL schema **12** сохранена, новая таблица/миграция не требовалась.
Рабочая `.data` не открывалась. Старый бинарник не читает новые JSON поля:
rollback требует сохранённой совместимой копии БД.

## Проверки

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  — успешно. После preview/UI правок также собран клиент.
- Targeted `dotnet test ... --filter 'FullyQualifiedName~CampActivity|FullyQualifiedName~ActivityProtocolTests|FullyQualifiedName~WorldNodeTests|FullyQualifiedName~EconomyTests|FullyQualifiedName~QuestTests'`
  — **30 passed / 0 failed / 0 skipped**, `.artifacts/character-stats/c5c-target.trx`.
  Проверены replay, неверный NPC/дистанция/состояние/груз/flood, SQLite stale fence
  и reconnect, owner-only UDP с удержанием commit, frozen event после restart,
  равные доли героя/Эхо, corrupt receipts и malformed wire.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content --RegionExports:VerifySources=false --RegionExports:AutoExport=false`
  — успешно: schema25, balance19, 8 weapons, 18 abilities, 5 creatures.
- Godot 4.7.1, Mobile/Vulkan, RTX 4060 Laptop: реальный GUI/UDP/SQLite сценарий
  `CampActivitiesSmoke.tscn` завершён с `CAMP_ACTIVITIES_SMOKE_OK`. Приняты оба
  независимых поручения, сдана новая поставка, взят следующий номер без выдачи
  древесины, собрана/продана руда после quote, обычный бой завершил «Дозор».
  EXP 0 → 20 → 21 → 61. Проверены высоты, journal, Escape и 1280/1920.
  Итоговые логи и снимки: `.artifacts/character-stats/c5c-ui-9a9c1f6efd29481ca1df8637116c7490/`.
  В журнале скрыта декоративная иллюстрация, чтобы новый блок оставлял место
  старому описанию и его прокрутке; при 1280 проверена высота scroll не менее 80 px.
- Offline `CampActivitiesPreview.tscn` использует ту же рабочую DialogueView и
  ActivityOrderView, без сервера. `CAMP_ACTIVITIES_PREVIEW_OK`; осмотрены доступный,
  активный и завершённый состояния при 1280/1920. Для capture передать
  `-- --capture-dir=<absolute-directory>`; без аргумента preview интерактивный.
  Preview-снимки: `.artifacts/character-stats/c5c-ui-7558849593e5432caf0e71b1bb26899f/`.
- Полная последовательная регрессия `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore`
  с временным output-only `xunit.runner.json` (`parallelizeTestCollections:false`):
  **2082 passed / 0 failed / 7 skipped / 2089 total**, 7 мин 33 с.
  `.artifacts/character-stats/c5c-regression.trx`; временный runner config удалён.
  Пропуски: 6 live PostgreSQL без тестового подключения и 1 fixture старой
  Development-базы. `git diff --check` — успешно.

В workspace отсутствуют исходные `GardenValleys.tscn` и `FoxHills.tscn` из
параллельной работы над картами. Их не восстанавливали. Server validation
использует опубликованные surface exports с отключённой сверкой source hashes;
полный графический сценарий проверялся на отдельной fixture `linvel_beech`
с перенесёнными сервисами/обычным NPC и отдельной SQLite, не на отсутствующей
сцене Gardens. Исходные городские сцены и ассеты не правились.

Существуют прежние NU1903 SQLitePCLRaw 2.1.11, NU1900 недоступного NuGet audit
и Godot physics interpolation deprecation. Live PostgreSQL здесь не запускался.
Пользовательские игровые процессы/рабочая БД не изменялись; commit/push нет.

## Дальше

Обновление после checkpoint C5c, 2026-10-20: пользователь отменил награду
очками характеристик за достижения. Прежний C6 снят с реализации; другая
награда будет выбрана позже. Следующий абзац — исторический план до отмены,
актуальное решение: [C6 отмена очков](character-stats-c6-cancelled.md).

C6 требует CS10: конкретных shared/world_unique достижений, условий, очков,
повторяемости и выбора победителя. Каталог не придуман. P2P/рыночный EXP,
дальнейшие квесты/события и финальный темп прокачки остаются за пределами этого
первого утверждённого набора; C7 баланс/инструменты также не объявлен готовым.
