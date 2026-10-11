# Сцены перед созданием Линвеля

Очистка 2026-10-11 выполнена через Godot MCP. `Content.Client/Scenes` содержит игровые сцены и их переиспользуемые части; стенды и демонстрационные карты находятся в `Content.Client/Tests`.

| Место | Назначение |
| --- | --- |
| `Scenes/App/MainMenu.tscn` | Старт F5, вход в сетевую игру |
| `Scenes/World.tscn`, `PlayerAvatar.tscn` | Игровая сессия и игрок |
| `Scenes/Actors/Primitives/` | Капсулы игроков, NPC, мобов, босса и общие анимации |
| `Scenes/Actors/SwordAttackRig.tscn`, `TrainingDummy.tscn` | Боевая презентация и тренировочный манекен |
| `Scenes/Blockout/` | Дом и дерево, используемые действующей картой пристани |
| `Scenes/Regions/RiverLanding/`, `RiverCity/` | Действующие карты, привязанные к серверному каталогу и экспорту |
| `Scenes/Regions/TerrainTest/`, `DungeonTest/` | Подключённые к игре регионы для высот и общих подземелий |

У старых Prototype/Outskirts из `Scenes/Regions` были только offline ссылки. Их каркас GameRoot и сами карты перенесены в `Tests/Regions/Fixtures`; это отдельные образцы, не текущие игровые регионы с такими же IDs.

Перенесённые стенды:

- `Tests/App/MainMenuSmoke.tscn` и его скрипт;
- `Tests/Navigation/NavigationStudy.tscn` и его скрипт;
- `Tests/Animation/CharacterAnimationGallery.tscn` — прежняя скелетная библиотека;
- `Tests/Actors/CapsuleActorGallery.tscn` — актуальные капсулы;
- `Tests/Regions/Fixtures/GameRoot.tscn`, `Prototype.tscn`, `Outskirts.tscn`.

Устаревший `Scenes/Actors/SwordTrainer.tscn` удалён после замены единственной ссылки в preview RiverCity на `TrainerFigure.tscn`. Публичный marker, его UUID и положение сохранены. Его preview отключает звук. Навигация, баланс, протокол и рабочая БД не изменяются; source hashes пакета обновляются штатным экспортом.

Оригиналы очищенных файлов сохранены в `.artifacts/archive/scenes-cleanup-20261011/`. Запросы MCP — `.artifacts/godot-mcp-setup/scene-cleanup-request.json`, ответы — `scene-cleanup-report.json`.

Для будущей лорной карты использовать собственную папку `Scenes/Regions/Linvel/`. Подключение к каталогу, экспорт и переход с действующих карт выполнять вместе с её реализацией, чтобы F5 и существующие персонажи оставались работоспособными.

## Проверка

- `dotnet build Game.slnx --no-restore -m:1`: 0 ошибок; прежние 3 предупреждения NuGet (SQLitePCLRaw 2.1.11 и недоступный audit BalanceStudio).
- Штатный `tools/Export-Regions.ps1 -NoBuild` с Godot .NET console: `REGION_EXPORT_PUBLISHED`. Сравнение Regions до/после: геометрия, anchors и bindings полностью совпали.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~RegionExportTests`: 19/19.
- Godot headless: `RegionAuthoringSmoke`, перенесённый `MainMenuSmoke` в offline режиме на отдельном порту 19052, `RiverCitySmoke`, перенесённый `NavigationStudy` с выводом в отдельный artifact, `CapsuleActorSmoke`, перенесённый `CharacterAnimationGallery --smoke`: все exit 0. Предупреждений UID, ошибок и resource leaks в этих логах нет.
- Аудит 52 scene/resource-файлов в Scenes/Tests: все 92 внешние ссылки существуют. В runtime-коде и инструментах не осталось ссылок на удалённые пути.
- Меню запущено и остановлено через MCP; runtime-журнал без ошибок и invalid UID. При служебном reimport кэша UID редактор вывел сообщения о progress dialog из deferred-вызова; они относятся к редакторскому импорту, standalone-проверки прошли без них.

В Scenes осталось 19 `.tscn`; из папки убраны 8 сцен (7 перенесены, 1 удалена). Логи и TRX — `.artifacts/scene-cleanup-verification/`. Текущая серверная БД и баланс не менялись.
