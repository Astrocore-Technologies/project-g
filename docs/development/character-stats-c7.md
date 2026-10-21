# Character & Stats — C7: мастерская и воспроизводимый аудит

Дата: 2026-10-20. Инструменты, матрица и технические проверки реализованы.
Авторская калибровка и совместная игровая приёмка ещё не завершены. Отменённая
награда очками за достижения не возвращалась. Никакой optimizer не менял баланс.

Последующее решение пользователя: старт0/20,100 HP /100 MP и +5/уровень,
balance21. Матрицы и замеры balance19/20 ниже — сохранённая история C7;
актуальные изменения и проверки — [нулевой старт](character-stats-zero-start.md).

## Инструменты

- В BalanceStudio добавлена кнопка **«Статы / EXP»**. Рабочая редактируемая
  сцена — `tools/BalanceStudio/CharacterAuditWindow.tscn`: отдельный черновик
  `experience-rewards.json`, серверная validation, undo/redo, явное сохранение,
  автоматическое увеличение revision и защита от внешней правки исходника.
  Поля до применения тоже считаются несохранёнными. Escape/закрытие формы
  сохраняют черновик в памяти; выход мастерской предупреждает о нём.
  Можно восстановить поля, экспортировать копию входных файлов и перечитать
  актуальный исходник с подтверждением замены черновика.
- Экспериментальная power-кривая A/B вносится только по явной отметке
  **«экспериментальная конфигурация»**, в основной черновик. Действуют прежние
  undo/redo, preview и защита файла основного редактора. Смена сохранённой
  кривой требует отдельной миграции персонажей, не скрытого пересчёта.
- **«Сравнить билды…»** создаёт новый каталог `.artifacts/character-stats/c7-<uuid>`:
  `report.json`, `builds.csv` и точные `content.json`, `experience-rewards.json`,
  `camp-services.json`, `cases.json`. Экспорт не изменяет source или БД.
  Кнопка игрового стенда основной мастерской теперь передаёт также текущий
  черновик EXP-наград в собственный файл тестовой сессии.
- Исправлен ExpectedDps существующего `BalancePreview`: отрицательный Critical
  Damage больше не создаёт отрицательный ожидаемый DPS, крит даёт 0 по серверному
  правилу. Обычный некритический урон при этом не исчезает. Начальный тестовый
  `BalanceTestBuild` теперь содержит все семь статов по 10, как обычный герой;
  явно заданные нестандартные QA fixtures по-прежнему поддерживаются.

Новый CLI использует те же серверные `StatCalculator`, WeaponPower,
ApplyDefense, hit/crit и ExperienceCurve. Выдачу ресурсов, EXP или очков клиенту
не поручали. Protocol v82, SQL schema12, стеки и ownership не менялись.

```powershell
dotnet run --project tools/CharacterStatsAudit -- D:/projects/project-g D:/projects/project-g/.artifacts/character-stats/my-new-audit
# Повторить расчёт по сохранённым входным файлам, а не по текущим source:
dotnet run --project tools/CharacterStatsAudit -- D:/projects/project-g D:/projects/project-g/.artifacts/character-stats/my-reproduction D:/projects/project-g/.artifacts/character-stats/c7-approved-health
```

Выходной каталог CLI должен быть новым, существующие доказательства не затираются.
`cases.json` — редактируемый сценарий. Содержит уровни 1/10/20/30 и только
математические stress 100/200; семь концентрированных и четыре смешанных билда;
реальные naked/ordinary/defensive item sets; отдельные положительные/отрицательные
test modifiers. **594 строки**. Альфа cap30 и сохранения не повышаются.
CSV/JSON включают HP/MP/Stamina, signed regen и прочие derived stats, physical/
magic mitigation и effective HP, melee/ranged/magic basic DPS и стоимость навыков.

Артефакт: `.artifacts/character-stats/c7-audit-final/`. Повтор в `c7-reproduced/`
дал **побайтово совпадающий report.json**. SHA-256 content/cases записаны в отчёт;
рядом все четыре исходных файла и сведения о runtime/OS.

## Сравнение до согласованной правки STR→HP

Уровень30, весь пул 163 очка, без экипировки/временного modifier. Референсная
цель — `test_creature`; оружие — training sword/bow/staff с их реальными параметрами.

