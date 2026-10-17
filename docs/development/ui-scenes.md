# Редактирование меню и HUD

Главное меню и основной HUD используют рабочие `.tscn`-сцены. Данные персонажа остаются серверными; автономные стенды используют отдельные тестовые значения.

Для переработки HUD и внутренних окон пользователь утвердил четыре концепта из `art-source/ui/rework-2026-10-17/`. Требования, текущий результат и оставшаяся работа — [утверждённый визуальный эталон](../design/ui-rework-2026-10-17.md). Прежние проверки ниже относятся к первому срезу; новый срез и его MCP-проверки описаны в конце документа.

## Сцены и настройки оформления

| Файл в Content.Client | Что редактировать |
| --- | --- |
| `Scenes/App/MainMenu.tscn` | Точка входа, игровая сцена и слой меню |
| `UI/Screens/MainMenuView.tscn` | Название, подпись, кнопки, отступы и область статуса подключения |
| `UI/Screens/Settings.tscn` | Компоновка окна настроек |
| `UI/Backgrounds/LinvelMenuBackdrop.tscn` | Фон, кадрирование и градиент читаемости |
| `UI/Theme/menu-theme.tres` | Шрифты, цвета, размеры и состояния элементов меню |
| `UI/Screens/PlayerHudView.tscn` | Размещение блока игрока, панели навыков и панели контроля |
| `UI/Components/PlayerResources.tscn` | HP, мана, выносливость, опыт и кнопка персонажа |
| `UI/Components/SkillSlot.tscn` | Общий вид одного слота навыка |
| `UI/Theme/hud-theme.tres` | Рамки и типографика остальных элементов HUD |
| `UI/Theme/player-resources.tres` | Шрифт, плоские заливки и тёмные треки HP, маны и выносливости |
| `Assets/UI/Hud/` | Тонкие векторные планки/оправа, фронтальный портрет, символы ресурсов и фигурная маска |

Имена узлов с Unique Name задают привязку к скриптам. При изменении имени обновить соответствующую кэшируемую ссылку. Постоянные узлы создаются сценами; C# заполняет значения, таймеры и динамические строки эффектов.

`MainMenu.cs` управляет текущей игровой сессией. `MainMenuView.cs` управляет только представлением и сообщает действия. Нельзя переносить подключение к серверу в `_Ready()` view.

Громкость и полноэкранный режим сохраняются в `user://client-settings.cfg`. Настройки работают до входа в мир. Применение громкости немедленное; сохранение выполняется при возврате из настроек, смене режима и завершении клиента. Неизвестные/неподходящие значения не используются вместо текущих настроек.

Фон первого среза — статичная иллюстрация. На ней нет запечённых кнопок и надписей. При входе в мир весь слой меню скрывается и не рендерится. Живой 3D-фон, музыкальная тема и анимированные варианты остаются следующей художественной работой. Происхождение изображения и запрос: `art-source/ui/linvel-menu-background.md`.

После повторного уточнения пользователя компонент ресурсов переделан по образцу `art-source/ui/rework-2026-10-17/05-player-resources-reference.png`: фронтальный портрет в тонкой фигурной оправе перекрывает три тёмные планки; у каждого ресурса цветной символ, подпись, плоская заливка и видимые текущие/максимальные значения справа. Двойной массивный круг и глянцевые капсулы удалены. Опыт остаётся тонкой полосой у навыков; боевой статус и подсказки защиты располагаются возле навыков. Нейтральная строка «Мирный | тег 0» скрыта; активный PvP, боевой тег, смерть и подбор показываются ситуационно.

Портрет задаётся у игрока: открыть `Scenes/PlayerAvatar.tscn`, выбрать корень и назначить `Hud Portrait` в Inspector. `WorldController` передаёт его в HUD локального игрока; `PlayerHud.SetPortrait(texture)` позволяет заменить изображение при обновлении внешности. HUD не назначает всем персонажам собственную общую картинку или иконку человека. Круглое отсечение и оправа остаются в `PlayerResources.tscn`, нажатие портрета открывает окно персонажа. Сейчас в сцене игрока назначена временная иллюстрация по пользовательскому образцу; она не меняет прототипную капсулу в мире и не является системой создания внешности. Автономный `HudPreview.tscn` задаёт свой `Preview Portrait`. Происхождение и запрос: `art-source/ui/player-resources.md`.

## Автономный предпросмотр

