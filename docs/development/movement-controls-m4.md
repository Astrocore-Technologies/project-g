# Movement & Controls — M4

Дата: 2026-10-18. Follow и переназначение PC input реализованы; пользовательская
приёмка отдельно от автоматических проверок. Mobile input исключён. Следующий
этап по плану — совмещённая PC-приёмка M5; падения и динамические объекты остаются
M7/M8 и требуют оставшихся gameplay-решений.

## Поведение и архитектура

Follow запускается пунктом «Следовать» в контекстном меню видимого игрока.
По умолчанию меню открывается ПКМ по модели. ПКМ по земле сохраняет движение;
меню само не отправляет движение и не отменяет предыдущую навигацию. Отдельной
кнопке Follow можно назначить клавишу: она выбирает игрока под курсором.

Сервер проверяет владельца команды, sequence/input order, alive/state, цель,
AOI/LOS и расстояние ≤40 м. Нужны видимый игрок и тот же регион; согласие цели
или членство в группе не требуются. Следование — обычный бег через существующий
`NavigationMover`/pathfinder, без атак, навыков, телепортации или копирования
особых перемещений цели. Хранятся только активные связи follower→target и
обратный индекс; нет поиска последователей полным обходом мира.

Уточнения M005–M007 пользователя:

- Расстояния считаются по прямой в 3D, включая высоту. Остановка на 3 м требует
  доступного прямого прохода: соседний этаж или уступ не заменяют маршрут.
- Потеря LOS/AOI, удаление >40 м или недоступный путь запускают восстановление
  на 5 секунд. Повторные неудачные поиски не сбрасывают таймер.
- Без видимости идти можно только к последней видимой точке. Текущая скрытая
  позиция не становится серверной навигационной целью, передаваемой клиенту.

При восстановлении видимости/дальности/пути Follow продолжается. Иначе сервер
останавливает его с понятной причиной владельцу. Ручная успешная команда движения,
Stop и фактическое начало своего действия прекращают Follow. Preview и отказ
боевого действия его не отменяют. Получение урона/PvP сами по себе не отменяют;
смерть, disconnect и смена региона прекращают связь. Follow не сохраняется в БД.

Перепланирование не чаще 0,25 с; смещение цели <0,2 м не вызывает лишний поиск,
если прежний маршрут ещё исполняется. Это технические параметры в
`Content.Server/World/ServerWorld.Follow.cs`. Частота шага остаётся 20 TPS.
Обычная точность удержания 3 м ограничена stop distance, шагом и сетевой задержкой;
это не изменение gameplay-значения 3 м.

## Переназначение

`PcInput` — единственная граница физических клавиш/мыши для gameplay. Именованные
Godot actions `pc_*` устанавливаются из локального профиля. Presenters используют
action IDs; удержание, отпускание, модификаторы и release после смены модификатора
обрабатываются общими helpers. В сцене проекта автозагрузка `PcInputRouter`.

Доступны **42 действия**: движение, Basic/Manual, 8 навыков, Dodge/Quick Recover,
Block, Parry, Cancel, Stop, Follow, контекстное меню, zoom, 3 команды Echo,
взаимодействия и игровые окна. Поддерживаются физические клавиши, кнопки мыши,
колесо для разовых действий и Ctrl/Shift/Alt. Колесо для удерживаемых действий
отклоняется. Новое назначение занятой кнопки отклоняется с названием действия;
действующая привязка сохраняется. Удаление и общий reset доступны явно.

Сохранён нынешний прототип боя: ЛКМ — одиночный клик/удержание для Continuous,
Ctrl+ЛКМ — явный Manual. Отдельные Stop и Follow по умолчанию не назначены.
Прежний конфликт G устранён: G подбирает предмет, Ctrl+G открывает гильдию
авантюристов. Это обратимые настройки PC-профиля, не новый постоянный gameplay rule.
Move/контекстное меню и Cancel/меню могут иметь общую кнопку благодаря контексту;
остальные одинаковые назначения запрещены. UI Escape остаётся навигацией интерфейса.

Профиль — `user://settings/input.cfg`, независимо от персонажа/БД. Камера и
display/audio используют прежние отдельные настройки. При уходе фокуса,
открытии modal и захвате новой кнопки игровые press не проходят; release/cancel
завершают удержание. Hotbar и основные подсказки показывают текущие назначения.

## Рабочие сцены и настройка

- `Content.Client/UI/Screens/InputBindingsView.tscn` — одна рабочая вкладка
  управления в главном меню и в игре. Список использует редактируемую
  `UI/Components/InputBindingRow.tscn`; C# создаёт только повторяющиеся строки.
- `UI/Screens/Settings.tscn` — общие вкладки «Экран и звук» / «Управление».
  `InGameSettingsView.tscn` наследует её для встраивания в игровой window shell.
  Открытие в игре: меню → «Настройки и управление».
- `UI/Screens/PlayerContextView.tscn` — меню игрока и уведомления Follow.
- Названия/default bindings — `Scripts/Input/PcInput.cs`, формат профиля и
  конфликты там же; события/подписи связывает `InputBindingsPanel`.
- Компоновка, размеры строк, темы и отступы меняются в `.tscn`/существующей Theme.
  Реальные игровые значения не заменяются данными preview.

