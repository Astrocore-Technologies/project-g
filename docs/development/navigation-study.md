# Исследование навигации: склон, впадина и мост

Первый исследовательский блок R7, 2026-10-10. Это отдельная площадка для выбора
навигации. Сетевая игра продолжает использовать текущие плоские координаты.
Рекомендация для следующего этапа — [ADR 0015](../architecture/0015-navigation-study-recommendation.md).

## Просмотр и редактирование

Открой `Content.Client/Scenes/NavigationStudy/NavigationStudy.tscn` в Godot и нажми
**F6**. При запуске сцена сама запекает навигацию, рассчитывает два набора маршрутов
и запускает шарики по ним. Жёлтые линии — полигональный вариант, голубые — сетка.
В левом верхнем углу показаны результаты каждого сценария.

`Geometry` содержит обычные редактируемые тела, меши и collision shapes. Склону
соответствует `HillRamp`, спуску — `PitRamp`, мосту — `Bridge` и `BridgeRamp`.
Проход шириной 1.8 м находится между `NarrowWallLeft` и `NarrowWallRight`.
`Scenarios/<имя>/Start` и `Goal` — редактируемые Marker3D. У сценария `Cliff`
стоит metadata `reachable=false`, поскольку площадка изолирована.
Профиль радиуса/высоты/ступеньки/уклона задаётся в инспекторе корня сцены.

Это исследовательский preview с фиксированной камерой; шарики показывают offline
маршруты, а не сетевых игроков. F5 запускает обычную игру. Для просмотра площадки
не нужны отдельный экспорт, PowerShell-скрипт или запущенный сервер.

## Сравниваемые реализации

В `tools/NavigationStudy` находится независимая библиотека .NET 10 без Godot.
Godot используется только для offline bake редактируемой сцены. Профиль проверки:
радиус 0.45 м, высота 1.8 м, ступенька 0.25 м, уклон до 45°, voxel 0.15×0.05 м.
Запечённая геометрия имеет небольшой вертикальный voxel offset; это навигационная
поверхность, а не точная поверхность физического collider.

Оба кандидата используют одну запечённую геометрию с одинаковым clearance:

- `PolygonStudy`: A* по треугольникам, связи через общие XYZ-рёбра, путь через
  середины порталов. Каждый изгиб поверхности сохраняется; funnel ещё не реализован.
- `TiledGridStudy`: клетки 0.5 м, несколько высот в одной колонке, тайлы 16×16
  клеток. Связные компоненты внутри тайла образуют межтайловый граф. После coarse
  поиска выполняется bounded fine поиск внутри коридора. Связь клеток проверяется
  по исходной поверхности в обе стороны. При patch возможен bounded fallback.

У обоих query есть общий бюджет раскрытых узлов. Workspace, heap и выходной список
переиспользуются. Один workspace предназначен для одного worker; это не thread-safe
глобальный сервис. Lookup высоты и видимость используют пространственные индексы.
`StudyVisibility` проверяет конечные box occluders отдельно от проходимости.
Это точечный луч, не готовая проверка hitbox, projectile или melee.

Блокирование треугольников `ApplyPatch` меняет revision и проверяется обоими
кандидатами. Это query fixture; production patch lifecycle, доставка по сети и
перестройка тайлов в tick сюда не подключены.

## Результаты

Исходный fixture: 40×32 м, холм 4 м, впадина −3 м, настил моста 4 м,
земля под ним 0 м, узкий проход, изолированный обрыв и длинный маршрут.
Автоматические проверки подтверждают восемь сценариев, непрерывную высоту всех
отрезков, разделение этажей, путь на мост через рампу, радиус в узком проходе,
блокирование/восстановление рампы, stale revision и bounded failure.

Измерения приведены в [сыром отчёте](navigation-study-results.json).
Release, .NET 10.0.3, Intel Family 6 Model 186, `DOTNET_TieredCompilation=0`;
200 прогревающих и 2000 измеряемых запросов на сценарий. Это один запуск, без
утверждения о стабильных percentiles под конкурентной нагрузкой.

| Показатель | Полигональный вариант | Сетка 0.5 м |
| --- | ---: | ---: |
| Узлы одной карты | 162 | 4818 + межтайловые компоненты |
| Память одного backend с общим mesh, приблизительно | 65 KiB | 1.39 MiB |
| Длинный маршрут, CPU/query | около 3 µs | около 70 µs |
| Подъём на мост, CPU/query | около 2.5 µs | около 24 µs |
| Аллокации прогретого запроса | 0 B | 0 B |

32 независимые копии дают 5184 polygon nodes против 154176 grid nodes. Их
локальные запросы не раскрывают остальные карты; суммарная managed память
mesh + backend составляет примерно 2 MiB против 40 MiB. Это проверка resident
AOI данных, не длинный маршрут по одной связной большой карте.

Сетка лучше в некоторых случаях: изолированный обрыв распознаётся быстрее
через coarse graph. Её короткий путь на холм/во впадину также короче текущего
пути по серединам polygon portals. Для игрового полигонального backend нужен
funnel/smoothing, сохраняющий высоты и проверяемый corridor.

`PolygonDiagnosticBinaryBytes` / `GridDiagnosticBinaryBytes` измеряют одинаковую
несжатую диагностическую упаковку узлов и рёбер, включая coarse graph у сетки.
`ExportJsonBytes` — общий JSON исходной геометрии. Эти величины не являются
готовыми wire packets или измеренным bandwidth игры.

## Границы исследования

Пока не измерены snapshot/nav delivery, latency при loss/reorder, CPU на Mobile,
пиковая native/GPU память клиента, 200/400 игроков и большая связная территория.
Повтор маршрута в .NET проверяет локальную детерминированность, но не заменяет
prediction/reconciliation двух клиентов. Размеры и бюджеты инструмента не
объявляются постоянными gameplay или production limits.

Библиотека подключена только к preview, тестам и benchmark. `Content.Server`
и `Content.Shared` не получили зависимости от Godot или исследовательского кода.
Live protocol, save models, SQLite и рабочие authored карты не изменены.

## Воспроизведение для разработчика

```powershell
dotnet build Game.slnx --no-restore -m:1
dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~Navigation

# При изменении сцены — обновить snapshot fixture после успешной проверки.
& 'D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path Content.Client res://Scenes/NavigationStudy/NavigationStudy.tscn -- --navigation-study-export=D:/projects/project-g/.artifacts/navigation-study/map.json

$env:DOTNET_TieredCompilation = '0'
dotnet run --project tools/NavigationStudy.Bench -c Release --no-restore -- Content.Tests/Fixtures/navigation-study.json .artifacts/navigation-study/benchmark.json
```

Реально выполнены build решения, 41 navigation test, headless bake/export и
графический запуск на Godot Mobile/D3D12 с RTX 4060 Laptop. Повторный export
дал одинаковые байты/revision. У сборки остались существующие NU1900/NU1903:
недоступность NuGet audit и advisory для SQLitePCLRaw; новых C# warnings нет.
