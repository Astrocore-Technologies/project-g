# Movement & Controls — M3

Дата: 2026-10-17. Камера и статические перекрытия реализованы; пользовательская
визуальная приёмка отдельно от автоматической проверки. Следующий этап — M4
(Follow и переназначение PC input), после проверки пользователем. Mobile исключён.

## Что изменилось

Кроны, крыши и стены между камерой и своим персонажем получают **20% opacity**.
Вместо полного исчезновения крон действует один компонент `OcclusionPresentation`.
Он индексирует статические меши региона один раз в клетки 16 м и проверяет
ближайшие клетки каждые 0,05 с. Лучи к стопам/корпусу/голове с техническим clearance
покрывают силуэт персонажа. Физические тела и collision shapes не меняются.

Godot Mobile renderer игнорирует `GeometryInstance3D.Transparency`. Поэтому
`OcclusionMaterials` создаёт и кэширует fade-копии Standard/ORM материалов и
варианты известных opaque painted shaders с ALPHA=0,2. Текстуры, авторское освещение
и параметры копируются; исходный материал/шейдер соседнего объекта не меняется.
После ухода камеры, отключения компонента или удаления игрока исходные override
восстанавливаются, включая отдельные surface overrides. Варианты переиспользуются;
нового материала каждый кадр нет. Прозрачный pipeline может менять сортировку
и отбрасывание теней; renderer остаётся Mobile.

Сервер применяет LOS к другим игрокам и Echo после spatial AOI query.
Собственный runtime entity всегда остаётся в interest. Текущий технический
критерий — существующий луч между точками на высоте 1 м от стоп через публичные
boxes/triangles/terrain faces. Низкий бордюр не скрывает игрока; стены, непрозрачные
перекрытия и рельеф блокируют луч. Flat-прототип использует прежнюю консервативную
grid-проверку. Камера и её прозрачность не участвуют в серверном решении.

Потеря LOS вызывает имеющийся reliable despawn и прекращает snapshots, публичные
HP/equipment/PvP/Avatar/BasicAction состояния. Возврат видимости присылает свежие
spawn/state. NPC сохраняют прежний AOI; рабочие данные и wire v76 не меняются.
Журнал скрывает имя источника за препятствием. Уже выпущенный снаряд проверяется
по собственной позиции; видимый снаряд остаётся видимым без раскрытия старой
точки кастера. Его runtime source ID сохраняется в существующем wire-контракте,
положение/модель кастера из этого не создаются. Приватный party presence по
ADR 0011 сохраняется; он не создаёт модель скрытого игрока в мире.

## Где настраивать

- `Content.Client/Scenes/PlayerAvatar.tscn`, узел **Occlusion**:
  `UpdateInterval` и `ActorClearance` — технические параметры, редактируемые в Inspector.
  Компонент активируется только у локального игрока.
- В редактируемой сцене региона группы **camera_fade** и **camera_roof**
  на меше или его родителе; **camera_occlusion_ignore** исключает поддерево.
  Существующие `forest_crown`, `forest_upper_branches`, `surface_occluder`
  и `collision_visual` поддерживаются без изменения рабочей карты.
- Комбинированный меш здания получает 20% opacity целиком. Для отдельного поведения
  крыша должна быть отдельным редактируемым мешем с `camera_roof`.
- Новому custom shader нужен uniform `camera_opacity`, реально влияющий на ALPHA,
  либо явный адаптер в `OcclusionMaterials`. Неизвестный shader выдаёт предупреждение;
  smoke реальной карты проверяет отсутствие таких материалов.
- `OccludedOpacity=0.2` соответствует ГД; изменение этого значения требует
  изменения принятого gameplay-контракта.

## Выполненные проверки

Сборка решения с отдельными server/test outputs:

```powershell
dotnet build Game.slnx --artifacts-path .artifacts/movement-controls-m3/build --no-restore -m:1 -p:NuGetAudit=false
```

Успешно, 0 warnings / 0 errors. Первое создание каталога выполнялось с restore
из локального NuGet cache. После вынесения компонента в PlayerAvatar отдельно
повторена сборка `Content.Client/Project-G.csproj` с теми же параметрами.

Целевой набор — **37/37 passed**:

```powershell
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m3/build --no-build --no-restore --filter 'FullyQualifiedName~PlayerVisibility|FullyQualifiedName~InterestTests|FullyQualifiedName~ProjectileSnapshotTests|FullyQualifiedName~SurfaceTerrainVisibility|FullyQualifiedName~SurfaceCliffVisibility' --logger 'trx;LogFileName=m3-visibility-final.trx' --results-directory .artifacts/movement-controls-m3
```