| Билд | HP | MP | Melee DPS | Ranged DPS | Magic DPS | Physical EHP | Magic EHP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| STR | 110 | 140 | 213,58 | 52,80 | 32,92 | 117,70 | 126,50 |
| VIT | 273 | 140 | 28,01 | 23,34 | 32,92 | 514,60 | 313,95 |
| INT | 110 | 140 | 28,01 | 23,34 | 398,75 | 117,70 | 126,50 |
| WIS | 110 | 1444 | 28,01 | 23,34 | 32,92 | 117,70 | 395,45 |
| Равномерно | 133 | 324 | 93,37 | 76,76 | 124,37 | 163,99 | 198,84 |

- INT не увеличивает MP/MDEF, WIS увеличивает: независимость подтверждается
  матрицей и unit tests. Смешанные билды расходуют весь пул без потерянного
  остатка; используется largest remainder со стабильным порядком статов.
- Concentrated VIT даёт 46,95% naked physical mitigation, WIS — 72,18% magic.
  Ориентир GDD 20–40% не является cap и не был превращён в clamp. Нынешний
  принятый ApplyDefense не заменялся на sqrt без решения автора.
- Матрица выявила незавершённый коэффициент CS15: STR → Max HP был равен0,
  хотя небольшой вклад задан GDD. Пользователь **явно выбрал временно 0,2 HP/STR**
  при текущем базовом HP100, сохранив VIT. Настройка внесена в `balance.health.strength=0.002`,
  `balanceVersion` повышен с19 до20, schema25 сохранена. В существующей общей
  формуле это доля BaseHP, не flat modifier: `BaseHP × (1 + 0.002 STR + 0.01 VIT)`.
  Связь применяется тем же калькулятором к существующим профилям NPC и Эхо;
  например MaxHP test_creature стал84,8 вместо84. Урон/защита не менялись.

Новая матрица — **`c7-approved-health/`**; воспроизведение из её входных файлов
в `c7-approved-health-reproduced/` дало побайтово тот же `report.json`.
SHA-256 content совпадает с текущим `prototype.json`, balance20. Исходный
baseline выше сохранён для сравнения. На уровне30 новые HP / physical EHP / magic EHP:

| Билд | HP | Physical EHP | Magic EHP |
| --- | ---: | ---: | ---: |
| STR | 144,6 | 154,722 | 166,29 |
| VIT | 275 | 518,375 | 316,25 |
| INT | 112 | 119,84 | 128,8 |
| WIS | 112 | 119,84 | 402,64 |
| Равномерно | 139,8 | 172,3734 | 209,001 |

Melee/ranged/magic DPS и MP из прежней таблицы не меняются; DPS-equivalent TTK
слегка увеличился вместе с HP референсного NPC. Новое начальное HP героя112.
В редакторе «Рост HP» добавлена подсказка о единицах, чтобы 0.002 не путали
с 0.2 в JSON.

**Темп EXP:** текущая legacy-linear:100 требует **43 500 EXP** 1→30;
эксперимент A150/B4 — **387 115 EXP**, первый порог 1677 вместо100. При пуле20
это 2175 и 19 356 эквивалентных сольных PvE-побед соответственно. NPC-руда1
потребовала бы 43 500 / 387 115 единиц. Это не оценка часов: фактическая частота
боёв, путь/сбор материалов и доступность контента ещё не измерены плейтестом.
Discovery/старое поручение одноразовые, watch — только bonus pool, exploration
требует новых ячеек. Числа activityCounts не разрешают повторять одноразовые
источники и не обещают достичь cap на существующем объёме открытий.

### Пределы математической модели

Контакт геометрически успешен, цель стационарна; expected DPS включает числовые
hit/crit. TTK в JSON/CSV — **HP / expected DPS**, не дискретная симуляция
первого взмаха, подготовки, снаряда, ротации, активной защиты, движения или сети.
Он может быть короче одного attack interval и не доказывает такой kill time
в игре. Для magic используется MDEF, для остальных оружий PDEF.
EHP без regen, evasion и активного блока. Gear — только перечисленные item
modifiers, полный condition, без enhancement/profession bonuses. Будущий
выбор лучшего билда по одной колонке этого отчёта был бы неверным.

