# Character & Stats — нулевой старт

Текущая HP-правка — balance22, описана в конце. Исходные расчёты/проверки
нулевого старта ниже относятся к balance21 и сохраняются историей.

Прямое решение пользователя 2026-10-20: новый герой начинает с семью нулевыми
характеристиками,20 свободными очками,100 HP /100 MP. Каждый следующий уровень
даёт5 очков; на уровне30 всего165, максимум одного стата при концентрации165.
Stamina100, стоимость1 очко за +1 и временные веса STR/VIT не менялись.

## Реализация

Настройка в `Content.Server/Data/prototype.json`: профиль `test_adventurer`,
его семь `stats`, `baseHealth=100`, `baseMana=100`,
`progression.initialStatPoints=20`, `statPointsPerLevel=5`. BalanceVersion21,
content schema25. Default `ProgressionBalance.InitialStatPoints` также20.
Новый `BalanceTestBuild` мастерской по умолчанию имеет нулевые статы;
явно заданные QA builds и сохранённые workspace presets не переписываются.

Серверная Starter/Allocation модель уже поддерживает нулевой StartingStats.
Расчёт preview/apply, списание очков, resource fractions, сохранение и respec
используют существующие authoritative пути. Protocol82 и SQL schema12 сохранены.
Новых runtime веток, client authority, per-tick scans и постоянных UI layout нет.
Обновлены клиентские smoke fixtures, чтобы проверять текущий старт и сброс.

100/100 — максимум до вложений и экипировки. После20 STR MaxHP104,
после20 VIT120; после20 WIS MaxMP260. На уровне30 при165 STR MaxHP133,
при165 VIT265, при165 WIS MaxMP1420. Число165 заменяет прежние163 из18/+5;
старые показатели balance19/20 в C7 оставлены историей измерений.

Это меняет стартовую силу, а не только подписи интерфейса. При нулевых статах
числовая прибавка атаки/защиты равна0, скорость имеет базовый множитель1,
HP/MP regen —1/с, базовый crit5%, базовая Block Strength10. Оружие продолжает
давать свой урон; без оружия и без вложений status attack0 означает0 урона
голыми руками по действующей формуле. Минимальный бесплатный урон не добавлялся.
Определения NPC/Эхо и коэффициенты боя этим изменением не менялись.

## Сохранения

Рабочая `.data` не открывалась; миграции, вайпа и автоматического перераспределения
не выполнялось. Существующая проверка EarnedFromLevels сравнивает ledger с
текущим бюджетом: сохранения прежнего старта18/+5 с Allocation требуют явной
миграции **до входа**. Их содержимое не обнуляется и не заменяется новым героем.
Это прежнее fail-closed поведение при смене бюджета, оно не ослаблялось.
Legacy документы без ledger сохраняют действующий отдельный путь совместимости.
Исторический StartingStats также должен быть совместим с профилем для respec.

Для проверки используется новый профиль/отдельная SQLite. Для фактического
переноса старых героев нужен согласованный способ учёта прежних70 базовых и18
свободных очков; в этом изменении такой способ не выдумывался.

## Проверка

