# Godot MCP в Project G

Подключено и проверено 2026-10-11 по запросу пользователя.

- Редактор: Godot 4.7.1 .NET, проект `D:/projects/project-g/Content.Client`.
- Уже включённый addon: `addons/godot_mcp_toolkit`, версия 1.0.3.
- Локальный мост: `@npgamedev/godot-mcp-server@1.0.3`, Node.js 24.14.0.
- Codex: сервер `godot` добавлен в `C:/Users/Yuri/.codex/config.toml`; перед изменением сохранена копия `config.toml.before-godot-mcp-*.bak`.

Мост установлен в `.artifacts/tools/godot-mcp-server`. Codex запускает его напрямую через `C:/Program Files/nodejs/node.exe`, с `GODOT_MCP_PROJECT_PATH=D:/projects/project-g/Content.Client` и `GODOT_MCP_CONFIG_VERSION=1`. Проект выбран явно; порт определяется из реестра toolkit. При проверке использовался существующий редактор и его localhost-порт 6550.

## Обычная работа

Открыть `Content.Client/project.godot` в Godot .NET и оставить Godot MCP Toolkit включённым. После изменения конфигурации переподключить MCP в Codex; обновлённая конфигурация также применяется при новом запуске клиента. Механизм конфигурации описан в [официальной документации OpenAI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

Инструменты предназначены для чтения и изменения сцен в живом редакторе. Чтение: `scene_get_tree`, `node_get_property`, `script_read`, `editor_get_console`. Дополнительные группы включаются через `discover_tools`, например `animation_authoring`, `3d_tools`, `runtime_advanced` и `editor_advanced`.

Сцены изменять инструментами MCP, сохранять после согласованной группы изменений. Перед запуском проверки читать консоль; C#-изменения дополнительно собирать .NET. Для проверки положения, материалов и анимаций нужен визуальный запуск, а не только headless-чтение.

Локальные инструкции инструмента: [Companion Skill](../../Content.Client/addons/godot_mcp_toolkit/CompanionSkills/godot-mcp-toolkit/SKILL.md). Документация и MIT-лицензия уже находятся внутри addon; npm-мост также содержит свою лицензию.

## Проверка соединения

```powershell
codex mcp get godot
```

В текущем чате новые встроенные инструменты не появились автоматически после записи конфига. Поэтому соединение дополнительно установлено стандартным MCP SDK через тот же мост; это не обращение к редактору в обход MCP.

При первоначальной проверке через MCP прочитаны настройки `Project-G`, скрипты и деревья `PlayerAvatar`, `SwordTrainer`, `TrainingDummy`; тогда `AppearanceScene` указывала на `MaleCharacter.tscn`.

При реализации капсул подключение повторно проверено. Через MCP созданы и сохранены нативные сцены и AnimationLibrary, назначен `PlayerFigure.tscn`, запущен `CapsuleActorGallery.tscn`, прочитаны runtime-показатели и сохранены снимки Mobile renderer. Итоги и команды — в [плане/отчёте по персонажам](../design/primitive-actors-plan.md#фактически-выполненная-проверка).

Локальные проверочные запросы, клиент SDK и ответы сохранены в игнорируемой `.artifacts/godot-mcp-setup/`. Токены авторизации не помещаются в документацию и отчёты; их обнаруживает сам мост.

Восстановление локального npm-моста при очистке `.artifacts`:

```powershell
& 'C:/Program Files/nodejs/npm.cmd' install --prefix 'D:/projects/project-g/.artifacts/tools/godot-mcp-server' '@npgamedev/godot-mcp-server@1.0.3' --ignore-scripts --no-audit --no-fund
```

Настройки остальных MCP-подключений сохранены. Проектный `.mcp.json` существовал до этой настройки; отдельная запись Codex нужна потому, что его клиент использует TOML-конфигурацию.

`res://.mcp.json` — это `Content.Client/.mcp.json`, конфигурация запуска stdio-моста. Настройки `Project Settings → MCP Toolkit → Limits` относятся к лимитам ответов. Секция `Security & Response Limits` расположена в отдельной панели самого плагина. При текущей проверке изменение токена вручную не понадобилось: подключение восстановилось после возвращения записи редактора в реестр. Порты и путь токена не закреплены в проверочном клиенте; их обнаруживает bridge.

Сравнить конфигурации клиентов можно по [руководству toolkit](https://npgamedev.github.io/godot-mcp-server/mcp-clients/). После создания новых ресурсов использовать `editor_refresh` и `editor_wait_for_idle`, чтобы игровой процесс получил актуальный индекс UID. Если standalone-запуск ещё видит старый дисковый кэш, штатный reimport ресурса в редакторе сохраняет его; бинарный UID-кэш вручную не редактировать.

При завершении отдельного headless-теста toolkit 1.0.3 очищал запись проекта из discovery, хотя редактор продолжал слушать порт. Для восстановления использовано подключение с явными `GODOT_MCP_EDITOR_PORT` и `GODOT_MCP_TOKEN_PATH` к существующему редактору и штатная `RegistryClient.ensure_registered` внутри него. Токен не менялся, addon не редактировался; после восстановления обычное обнаружение снова проверено. Вспомогательный скрипт удалён.