Ресурсные uses — число upfront оплат из полного запаса, без regeneration,
cooldown, channel drain, charges и ротации. HealthCost — абсолютное HP;
возможность фатальной оплаты здесь не симулируется. Signed modifiers — fixture,
не новые предметы. Реальные resource transitions и negative-regen death
проверяются отдельно существующими domain/UDP/SQLite tests.

## Измерения и проверка

Оборудование: Windows, Intel64 family6 model186 stepping2, 16 логических CPU;
.NET10.0.3 Debug. UI: Godot4.7.1, Mobile/Vulkan, RTX4060 Laptop GPU. Это локальный
Development стенд, не capacity estimate MMORPG.

Пять idle UDP клиентов в Beech, все региональные simulation contexts, повторные
намерения 20Hz, около10с, rendering исключён. Трафик одного observer — payload
всех типов сообщений, без transport headers/retransmits. Largest packet930B.

| Настройка сети | Tick p50 / p95 / p99, мс | Payload/с | Snapshot interval p50 / p95, мс |
| --- | --- | ---: | --- |
| Без impairment | 0,272 / 0,569 / 1,059 | 10 913 B | 47,16 / 72,92 |
| Настроены 100–150мс и 10% loss | 0,354 / 2,168 / 12,213 | 10 906 B | 47,59 / 81,53 |

Это **межприходный интервал**, не RTT/command latency. Второй прогон задаёт
LiteNetLib impairment, не измеряет потерю внешнего канала. Отчёты
`c7-network-clean-health.json`, `c7-network-lossy-health.json` на balance20;
SHA-256 входного content и окружение — `c7-measurement-inputs.json`.
Одинакового performance-baseline
C0 в сохранённых материалах нет, поэтому ускорение/регрессия относительно C0
не заявляется. Idle full resources не доказывают стоимость массового combat/EXP.

Отдельно `EveryChangedResourceTickCommitsAndIdleFullTicksDoNotWrite`:
40 SQLite commits за2 simulated секунды активного восстановления одного героя,
после заполнения — 0. Median8,816мс, p95=18,630мс, max31,170мс
(`c7-resource-health.trx`, balance20). Это ожидаемая
цена C2 publish-after-checkpoint на каждом изменении ресурса; прежняя coalesced
схема имела2 commits за ту же длительность. Данные не обосновывают возвращение
к публикации неподтверждённых ресурсов или вывод о масштабировании.