В Godot открыть сцену и запустить F6:

- `Tests/UI/MainMenuPreview.tscn` — та же рабочая сцена меню, вход и отмена моделируются без сети.
- `Tests/UI/HudPreview.tscn` — та же рабочая сцена HUD с явно изолированными fixture-значениями.

Данные предпросмотра находятся только в `Tests/UI/UiScenePreview.cs`. Настройки предпросмотра сохраняются отдельно в `.artifacts/ui-scenes/preview-settings.cfg`, не затрагивая настройки игрового клиента.

Автоматический режим этих сцен: `-- --smoke`. Флаг `--capture` добавляет скриншоты меню, настроек или HUD для двух разрешений в `.artifacts/ui-scenes/`. Для снимков нужен настоящий renderer; headless проверяет только поведение и компоновку.

## Проверка в игре

F5 запускает `Scenes/App/MainMenu.tscn`. Проверить настройки, вход, отмену, ошибку недоступного сервера и повтор. В мире ресурсы располагаются сверху слева, навыки снизу, миникарта справа под навигацией. Нажатие кнопки персонажа открывает существующий экран K. Отслеживаемое поручение располагается ниже блока игрока.

Плашка с лозунгом в игровой HUD не добавляется. Её изображение из раннего концепта не является игровым UI-ресурсом.

Полезные существующие проверки:

```powershell
dotnet build Game.slnx
& $godotPath --headless --path Content.Client res://Tests/UI/MainMenuPreview.tscn -- --smoke
& $godotPath --headless --path Content.Client res://Tests/UI/HudPreview.tscn -- --smoke
& $godotPath --headless --path Content.Client res://Tests/App/MainMenuSmoke.tscn -- --server-port=<свободный-порт> --identity=<тестовый-профиль>
```

Для `MainMenuSmoke -- --live` и `Tests/UI/PlayerHudSmoke.tscn` запускать отдельный Development-сервер с временной SQLite БД и уникальным профилем. Рабочая БД для проверки не нужна. Клиент и сервер должны быть собраны из одной версии сетевого контракта.

## Проверено 17 октября 2026 года

- `dotnet build Game.slnx --no-restore -m:1 -p:OutDir=D:/projects/project-g/.artifacts/ui-scenes/build/` — успешно, 0 ошибок. Обычный вывод сервера занят работающим процессом, поэтому для полной сборки использован отдельный каталог. Сохраняются прежние NU1903 для SQLitePCLRaw и NU1900 при недоступном NuGet audit.
- `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` — успешно, 0 предупреждений и ошибок; клиентская сборка доступна Godot.
- `MainMenuPreview -- --smoke`, `HudPreview -- --smoke` — успешно, геометрия, фокус, локальное сохранение настроек, pending-состояние, 1280×720 и 1920×1080.
- Эти же сцены запущены с настоящим Vulkan Forward Mobile renderer; скриншоты меню, настроек и HUD осмотрены. После проверки из настроек убран лишний вертикальный скролл, PvP-статус размещён ниже блока ресурсов, пустая рамка трекера скрывается.
- `MainMenuSmoke` без сервера — успешно: нет автоматического подключения, защита двойного входа, timeout, очистка и повторная попытка.
- `MainMenuSmoke -- --live` на отдельном сервере/временной БД — успешно: вход, готовность мира, принудительный обрыв, возврат, переподключение и сохранение позиции.
- `PlayerHudSmoke` с реальным Mobile renderer — успешно: серверные ресурсы, черновик характеристик, отмена, применение, resize и cleanup.
- 18 целевых .NET-тестов — успешно: `WelcomeRejoinTests`, handshake, `PvpIntegrationTests.CombatDisconnect`, `IdentityProtocolTests`, round-trip `ServerWelcome`. Команда: `dotnet test .artifacts/ui-scenes/build/Content.Tests.dll --filter 'FullyQualifiedName~WelcomeRejoinTests|FullyQualifiedName~Handshake|FullyQualifiedName~PvpIntegrationTests.CombatDisconnect|FullyQualifiedName~IdentityProtocolTests|FullyQualifiedName~NetworkProtocolTests.ServerWelcome'`.
- Прочитаны внешние зависимости восьми новых сцен/ресурсов, отсутствующих ссылок нет. Проверены UTF-8 и whitespace новых исходников/документов, `git diff --check` затронутых tracked-файлов.