Проверены: невидимый игрок отсутствует в spawn/snapshot; self остаётся; потеря
и возврат LOS; свежая baseline; низкое препятствие; игроки на разных этажах;
AOI hysteresis; независимый видимый projectile со скрытым origin, его исчезновение
и повторное появление. UDP integration использует два клиента, 100–150 мс и
10% потерь: скрытый игрок не раскрывается через snapshots/боевые позы; два
цикла hide/reveal восстанавливают клиентский состав сущностей.

Общая последовательная регрессия — **1962 passed / 1 skipped / 0 failed**,
1963 tests, 6 мин 53 с. Команда:

```powershell
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m3/build --no-build --no-restore --filter 'FullyQualifiedName!~Postgres&FullyQualifiedName!~WorkingDatabase' --logger 'trx;LogFileName=m3-regression.trx' --results-directory .artifacts/movement-controls-m3
```

Только в изолированном test output временно использован `xunit.runner.json`
с `parallelizeTestCollections=false`, `maxParallelThreads=1`; после прогона
удалён. Лог — `.artifacts/movement-controls-m3/regression.log`, TRX рядом.
Postgres/WorkingDatabase намеренно исключены; один DevelopmentCopy test пропущен
по требованию внешней development-БД. Это не проверка рабочей базы.

Godot 4.7.1 Mono, отдельные процессы без управления общим редактором:

- `Tests/Camera/OcclusionSmoke.tscn`, headless: 20% opacity, соседний общий ресурс,
  surface overrides, collision shapes, переиспользование материала, disable/exit.
- Тот же стенд, **Vulkan / Forward Mobile, RTX 4060 Laptop**: сохранены
  `.artifacts/movement-controls-m3/occlusion.png` и `occlusion-opaque.png`.
  Реальный пиксель своего персонажа через fade: RGB 0,824/0,259/0,537;
  opaque baseline: 0,149/0,400/0,851. Своя капсула видна, сосед остаётся opaque.
- `OcclusionSmoke.tscn -- --real-region`, headless и Vulkan/Mobile: Буковая долина,
  **1189 мешей**, 30 авторских точек обзора, 39 попаданий fade суммарно,
  unsupported materials = 0. В Vulkan каждый viewpoint реально отрисован,
  fade-шейдеры скомпилированы без ошибок; кадр навеса — `beech-occlusion.png`.
- `GameplayCameraSmoke.tscn`: 55°, шаг 5%, границы 70–130%, точный центр при
  разных высотах, изоляция анимации и владение AudioListener.
- `LinvelBeechPreview.tscn --quit-after 8`: прежний F6-preview работает с новым
  компонентом без глобальной мутации forest materials.
- `NavigationCancellationSmoke.tscn` с отдельным M3 сервером и SQLite на
  `linvel_beech`: 60–90 мс / 3% потерь, sparse Destination/Stop, отмена/replay,
  удерживаемая ПКМ/упор/отпускание и 0,4/0,6 м correction. Рабочий сервер/БД
  не останавливались и не сбрасывались. Harness и логи — `.artifacts/movement-controls-m3`.

Есть прежнее предупреждение Terrain3D `instance_reset_physics_interpolation`
deprecated. Новых warnings о неподдержанных материалах на Буковой долине нет.

## Приёмка в игре и границы

Пересобрать обе стороны и войти в Буковую долину. Пройти под кронами и навесом
лесорубов: свой персонаж читается, перекрытие остаётся полупрозрачным, соседнее
дерево/здание не меняется. Отойти: материал восстанавливается. Упереться в стену:
коллизия сохраняется. Со вторым клиентом проверить, что противник за стеной
не появляется после fade, а при выходе из-за стены появляется с текущей позицией.

Отдельный редактируемый стенд — `Content.Client/Tests/Camera/OcclusionSmoke.tscn`.
Обычный запуск автоматически проверяет сценарий и завершает процесс.

Этот срез работает со статическими помеченными scenery meshes. Произвольный
новый шейдер, модификация геометрии на ходу, двери/лифты/платформы требуют
соответствующего адаптера и пересчёта индекса в M8. Это не проверка падений/опор
M7–M8, не production load test и не полное завершение GDD 02. Follow/rebinding
и финальная совмещённая приёмка остаются следующими этапами.