- Solution build: `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  прошёл; прежнее NU1900, недоступен NuGet audit endpoint.
- `zero-start-fixtures-final.trx`:43 passed. Новый бюджет/SQLite reopen,
  сохранение доли HP при20 STR, неизменность прежнего18-point документа и отказ
  до миграции, default мастерской, ресурсы, buffer и PvE solo/group.
- Content validation через `--validate-content --RegionExports:VerifySources=false --RegionExports:AutoExport=false`
  прошла: schema25/balance21. Использованы опубликованные exports;
  отсутствующие исходные GardenValleys/FoxHills не восстанавливались.
- `zero-start-audit/`:594 строки CLI по текущим источникам. EXP curve/rewards
  прежние; текущая legacy-linear требует43500 EXP до30, эксперимент A150/B4
  остаётся отдельным сравнением. Уровни100/200 — математический stress.
  Повтор из экспортированных входных файлов в `zero-start-reproduced/` дал
  побайтово совпадающий report.json; SHA-256 content совпадает с текущим source.
- Реальный Godot4.7.1 Mobile/Vulkan, UDP и отдельная SQLite:
  `PLAYER_HUD_OK`, `SOLO_START_OK`, флаг `--zero-start`. Подтверждены семь нулей,
  20 очков, preview без списания, cancel, apply одного DEX, resize1280/1920.
  Артефакты: `zero-start-ui-09b64f0984f64b63af8e0b3abdfd3a61/`.
- `RESPEC_PREVIEW_OK`, `RESPEC_SMOKE_OK`: возврат3 вложений в общий пул20,
  все статы0 после бесплатного сброса, новое распределение, цена100 монет и
  недостаток монет, Escape,1280/1920. Отдельные сервер/SQLite,
  `c4-ui-7afe5ce43dcf44ae93bf00ec516f4a32/`. Галочка подтверждения, подтверждение
  и переход к распределению активированы реальным клавиатурным вводом.
  Синтетические mouse clicks по подтверждению в предыдущих попытках не
  активировали контролы; успешный прогон не доказывает мышиное подтверждение.
  Production UI этим изменением не переписывался.
- Полный последовательный набор `zero-start-regression.trx`:2086 passed,
  5 failed,7 skipped, всего2098,7мин16с. Четыре падения — прежние суммы13/25
  в respec fixtures с историческим бонусом7, теперь15/27. Пятое — сценарий
  одновременных смертей, где после служебной allocation нулевой герой бил
  без оружия с0 урона. Его fixture явно задаёт ненулевой урон тестового оружия,
  сохраняя production unarmed0 и все проверки commit barrier.
- После исправления fixtures итоговый solution build прошёл и **все5 случаев
  повторно прошли** в `zero-start-regression-fixes.trx` (5 passed /0 failed).
  По testId подтверждено точное совпадение упавшего и повторно пройденного
  множества. Остальные2086 уже прошли на том же production content/code;
  после полного прогона менялись только эти fixtures и клиентский smoke.
  Это составная проверка2091 успешного случая, **не один полный зелёный прогон**.
  Семь пропусков:6 live PostgreSQL и1 отдельная development-копия сохранений.
  Временный output-only runner с `parallelizeTestCollections=false` удалён.

Все артефакты находятся в `.artifacts/character-stats/`. Финальный баланс
первых боёв и темпа требует игрового плейтеста, не только числовой матрицы.

## Последующая HP-правка для плейтеста — balance22

По прямому решению пользователя `health.strength=0.05`, `health.vitality=0.1`:
при базовом HP100 это +5 HP/STR и +10 HP/VIT. Сам калькулятор не менялся:
`MaxHP = BaseHP × (1 + 0.05 STR + 0.1 VIT)` до equipment/effect modifiers.
Стартовые100 HP /100 MP,0 статов,20 очков и +5/уровень сохранены.

| Вложения | HP героя |
| --- | ---: |
|20 STR|200|
|20 VIT|300|
|165 STR|925|
|165 VIT|1750|
|24 STR /24 VIT|460|

Веса общие для существующих профилей: test_creature140 HP, test_boss528,
Мира250. Другие коэффициенты не менялись. Подсказка редактора обновлена.
Рабочая БД и пользовательские процессы не изменялись; старые resource maxima
используются существующим механизмом сохранения доли HP, без SQL-миграции.

Проверка balance22: solution build прошёл (прежнее NU1900), content validation
прошла на опубликованных exports. `health-5-10-final.trx`: **1145 passed /0 failed**,
9с. Проверены combat/stats/data, распределение, respec/domain+UDP, таверна,
resource transitions, SQLite, восстановление/призыв Эхо и fixed HP NPC/boss.
Конкретный fixture сохранения:52/104 HP при20 STR →100/200 после смены баланса;
доля и ledger сохраняются после SQLite reopen. Два боевых теста больше не
полагаются на прежнее число ударов до смерти: отдельный удар проверяет уже
мёртвую цель, а encounter имеет больший bounded лимит попыток.

```powershell
dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Server.Combat|FullyQualifiedName~Server.Stats|FullyQualifiedName~Server.Data|FullyQualifiedName~CharacterStatsAuditTests|FullyQualifiedName~ZeroStartTests|FullyQualifiedName~RespecTests|FullyQualifiedName~RespecIntegrationTests|FullyQualifiedName~Tavern|FullyQualifiedName~Resource|FullyQualifiedName~EchoRecovery|FullyQualifiedName~EchoRuntimeTests|FullyQualifiedName~NpcSimulationTests|FullyQualifiedName~BossSimulationTests|FullyQualifiedName~StatAllocationTests|FullyQualifiedName~ProgressionAndManaTests' --logger 'trx;LogFileName=health-5-10-final.trx' --results-directory .artifacts/character-stats --logger 'console;verbosity=quiet'
```

Свежая матрица594: `.artifacts/character-stats/health-5-10-audit/`, content SHA-256
совпадает с текущим source. Полная регрессия и UI render на balance22 отдельно
не повторялись. Работающий сервер применит новые коэффициенты после restart.