В интеграционной проверке исправлена прежняя ошибка повторного входа: сохранённая disconnected-сессия повторно отправляла первично выданный development-token. Теперь `ServerWelcome` выдаёт его только клиенту без существующего credential. IDs, layout, порядок полей, delivery method и версия протокола этим исправлением не меняются; проверка клиента не ослаблена. Новая проверка начинает с новой личности и повторно подключает её к тому же retained actor.

Логи и TRX находятся в `.artifacts/ui-scenes/`. Снимки реальной игры — `.artifacts/hud-1280.png` и `.artifacts/hud-1920.png`. UI-проверки завершились без runtime errors; при загрузке существующих регионов сохраняется предупреждение о deprecated `instance_reset_physics_interpolation()`. Headless editor import также выводит предупреждения о ресурсах при завершении редакторских плагинов.

### Проверка исправленного оформления ресурсов

- `dotnet build Game.slnx --no-restore -m:1 -p:OutDir=D:/projects/project-g/.artifacts/hud-refinement/build/` — 0 ошибок, прежние четыре NuGet warnings.
- `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` — 0 ошибок и предупреждений.
- `HudPreview -- --smoke --capture` на Vulkan Forward Mobile — успешно; проверены расположение и реальные кадры на 1280×720 и 1920×1080, включая частичное заполнение трёх ресурсов.
- `PlayerHudSmoke` с отдельным сервером/БД и реальным Mobile renderer — успешно: серверные ресурсы, изменение характеристик, resize и cleanup. Повторная проверка `MainMenuSmoke -- --live` также успешна.
- Игровые снимки осмотрены и сопоставлены с пользовательским образцом. Ошибок shader/resource/runtime в игровых логах нет; прежние editor-import shutdown warnings и предупреждение о physics interpolation сохраняются.

Логи исправления: `.artifacts/hud-refinement/`. Значения и источники данных не менялись; протокол, Server и Shared этим исправлением оформления не затронуты.

### Аватар локального игрока

`PlayerAvatar.HudPortrait` передаётся в HUD при создании локального игрока; текстура больше не зашита в UI-компонент. `dotnet build Game.slnx --no-restore -m:1 -p:OutDir=D:/projects/project-g/.artifacts/hud-refinement/build/` завершён с 0 ошибками и прежними четырьмя NuGet warnings. Финальная обычная сборка клиента — 0 ошибок и предупреждений. `HudPreview -- --smoke --capture` проверен на Mobile renderer при двух разрешениях. `PlayerHudSmoke` на отдельном сервере/БД подтверждает соответствие портрета локальному игроку, отсутствие стандартной иконки человека, открытие характеристик реальным нажатием на портрет, отмену/применение характеристик и resize. Повторный `MainMenuSmoke -- --live` успешен. В тесте после отмены черновика добавлено ожидание layout перед следующим нажатием. Финальный `player-hud-live.log` содержит `PLAYER_HUD_OK: character portrait/click`; игровые кадры — `.artifacts/hud-1280.png` и `.artifacts/hud-1920.png`.

## Рабочий срез редизайна 17 октября

- Компоновка основных окон: `UI/Screens/{Character,Inventory,WorldMap,Journal,Guild,Echo,Dialogue,Profession,PauseMenu}View.tscn`.
- Оболочка, отступы, заголовок и закрытие: `UI/Components/WindowShell.tscn`; её оформляет `UI/Theme/rework-theme.tres` и `Assets/UI/Hud/window-frame.svg`.
- Иллюстрация / реальная 3D-модель и слоты: `UI/Components/CharacterDisplay.tscn`, `PlayerAvatar.CharacterPortrait`, `PlayerAvatar.AppearanceScene`. Рисунок не меняет серверную экипировку.
- Локальная карта: `UI/Components/Minimap.tscn`; `StarterMap.RadiusMeters` задаёт охват, `UiMinimapView` — частоту/камеру; большая карта имеет отдельный view.
- Навигация и NPC-подсказка: `UI/Components/HudNavigation.tscn`, `InteractionPrompt.tscn`.
- Новый арт и привязки: `UI/Theme/rework-art.tres`, `Assets/UI/Icons/Rework/painted-icons.png`, `Assets/UI/Portraits/traveler-fullbody.png`.

