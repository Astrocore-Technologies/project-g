# Собственные ассеты Садовых долин

`garden_asset_library.blend` содержит 19 моделей, созданных в Blender через MCP для Садовых долин Линвеля. Имена, размеры и количество треугольников записаны в `catalog.json`. Исходный инструмент сборки: `tools/build_garden_assets.py`; его запуск для геометрии должен происходить внутри Blender через MCP. Отдельные модели экспортированы в GLB в `Content.Client/Assets/World/Linvel/Gardens/`.

Все модели статические, с применённым масштабом, UV и собственной рисованной палитрой. GLB имеют встроенные текстуры. Сохранённые Godot ArrayMesh лежат в `Content.Client/Assets/World/Linvel/Gardens/Meshes/`; игровое размещение редактируется в `GardenValleys.tscn`.

Модели и растительность создавались процедурно для проекта; сторонние модели не копировались. Художественное направление задают предоставленные пользователем лор и визуальные материалы из `art-source/linvel/references/`. Сельские здания, каменные основания, красная черепица, живописные кроны и мягкие цвета служат собственному облику Линвеля.

`garden_terrain.blend`, `tools/build_garden_terrain.py`, старый `garden_terrain.glb` и профиль `layout.json` сохраняют историю первоначального рельефа. Текущий игровой рельеф редактируется в Terrain3D и хранится в `Content.Client/Assets/World/Linvel/Gardens/Terrain/Data/`. Повторный экспорт земли из Blender не должен заменять ручные правки Terrain3D.

Шейдер `Terrain/painted_terrain.gdshader` основан на примере `extras/shaders/lightweight.gdshader` установленного Terrain3D 1.0.2 и дополнен стилизованным освещением Project G. Полный текст MIT License и copyright авторов Terrain3D сохранены в начале файла. Четыре native-покрытия рельефа созданы для проекта.

Инструкция по редактированию и проверкам: `docs/development/linvel-garden-valleys.md`.
