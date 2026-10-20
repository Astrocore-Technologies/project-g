# Character & Stats C2 — ресурсы и регенерация

Дата: 2026-10-20. Срез реализован; финальные результаты проверок приведены ниже.
Пользовательская приёмка C2 отдельно от реализации. C3 (PvE EXP penalty и
таверна), respec, новые источники EXP и достижения этим срезом не закрываются.

## Принятые уточнения

Ответы пользователя записаны в [gameplay contract](../design/gameplay-contract.md):

- При oldMax=0 новый положительный Max MP/Stamina появляется с текущим запасом 0.
  Его заполняют обычное восстановление или явная операция пополнения.
- Если у суммарной отрицательной HP-регенерации нет однозначного источника,
  причина смерти — «Отрицательная регенерация», без игрока-убийцы.

## Реализация

`Combat/ResourceTransition.cs` содержит общую арифметику доли, signed flow и
границ полного/пустого ресурса. `RefreshEffectStats` применяет переход игрока
один раз после пересчёта equipment/passive/effect; Ability использует тот же
объект MP/Stamina и повторно ничего не масштабирует. Allocation и износ
оборудования входят через существующий путь конфигурации. Изменение абсолютного
HP при смене максимума не вызывает damage/heal procs, не сбрасывает cooldown и
не воскрешает. NPC/Эхо сохраняют прежний clamp максимумов: отдельное правило
смены их ёмкости из GDD персонажа не выводилось.

HP/MP больше не накапливают скрытую секунду до применения. Каждый отрезок
общего combat timeline интегрирует свой rate; изменение/истечение эффекта или
пассивки не применяется задним числом. Combat-tag passive имеет границу
simulation time, а истечение по внешним часам сохраняется. Полные ресурсы с
отрицательным rate остаются в scheduled sets; пустые MP/Stamina засыпают до
изменения состояния. Явное пополнение снова будит отрицательный flow.

Существующие условия применения сохранены:

| Ресурс | Условия |
| --- | --- |
| HP | Живой активный actor; returning NPC приостанавливает обычный regen по D102 |
| MP | Живой активный actor с разрешённым connected recovery |
| Stamina | Живой активный actor, задержка D101 после расхода; существующие паузы Block, stationary cast, control/stun/recovery и casting Мечника |
| Offline | Восстановления и отрицательных ticks нет; оставшееся disconnected body следует существующему lifecycle |

Эти gates одинаково ограничивают signed rate соответствующего ресурса;
дополнительных правил in-combat/голода/усталости не введено.

Channel и суммарная регенерация MP используют общую границу оплаченного времени.
Положительный regen при полной полоске может оплатить работу того же отрезка:
короткий внутренний credit не публикуется, не сохраняется и не переносится в
следующий отрезок. Нет лишнего pulse при исчерпании MP. HP=0 от regen завершает
обычный lifecycle до следующего действия: DeathId, остановка Channel, снятие
обычных эффектов, журнал и существующая обработка смерти выполняются один раз.
Потеря HP записывается с причиной regen, без Hit/crit/defense/proc pipeline.
Последнему PvP-атакующему не присваивается убийство от агрегированного regen;
существующая mixed-PvP классификация риска/дропа сохраняется. Правильный новый
PvE EXP penalty остаётся задачей C3.

## Сохранение и сеть

В CharacterState v2 и SavedEchoCombat v1 добавлен optional server-only
`SavedResourceMaxima` v1. Он хранит фактические прошлые максимумы и проверяет
версию, конечность и границы. Login сначала проверяет ссылки на definitions и
learned passives, восстанавливает текущую конфигурацию без offline ticks, затем
один раз применяет сохранённую долю игрока. Истечение offline buffs, смена
условий пассивки и новые численные максимумы контента не дают случайного refill.
Для Эхо сохраняется прежняя политика текущих запасов.

Документы без ResourceMaxima читаются через прежний `StatsAtSave` с validation
эффектов/пассивок. Исторический баланс такого legacy-документа восстановить
невозможно; это не converter произвольных старых балансных версий. Невалидные
границы отклоняются, данные не обрезаются молча для «ремонта».

SQL schema остаётся 12, content schema 25/balance 19, wire protocol 78.
Wire layout и delivery не менялись. Подтверждённый итог HP следует существующей
канонической последовательности CombatState после промежуточных hit events,
чтобы поздний heal/respawn не перезаписался ранним hit того же tick. Запись
ресурсов завершается до публикации по ADR 0008; при заблокированном commit
клиент не видит новую смерть и её журнал. Рабочая `.data` БД не открывалась.