Для сценарной проверки через MCP сначала подними отдельный development server/SQLite и запиши port/identity/serverPid в `.artifacts/ui-redesign/live.json`. Затем запускай `Tests/UI/UiReworkSmoke.tscn`, `UiReworkHudSmoke.tscn`, `UiReworkInventorySmoke.tscn`. Эти адаптеры используют те же runtime-сцены, реальные подтверждения и отдельные identities. `UiReworkLive.tscn` служит ручной MCP-проверке (`ShowScreen`, `CloseScreen`, `SetReviewSize`). Без review config обычные `UiScreensSmoke`, `PlayerHudSmoke` и `UiInventorySmoke` принимают прежние CLI-параметры.

Результаты, артефакты и явно не выполненные сценарии перечислены в `docs/design/ui-rework-2026-10-17.md`; новый срез не означает завершение художественного переноса всех вспомогательных окон.

### Hover навигации HUD

Шесть кнопок `HudNavigation.tscn` используют отдельный `HudNavButton` из `rework-theme.tres`: одинаковые пустые StyleBox и отступы во всех состояниях, золотистый icon_hover_color, более тёмный pressed. Прямоугольная заливка и нижняя линия удалены; icon_max_width остаётся 25, hover не сжимает рисунок. Иконки назначены в самой сцене для автономного просмотра; runtime catalog сохраняется. Проверено запуском этой рабочей сцены через MCP, реальным mouse hover и press; границы Character до/после — 42×34 UI px, hover подтверждён is_hovered(). Кадр: `.artifacts/ui-redesign/nav-hover-fixed.png`. C# и игровые команды не менялись.

### Магическая защита в обзоре персонажа

В `CharacterView.tscn` добавлена строка «Маг. защита» под физической; `ProgressionPresentation` берёт значение из `StatPreview.Current[6]`, как вкладка производных характеристик. Серверные расчёты и протокол не менялись. Сборка решения с прежним OutDir — 0 ошибок, 4 прежних NuGet warnings; обычная сборка клиента — 0 ошибок/предупреждений. Через Godot MCP на отдельном сервере подтверждено совпадение overview «15» с derived «Маг. защита: 15», проверены границы новой строки и кнопки распределения; фактический кадр — `.artifacts/ui-redesign/character-magic-defense-fixed.png`.

### Повторная переработка ресурсов по уточнённому концепту

`PlayerResources.tscn` — 468×124 UI px; подписи, символы, значения и полосы редактируются отдельно. Тема использует StyleBoxFlat; `portrait-mask.svg` определяет маску, `portrait-rim.svg` — тонкую оправу. `PlayerAvatar.HudPortrait` и `HudPreview.PreviewPortrait` ссылаются на новый `traveler-portrait-front.png`. Создан встроенным image_gen; prompt/источник сохранены в `art-source/ui/rework-2026-10-17/hud-resource-refinement.json`. Это только иллюстрация HUD, мировой аватар не заменён.

`PvpStatus.tscn` задаёт сдержанную ситуационную строку; `PvpPresentation` скрывает её для живого мирного персонажа без тега/подбора, сохраняя показ опасных состояний. Серверные расчёты, команды, протокол и persistence не менялись.

Проверено:
- `dotnet build Game.slnx --no-restore -m:1 -p:OutDir=D:/projects/project-g/.artifacts/hud-concept-correction/build/` — 0 ошибок, четыре прежних NuGet warnings NU1903/NU1900. Обычная сборка клиента — 0 ошибок и предупреждений.
- Godot MCP: автономный `HudPreview` с частичными значениями 82/100, 64/100, 75/100; видимые числа и плоские треки осмотрены. После добавления новых изображений выполнен filesystem rescan/import.
- `UiReworkHudSmoke` с отдельным сервером/SQLite → `PLAYER_HUD_OK`: локальный портрет, открытие по клику, ресурсы, server preview/cancel/apply, resize и cleanup. Лог — `.artifacts/hud-concept-correction/mcp-hud-result.json`.
- `UiReworkLive` на 1280×720 и 1920×1080: реальные значения 110/110, 140/140, 100/100; все три Label видимы, текст помещается, нейтральный PvpStatus скрыт. Кадры и вырезанный непосредственно из viewport фрагмент — `.artifacts/hud-concept-correction/`. Фрагмент `hud-detail.png` не является сгенерированным макетом.
- Фактический кадр сопоставлен с сохранённым пользовательским образцом. Художественная интерпретация лица/орнамента остаётся отличимой от исходного рисунка; композиция, плоские ресурсы и видимые значения реализованы в рабочей сцене.