## Протокол v77

Breaking addition требует пересборки/перезапуска клиента и сервера. Старый peer
отклоняется прежней проверкой protocol version. Snapshot v76 не расширялся.

| ID | Сообщение | Порядок полей после UInt16 message ID | Размер с ID | Delivery |
| --- | --- | --- | --- | --- |
| 124 | FollowCommand | UInt32 sequence, clientTick, inputOrder; UInt64 target runtime ID | 22 bytes | ReliableOrdered |
| 125 | FollowState | UInt32 sequence, serverTick, inputOrder; UInt64 target; byte phase, reason | 24 bytes | ReliableOrdered, только владелец |

Cancel использует существующий надёжный MoveCommand/Stop. Follow разделяет
movement sequence, ingress rate/time guard и общий input order с боем. В одном
fixed tick принимается актуальная навигационная команда; более старый order
не вытесняет новый Follow. Все readers отвергают truncated/trailing packets,
нулевые обязательные поля и неизвестные enum. Persistent IDs не передаются.

Клиент не посылает положения цели или поток Follow destinations. Он получает
обычные authoritative snapshots и предсказывает подтверждённый маршрут;
подтверждения удаляют старые кадры replay. Отказ Follow сохраняет прежнюю
серверную навигацию. Поздний accepted state не возвращает отменённое намерение.

## Проверки

Сборка решения, 0 warnings / 0 errors:

```powershell
dotnet build Game.slnx --artifacts-path .artifacts/movement-controls-m4/build --no-restore -m:1 -p:NuGetAudit=false
```

Первое создание outputs выполнялось с restore из локального NuGet cache.
Последние client-only исправления отдельно проверены сборкой
`Content.Client/Project-G.csproj` с теми же флагами.

Целевые **14/14 passed**: Follow domain + malformed/round-trip protocol + два
UDP-клиента с 100–150 мс / 10% потерь. Проверены 3 м, 3D/этажи/40 м,
таймер без продления, последняя видимая точка, возврат цели, ручная/боевая отмена,
отказ навыка, damage/death/disconnect, выгрузка региона с причиной TargetLeft,
owner-only state, Stop/отложенный Start.

```powershell
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m4/build --no-build --no-restore --filter 'FullyQualifiedName~FollowTests|FullyQualifiedName~FollowProtocolTests|FullyQualifiedName~FollowIntegrationTests' --logger 'trx;LogFileName=m4-follow-final.trx' --results-directory .artifacts/movement-controls-m4
```

Общая регрессия: **1975 passed / 1 skipped / 0 failed**, 1976 tests, 7 мин 9 с.
Последовательный xUnit runner задавался временно только в изолированном test
output и убран после проверки. Исключены Postgres/WorkingDatabase; один
DevelopmentCopy test пропущен без внешней dev-БД. Рабочая БД не проверялась.
Общая регрессия запускалась до последних правок причины выгрузки региона и
проверки handshake/delivery для FollowState. После этих правок повторно прошли
сборка, все 14 целевых tests и реальный Godot/UDP Follow smoke ниже.

```powershell
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m4/build --no-build --no-restore --filter 'FullyQualifiedName!~Postgres&FullyQualifiedName!~WorkingDatabase' --logger 'trx;LogFileName=m4-regression.trx' --results-directory .artifacts/movement-controls-m4
```

Godot 4.7.1 Mono:

- `Tests/UI/InputBindingsSmoke.tscn`, headless и Vulkan/Forward Mobile:
  42 actions, key/mouse/modifiers, конфликты, ограничение колеса, сохранение/reset,
  отпускание модификатора, Escape при capture и рабочая сцена settings.
- Реальный рендер рабочей вкладки в прежней тёмной теме с золотыми акцентами:
  `.artifacts/movement-controls-m4/bindings-final.png`. Логический viewport
  1280×720; финальный кадр снят после стабилизации layout. Последний прогон
  использует отдельную тестовую конфигурацию с теми же исходниками/assembly и
  отключённым MCP autoload, не меняя основной проект или общий редактор.
- `Tests/Navigation/FollowLiveSmoke.tscn`, реальная Буковая долина, отдельный
  сервер/порт/SQLite, два клиента, 60–90 мс / 3% потерь: контекстное меню без
  movement, owner Follow, отсутствие movement heartbeat, prediction drift <0,5 м,
  история <20 кадров, переназначенный Stop и input isolation настоящих settings.
  Финальные логи: `.artifacts/movement-controls-m4/live-886099f1b8e04075a6323d638a1de882`.

Есть прежнее Terrain3D deprecation warning `instance_reset_physics_interpolation`.
Нет проверки production capacity, реального handoff между процессами, Mobile,
падений, динамических опор и пользовательской визуальной приёмки.

## Приёмка

Пересобрать и перезапустить обе стороны (v77). Двумя клиентами войти в один
регион. ПКМ по второму игроку → «Следовать»; поводить цель, скрыть за стеной,
вернуть до/после 5 секунд. Проверить дистанцию, смерть/выход и ручную отмену.
В меню → настройки → управление переназначить движение, навык и Stop, проверить
конфликт, отпускание над UI, повторный вход и reset. Рабочий сервер/данные
автоматическими стендами не останавливались и не сбрасывались.
