# Создание регионов — R1

Обновление R2: действующие сцены пристани и города экспортируются общим инструментом.
Команды, границы и приёмка: [region-export.md](../development/region-export.md).
Ниже сохранён контракт демонстрационного каркаса R1; его две demo scenes пока не являются
производственными сценами всех действующих регионов.

Текущий результат — offline каркас двух demo scenes. Экспорт и перенос существующей
геометрии относятся к R2/R3. DemoBlocker и прочие образцы не заменяют карту сервера.

## Проверка в редакторе

1. Собрать C# проект, открыть `Content.Client/project.godot` в Godot .NET 4.7.1.
2. Открыть `Scenes/Regions/Prototype/Prototype.tscn` либо `Outskirts/Outskirts.tscn`.
3. Переместить `Terrain/DemoBlocker`, сохранить. F6 показывает текущую сцену.
4. Открыть `Scenes/App/GameRoot.tscn`, F6. Кнопки переключают обе карты; строка
   OFFLINE PREVIEW подтверждает, что серверное подключение не требуется.
5. Изменить ID Gate на ID Entry и сохранить. Preview покажет duplicate ID;
   вернуть исходный ID. Некорректная сцена не заменяет уже открытый регион.

F5 по-прежнему запускает существующую сетевую игру. Preview GameRoot будет расширяться
до полноценного session shell при переносе presentation/activation на R3/R4.

## Authoring contract v1

- Одна единица — метр. X/Z — плоскость, Y — вверх. RegionRoot имеет identity transform.
- `RegionId` — постоянный key: 1–48 символов `[a-z0-9_-]`. Демо сохраняют
  `prototype` / `outskirts`; key не зависит от названия файла/узла.
- `MovementBounds` — конечный положительный Rect2 в X/Z, минимум включён, максимум
  исключён. Текущие demo bounds: (-15, -15), размер (30, 30).
- Прямые Node3D children: Environment, Terrain, Decorations, PublicStateBindings,
  AuthoringAnchors. Preview camera размещена в Environment; gameplay camera появится
  при привязке server actor. Runtime actors не создаются маркерами.
- Entry/Gate/ActorSpawn/Interaction/NavigationPatch лежат под AuthoringAnchors.
  Их region-local положение: конечные X/Z, Y=0; basis единичный, включая родителей.
- AuthoredObjectId — непустой UUID в D-format; уникален внутри региона для всех типов
  markers. Rename/move сохраняет ID. При дублировании размещения назначить новый UUID
  в Inspector, например полученный командой `[guid]::NewGuid()` в PowerShell.
  Проверка не заменяет конфликтующие IDs автоматически.
- Gate хранит только публичный radius (0, 10], patch — положительный footprint X/Z.
  Destination, NPC definitions, rewards, secret names/conditions задаются server rules.
- Для первого exporter зарезервированы groups `region_walkable` и
  `region_movement_blocker` на StaticBody3D: плоская поверхность Y=0 и axis-aligned
  BoxShape3D, единичный scale, без rotation/skew. Mesh — visual, CollisionShape —
  export geometry. Декоративные meshes сами по себе не означают препятствие.
  R1 проверяет markers/структуру; shape/rasterization/reference validation появится в R2.
- Неподдерживаемые collider/transform должны отклоняться R2 exporter, а не становиться
  свободной областью. Высоты и многоэтажность ждут отдельного navigation contract.

`RegionAuthoringValidation` вызывается перед preview и доступен через configuration
warnings RegionRoot. После изменения descendants повторный preview даёт свежую полную
проверку. Маркеры не выполняют tick, сетевые операции или gameplay logic.
Release stripping authoring anchors и проверка PCK входят в R2; эти сцены пока tooling.

## Автоматическая проверка

После сборки запустить Godot .NET с `--headless --path Content.Client
res://Tests/Regions/RegionAuthoringSmoke.tscn`. Успех: exit code 0 и
`REGION_AUTHORING_SMOKE_OK`. Проверяются 20 переключений, освобождение старых nodes,
PackedScene round-trip placement/ID, duplicate UUID, bounds/transform и сохранение
активной сцены при ошибке следующей. Это не проверка сетевого перехода R4.
