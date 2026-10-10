# ADR 0015: Рекомендация навигации после проверки рельефа

## Status

**Принято для интеграционного среза, 2026-10-10.** После исследования пользователь
разрешил подключить высоты к движению, бою, сети, сохранениям и общему подземелью.
Дальность атак — прямая 3D; combat tag блокирует все региональные переходы.
Limits крупного мира и нагрузочная приёмка остаются отдельной проверкой.
Связано с [ADR 0014](0014-terrain-and-shared-dungeons.md).
Полные сценарии, команды и границы замеров — [отчёт исследования](../development/navigation-study.md).

## Context

Нужны склоны, впадины и разные поверхности на одинаковых X/Z при независимом
.NET сервере. Текущая flat grid не описывает эти случаи. На редактируемой сцене
сравнены offline polygon mesh и tiled grid с несколькими поверхностями и coarse
graph. Оба backend проходят reachability, height continuity, ceiling visibility,
clearance, dynamic blocking и stale revision проверки.

## Decision

Использовать **offline polygon navigation с независимыми .NET queries** как
основу следующего вертикального среза. Godot запекает authored geometry в
редакторе/инструменте экспорта; сервер и prediction работают на публичном
versioned export без NavigationServer3D и без Godot assemblies.

Причина: на fixture 162 узла против 4818 у сетки, примерно в 20 раз меньше
managed памяти с учётом общей геометрии, существенно дешевле поиск длинного
пути. Данные 32 отдельных копий сохраняют преимущество. Это результат текущих
реализаций, а не доказательство превосходства над любым оптимизированным grid.

Перед production реализацией нужны tile packaging/AOI cache, допустимые budgets,
funnel/corridor smoothing и проверка большой связной карты. Prototype пути через
середины порталов безопасны на fixture, но дают лишние повороты и длину.
Простой scalar heightfield не подходит для моста с нижним проходом.

## Alternatives

| Вариант | Результат / последствия |
| --- | --- |
| Offline polygons — рекомендуется для следующего среза | Компактность и дешёвый поиск; сложнее smoothing, tile seam validation, patches и экспорт. Нужны строгие проверки geometry/revision и parity |
| Tiled surface grid + coarse graph | Проще регулярная raster модель; coarse graph быстро отсекает unreachable islands. Больше узлов/памяти; coarse route не гарантирует кратчайший fine path, теряется subcell геометрия |
| Godot NavigationServer на сервере | Не рассматривается: нарушает принятый независимый headless .NET Server boundary |

Новая библиотека исследования не объявляется production navigation API. Перенос
детерминированного публичного ядра в Shared выполняется отдельными небольшими
изменениями после приёмки; скрытые server rules туда не переходят.

## Coordinates, identity and revisions

Для R8 предлагается положение `X/Y/Z + physical surface identity`, связанное с
region/map revision. ID поверхности различает мост и землю под ним, но не является
gameplay channel/layer. ID не сохраняет смысл при произвольном rebake другой
revision; клиентское намерение и сохранённое положение требуют проверки версии.

Исследование использует triangle index и hash полного fixture, matching рёбер
до 1 мм и отдельную patch generation. Это временный формат инструмента. Stable
tile/poly IDs, quantization, wire layout и допустимые размеры региона выбираются
до первого изменения protocol/save models; исследовательские числа не становятся
неявным постоянным контрактом.

## Migration and rollout

Сначала изолированная карта со склоном: два клиента, NPC, серверная высота,
prediction/reconciliation, snapshots и reconnect. Затем боевые height/LOS проверки
и перекрытие. Вход в общее подземелье остаётся зависимым от R4/R6 и решения D3.

Старые X/Z позиции переносятся только по известным region/revision mappings на
однозначную поверхность. Неизвестный mapping или несколько этажей не исправляются
эвристическим выбором верхнего пола. Миграция проверяется на копии БД и получает
отдельное одобрение до применения к рабочим сохранениям. Production rollback
должен учитывать protocol/content/save version вместе.

Исследовательский этап не менял runtime. Последующий разрешённый срез использует
protocol 32, native polygon export v1 и аддитивный SavedSurface v1 (высота + hash
региона). Legacy flat saves сохраняются; перенос старых карт в вертикальные
по-прежнему требует явного mapping. Server остаётся независимым от Godot.

Пределы среза: до 4096 vertices/polygons и 256 OBB blockers, geometry до 256 KiB
на регион, chunks по 900 bytes. Две тестовые вертикальные карты входят в общий
bounded graph из пяти регионов. Рёбра сопоставляются по XYZ с точностью 1 мм;
пол и этаж выбираются высотой, а hash фиксирует точную revision. Это prototype
budgets; расширение на большую карту требует измерений и tile packaging.

Переход сначала сохраняет destination через существующий durable checkpoint.
Далее идут epoch + geometry, scene-ready, baseline, applied и activation.
До activation команды не выполняются; при обрыве вход восстанавливается из
durable destination. Preload до commit и полный R4 этим срезом не реализованы.
Подробности — [terrain integration](../development/terrain-integration.md).

## Outstanding verification

Большая связная карта, smoothing quality, tile seams, релевантная доставка
геометрии и snapshot bandwidth, Mobile native/GPU peak memory, loss/reorder и
нагрузочная ёмкость. Исследование подтверждает выбранные geometry/query сценарии,
но не объявляет законченный R7/R8. Поддержка высот в текущей сетевой игре
проверена на двух тестовых картах, а не на production масштабе.