## Проверки

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false` — успешно.
- Целевой прогон после исправлений — **267 passed / 0 failed / 2 skipped**.
  Включены resource transitions, full/partial/empty flow, zero capacity,
  одиночный/крупный временной шаг, expiry эффекта и combat passive, Channel,
  HP-price, общая смерть, PvP credit, inventory/passives/allocation, offline,
  JSON и SQLite, реальные UDP и checkpoint gates.
- Полная последовательная регрессия — **2020 passed / 0 failed / 7 skipped /
  2027 total**, 7 мин 17 с. 6 opt-in PostgreSQL и 1 opt-in рабочая Development
  копия пропущены. Команда: `dotnet test Content.Tests/Content.Tests.csproj
  --no-build --no-restore --logger 'trx;LogFileName=c2-regression-final.trx'
  --results-directory .artifacts/character-stats`. Для стабильных UDP timings
  на время запуска в output добавлен `xunit.runner.json` с
  `parallelizeTestCollections=false`; файл удалён в finally и его отсутствие проверено.
- Content validation с `--RegionExports:VerifySources=false` — успешно;
  schema 25, balance 19, 8 weapons / 18 abilities / 5 creatures.
- Обычный `--validate-content` останавливается на отсутствующем
  `Content.Client/Scenes/Regions/Linvel/GardenValleys.tscn` в текущей работе
  над картами. Экспорты проверены, соответствие исходным сценам не подтверждено.
- Godot GUI не запускался: этот срез не меняет клиентские сцены/UI.
  Live PostgreSQL и рабочая копия Development персонажей не проверялись.
- Сохраняются прежние build warnings NU1903 SQLitePCLRaw.lib.e_sqlite3 2.1.11
  и NU1900 недоступности audit endpoint; зависимости этим срезом не менялись.

Первый полный прогон выявил старые утверждения о clamp/секундном regen и
проверку ссылки на passive, которую требовалось сохранить при новом контексте.
Ожидания обновлены; тесты точной цены/симультанных попаданий используют fixture
с отключённым regen, чтобы по-прежнему проверять свой исходный инвариант.
UDP gate ждёт фактический `BlockedSave`, а не любой уже выполняющийся save.

Артефакты: `.artifacts/character-stats/c2-targeted-final.trx`,
`.artifacts/character-stats/c2-regression-final.trx`.

## Частота checkpoint

`ResourceFlowIntegrationTests.EveryChangedResourceTickCommitsAndIdleFullTicksDoNotWrite`
выполняет настоящие SQLite WAL/FULL commits на отдельном файле: один персонаж,
40 fixed ticks по 0.05 s, неполные HP/MP, затем 40 полных idle ticks.
Результат — **40 commits за 2 секунды симуляции, затем 0 записей**. Прежний
секундный HP/MP-only график требовал бы 2 checkpoint за тот же интервал;
это сравнение графиков, не отдельный измеренный baseline старого бинарника.
В финальном последовательном прогоне: median **10.578 ms**, p95 **12.450 ms**,
max **19.824 ms** на checkpoint. Это локальная Windows/.NET 10 среда с
изолированным SQLite, один actor; аппаратный performance baseline не снимался.

Ни глобальный scan игроков, ни отложенная запись уже показанного ресурса не
добавлены. Частота записей и канонических resource updates выросла; regional
нагрузка и bandwidth этим коротким тестом не доказаны. ADR 0008 не обходился
ради оптимизации, load budgets 200/400 не объявляются проверенными.

## Где настраивать и как проверить

- Баланс максимумов/rate — server content: `balance`, creature base values,
  modifiers equipment/passives/effects. Отдельных спрятанных чисел в runners нет.
- Старые `CombatOptions.*RecoveryIntervalSeconds` остаются совместимыми ключами
  конфигурации, но больше не управляют шагом применения ресурса.
- В игре: получить урон, запомнить долю HP, надеть/снять увеличивающую Max HP
  броню. Доля сохраняется с учётом естественного regen за прошедшее время;
  cooldown продолжает идти. При увеличении Max MP через WIS работает та же доля.
- Для negative regen использовать отдельную копию server content и отдельную
  Development SQLite: в modifiers профиля `test_adventurer` задать, например,
  `healthRecovery: -20`, `manaRecovery: -20`, `staminaRecovery: -30`.
  Это демонстрационные modifiers, не изменение основного баланса. Через
  `--Balance:ContentPath=<копия>` проверить уменьшение полосок, остановку MP/SP
  на 0 и причину HP-смерти в журнале. Reconnect не создаёт нового DeathId/штрафа.
  Запуск/проверка основной карты остаётся зависимой от её актуальных exports.
- Точный воспроизводимый серверный сценарий: `dotnet test
  Content.Tests/Content.Tests.csproj --filter ResourceTransitionTests`; UDP/SQLite:
  `--filter ResourceFlowIntegrationTests`. Все файлы БД этих tests изолированы.