- Build `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  — успешно, прежнее NU1900 NuGet audit. Изменён только явно выбранный STR→HP;
  другие coefficients, EXP-кривая и rewards не менялись.
- 48 targeted audit/workshop/progression/allocation/respec tests — passed.
  `c7-target.trx`: распределение остатков, stress без поднятия cap, независимость
  INT/WIS, zero negative-crit DPS, strict reward validation, undo/revision/conflict,
  прежние редакторские сценарии и совместимость V1.
- На balance20 отдельно прошли два сетевых замера и две resource проверки:
  `c7-network-clean-health.trx`, `c7-network-lossy-health.trx`, `c7-resource-health.trx`.
- После нового STR→HP расширенные **1015 боевых/resource/Stats/тaверна/respec/tool tests passed**,
  `c7-health-target-final.trx`. Прежние ожидания HP актуализированы
  под новый баланс; lifesteal/reflection test проверяет порядок относительно
  текущего MaxHP, а не старой константы. Ещё6 проверок нового коэффициента,
  SQLite fraction restore, audit и редакторской подсказки — passed,
  `c7-approved-health-audit.trx`.
- Проверки мира, данных и UDP на balance20: **274 passed / 0 failed / 2 skipped**,
  `c7-health-world-final.trx`. Проверены восстановление Эхо с эффектами и
  сохранением, полный запас при призыве, передача MaxHP босса и отсутствие
  подстройки его MaxHP при подключении второго игрока.
- `c7-health-periodic.trx` — **1 passed**: синхронные basic/DoT смерти остаются
  за одним durable checkpoint. Для открытия checkpoint gate тест теперь
  тратит INT вместо STR: его нулевой magic scale и исходный 1 HP не меняются.
  Изменение STR действительно пересчитывает текущий HP пропорционально MaxHP,
  поэтому прежний gate через STR больше не был нейтральным fixture действием.
- Fixture dry-run старого ресурсного контекста: **55/110 → 56/112**, без выдачи
  очков/сброса progression, commit и SQLite reopen сохраняют56/112.
- Рабочая `Tests/CharacterAuditSmoke.tscn`: **CHARACTER_AUDIT_UI_OK**,
  настоящие mouse clicks по той же editable scene, staging/undo/redo/save,
  revision2→3 только на копии, opt-in power1677/undo, внешний конфликт без
  перезаписи, export, Escape/reopen с сохранением draft, 1280/1920.
  Итоговые снимки — `c7-ui-final/`; экспорт этого тестового черновика —
  `c7-b4fab8b409914e7b86e395bdcfb62b25/`. Игровые source не сохранялись этим тестом.
- Кнопка в настоящем основном окне мастерской на balance20:
  **CHARACTER_AUDIT_HOST_OK**, 1280px, открыть/закрыть без изменения основного
  черновика. Повторный игровой GUI/UDP/SQLite smoke C5c на balance20:
  **CAMP_ACTIVITIES_SMOKE_OK**, EXP0→20→21→61, артефакты
  `c5c-ui-a557401a359a4693b95c3d36602fb0df/`, отдельная SQLite и Beech fixture.
- Server `--validate-content --RegionExports:VerifySources=false --RegionExports:AutoExport=false`
  — успешно: schema25/**balance20**, 8 weapons, 18 abilities, 5 creatures.
- Полная последовательная регрессия до правки коэффициента: **2086 passed / 0 failed / 7 skipped**,
  7мин4с, `c7-regression.trx`.
- Финальная полная последовательная регрессия на **balance20: 2088 passed /
  0 failed / 7 skipped**, всего2095, 8мин4с, `c7-regression-health-final.trx`.
  Команда: `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=c7-regression-health-final.trx" --results-directory .artifacts/character-stats`.
  Коллекции последовательно через временный output-only `xunit.runner.json`
  с `parallelizeTestCollections=false`; файл удалён после завершения.
  Пропущены6 live PostgreSQL и1 проверка отдельной development-копии сохранений.

## Rollout и dry-run

Новых SQL migrations или protocol change нет. Fixture-only reopen, resource
checkpoints и legacy ledger проходят существующие tests. Это не миграционный
dry-run рабочей `.data`: она не открывалась. Старый default workshop fixture
1/1/… меняется только для **нового** создаваемого тестового билда; сохранённые
workspace presets остаются как заданы автором.

Перед пользовательским запуском: собрать solution, открыть `tools/Open-BalanceStudio.ps1`,
проверить основной source, «Статы / EXP», экспортировать снимок, запускать
отдельную игровую сессию редактора. Изменённые reward revision сохраняются
явно; работающий сервер использует прежнюю загруженную конфигурацию до restart.
Новый curve key в существующей БД отклоняется с требованием миграции, а не
обнуляет/пересчитывает EXP. Для эксперимента используется новая БД.

Rollback инструмента не требует изменения БД; исходные content/reward файлы
сохранять отдельно перед авторским редактированием. Если позже сохранять
новую кривую/контент и обновлять персонажей, нужен отдельный согласованный
конвертер и копия согласованного checkpoint. В этот запуск этого не делали.

Исходные GardenValleys/FoxHills отсутствуют в workspace из параллельной работы
над картами. Проверка content использовала опубликованные exports, без доказательства
source freshness; игровые smoke C1–C5 выполнены на изолированных fixtures.
Возврат исходных карт и домашняя приёмка остаются необходимыми для полного
стартового маршрута. Live PostgreSQL не проверялся. Пользовательские процессы,
рабочие сохранения и городские ассеты не менялись; commit/push не выполнялись.

## Что остаётся до полной приёмки

STR→HP закрыт выбранным временным коэффициентом. Окончательные A/B и темп CS07, остальные открытые
элементы CS03/15 — авторские решения по результатам игрового плейтеста.
Пользовательская приёмка C1–C5/C7 не подменяется числом тестов. Другая награда
достижений и дальнейшие P2P/рыночный EXP/content definitions остаются отдельными
задачами. Весь GDD03 не объявляется завершённым этим отчётом.
