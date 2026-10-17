# Боевые Эхо — подключение §13 GDD 01

Источники: `gdd-01.md` §13, решения D096–D098 и D109–D111 в `implementation.md`. Этот план не определяет гачу, развитие, характеры и систему отношений из GDD 02.

Актуализация 2026-10-17: coordinator, общие combat/ability/motion, owner commands/UI, печать, AI/follow, refusal boundary и persistence подключены; runtime включён без автоматической выдачи. D123 переводит живых Active в Ready после crash с новым summon. Signature использует 3D intent/current entity ID и собственные Aim/Release/Cancel, wire73; profiles сегментируются атомарно. Ниже сохранена последовательность разработки: промежуточные версии и слова «следующее подключение» относятся к соответствующему срезу. Текущая приёмка — в `implementation.md`.

## Подтверждённый контракт

- Не более трёх одновременно активных тел; это не лимит владения Эхо.
- Призыв каждого — отдельное обычное действие владельца: 10 с без Cast Speed, 20 MP, движение 0, поворот разрешён, без дополнительного cooldown. Оплата и остановки по общему Cast 0/50/100%; тело появляется только при успешной активации.
- Погибшее Эхо не становится доступным автоматически. Одна «Печать восстановления» вне In Combat переводит его в готовность с полными ресурсами; затем нужен обычный призыв.
- Отзыв/удаление владельца сохраняет ресурсы, cooldown, ACR и ещё действующие эффекты. Повторный призыв не лечит. Disconnect до удаления владельца не останавливает симуляцию.
- У каждого тела собственные HP/MP/Stamina, действия, защиты, контроль и Threat. Contribution относится владельцу, фактический source остаётся Эхо. Signature запускает владелец, AI её не выбирает.
- Отказ допустим только для 5★; решение об отношениях приходит из отдельного server-only интерфейса. Combat исполняет результат и не выдумывает скрытые условия отношений.
- D112 задаёт первый профиль Миры: семь stat=10, base HP 100 / MP 60, общие ресурсы, движение 5,5, hurtbox 2/0,4 и Basic учебного меча; manual Signature — Instant GroundArea Range 6 / Radius 2 / 1×MagicATK / 10 MP / cooldown 8 с.
- D113 задаёт follow 1,5 м, teleport при >30 м либо недоступном маршруте 3 с, destination в пределах 2 м от владельца после завершения действия/контроля, без пополнения ресурсов.
- D114 продлевает In Combat владельца при враждебном контакте Эхо, сохраняя отдельный Threat. Живые Эхо продолжают бой возле тела умершего владельца до его удаления; ручные команды мёртвого владельца недоступны.
- D115: summon является magical Cast, Silence запрещает начало и прерывает подготовку с 50% оплаты. D116: PvP связан с владельцем при сохранении геометрии отдельного тела; Echo death исключает player death penalties.
- D117: первый AI Миры реагирует на hostile contact с семьёй владельца, выбирает ближайшего такого противника в радиусе 7 м от владельца и использует Basic; без цели следует. D118: Taunt на Эхо использует AI duration/Targeting ACR, без player damage penalty.
- D119: обычное полное удаление владельца переводит живых Эхо в Ready, без лечения/сбросов; требуется новый summon. Reconnect до удаления сохраняет bodies; региональный transfer переносит active Echo. Crash restore active bodies пока ожидает отдельного ответа.
- D120: вовлечённый противник забывается после выхода дальше 7 м от владельца; повторное вступление требует нового hostile contact. D121: мгновенный добровольный отзыв разрешён только вне In Combat владельца, с сохранением состояния.
- D122: реакция отказа 5★ на уже начатое действие задаётся явно для конкретного Эхо; универсальный default Finish/Interrupt не подключается.
- D123: crash переводит живые Active Эхо в Ready с сохранением ресурсов/cooldown/ACR; требуется новый summon 10 с / 20 MP. Общий restore выполняет завершение поддержания и запуск отложенного cooldown при входе, без пополнения ресурсов.

## Модули и границы

`EchoSimulation` становится координатором owned records, активных тел и решений AI. В нём не остаются собственные формулы урона, отдельный instant-hit путь или второй боевой clock. `EchoActor` связывает persistent instance, runtime entity, NavigationMover и общий Combatant. CombatSimulation и AbilitySimulation выполняют обычные атаки, skills, defenses, ресурсы, эффекты и смерть.

Summon использует тот же ordinary lane, input arbitration, ActionInstance и механизм Cast, что и навыки. Его server-only request связывает применение с конкретным принадлежащим владельцу Эхо; обычный AbilityCommand не получает обход проверки ownership/loadout. Операция активации повторно проверяет жизнь владельца, ресурсы, владение, состояние Эхо и свободное место среди трёх тел.

AI возвращает намерения для common runners. Поиск целей использует spatial query и решение обновляется только у активных/разбуженных акторов. Follow/navigation, combat decision и relationship permission разделены небольшими политиками. Сигнатура имеет отдельный признак manual-only. Значения первого боевого профиля и пороги следования утверждены D112/D113; отдельные открытые gameplay-переходы проверяются до подключения.

PvP/support работают по принадлежности Эхо владельцу, сохраняя самостоятельное тело как source/target. Внутри семьи owner/Echo не возникает friendly fire. Threat записывается на runtime Эхо, contribution credit — на persistent character. Released projectiles/DoT сохраняют обе identities после удаления тела.

## Состояния и сохранение

Состояния экземпляра: Ready → Summoning → Active → RequiresRestoration → Ready. Отказ от помощи — отдельное ограничение доступности/AI, не подмена состояния смерти. Неуспешный призыв не создаёт тело и не меняет чужое Эхо. Повтор команды не дублирует призыв или списание печати.

Persistent combat record содержит instance/definition identity, ресурсы и stats-at-save, cooldown/charges, defense/ACR и жизненный статус. Runtime network ID туда не попадает. Эффекты сохраняются отдельными bounded rows, а не ограниченным списком внутри одного Echo JSON: GDD не разрешает вводить cap разных баффов из-за размера документа. Все изменения owner/resources/Echo/consumable/effects входят в один уже существующий fenced character checkpoint.

Старый SavedEchoes v1 содержит только до трёх active записей без HP и смерти. Его adapter должен сохранять UUID и делать явную версионную конверсию к новой модели; старое автоматическое появление/плоский урон не являются допустимым fallback. Working DB при разработке не открывается. Irreversible conversion не запускается без отдельной проверки существующей авторизации пользователя.

Для большого owned roster нужны отдельные записи и owner-only страницы; предел трёх active тел не превращается в предел owned records. Persistence расширяется внутри прежней database strategy/process boundary. Выдача новых Эхо и печатей остаётся внешним интерфейсом, без новых acquisition/price/loot правил.

## Client / Shared

Shared хранит bounded commands/results, публичные состояния и компактные enum. В commands передаются owner-bound handle/slot и ручной 3D aim, не результат summon/damage. Persistent UUID, AI/отношения и скрытые definitions остаются на сервере.

Owner UI показывает доступность/смерть, цену/время призыва, восстановление и причину отказа. Общие CombatState/ActorLifeState/ControlState/effects presentation обслуживают тело Эхо. Другие игроки видят только актуальные AOI entities, cast/effects и подтверждённые результаты. Ответы об успешном призыве/восстановлении появляются после commit.

## Последовательность и проверка

1. Подключить generic companion combat identity, владельца для PvP/support и самостоятельный Threat/credit. Проверить friendly fire, control/damage/defense, смерть и source-independent effects.
2. Добавить versioned combat records и общий capture/restore для ресурсов, cooldown, ACR и effect rows; проверить v1 adapter и отсутствие offline ticks/пополнения.
3. Подключить Summon к ordinary action runner, отмены/срыв/смерть и ресурсную оплату. Проверить три тела, последовательные 10 с, duplicate commands и checkpoint barrier.
4. Подключить восстановление одной печатью вне боя, idempotency и отсутствие auto-summon. Проверить crash/rollback, повтор и нехватку предмета.
5. Перевести следование и AI на common actions, manual Signature и relationship interface. Утвердить недостающие числа/переходы до включения.
6. Обновить owner UI/AOI/wire, пройти реальный Godot/UDP сценарий с тремя Эхо, death/restore/resummon и disconnect body.

Критерий закрытия B11 — рабочий сквозной сценарий §13 и §16. Наличие этого плана или отдельных DTO не означает завершения раздела.

## Подключённые части

Common domain: companion identity/family/credit, общий AI Basic/skills, owner In Combat, отдельный Threat, Taunt AI semantics. Мире заданы редактируемые creature/Signature/follow profiles в content schema 21 / Balance 17; ManualOnly запрещает Signature через AI ingress. Общий Summon runner входит в ordinary input arbitration и combat clock, ActionInstance исполняет fixed 10 с и 0/50/100% MP. Новый coordinator подключает activation callback к common body, navigation входит в общий timeline. Старый flat-hit путь удалён.

Persistence: additive schema 11 создаёт отдельные owned rows ≤8192 UTF-8 bytes без лимита владения. SavedEchoCombat v1 хранит readiness/life, active slot, ресурсы, cooldown/charges, defense/ACR, facing и собственный timestamp. TargetEcho в effect document v1 связывает отдельные эффектные строки с экземпляром. CompanionRuntimeState использует общие capture/restore; offline ticks и ресурсное пополнение не выполняются. Отключённая система сохраняет roster/effects и их исходный clock. Неизменный retained roster не переписывается каждым character checkpoint; перед production active roster нужен такой же пропуск неизменных записей/row delta.

EchoSimulation заменена coordinator с owned records, v1 adapter → Ready, lifecycle, общим body/navigation/Signature/AI и отзывом вне боя. Wire 69 добавляет owner-only страницы по 8 записей и команды summon/recall через session handles; это размер пакета, не лимит владения. Клиент получает список и явные кнопки. Первое UDP доказательство explicit summon/Signature и publication после checkpoint прошло в составе targeted прогона; региональный fixture исправляется отдельно. Production flag пока выключен. Нужны: завершить проверки lifecycle/recall/регионов, restore seal consumption, row delta, relationship boundary и сквозная Godot проверка Эхо. DB migration проверяется на изолированных базах; working DB не открывается.

## Детали следующего подключения

- Заменить internals EchoSimulation: owner registry хранит owned records, активный Actor связывает UUID, runtime ID, NavigationMover и Combatant. Старые flat damage/timers не остаются fallback. Не создавать вторую формулу damage или отдельный simulation clock.
- SummonEligibility проверяет принадлежность/Ready/три active slots; SummonActivated создаёт тело, BindCompanion и AbilitySimulation.AddActor, затем общий CompanionRuntimeState.Restore. UUID разрешается сервером из owner handle; клиент не выбирает database ID.
- Начальное persistent BindWorldActor обязано завершиться до publication/actions. Owner index уже обновляет family/credit построенных bodies. Released snapshots сохраняют persistent identities независимо от последующего удаления.
- Подготовку/движение Echo включить в PrepareCombatMotion, NextCombatMotionBoundary и AdvanceCombatMotion; текущие world hooks StopControlledMotion/StartControlledDash/Flee должны уметь получать Echo mover. Только post-frame AI без общего motion timeline не обеспечивает корректные projectile/contact проверки.
- На Defeated пометить RequiresRestoration; физическое удаление отложить до конца contact batch/frame, чтобы не удалять target из середины damage/proc resolution и не отзывать захваченные контакты. Снять из AOI и common actors после capture оставшегося состояния.
- D119 требует отдельного capture mode: periodic checkpoint сохраняет Active, обычное окончательное удаление владельца сохраняет Ready, regional cargo переносит Active. Для обычного удаления сначала остановить sustain actions общим Departure и зафиксировать начавшиеся end cooldown, затем capture и fenced commit. Не трактовать каждое CaptureCharacter как logout. Crash active restore пока не утверждён.
- D117 AI просыпается от HostileContact/движения владельца, хранит только вовлечённых противников, выбирает spatial candidates в authored CombatRadius. Taunt использует общий AiTauntTarget и допустимость цели. Basic идёт через ExecuteAiBasic, Signature — только owner command. Не использовать старые assist timeout/flat power как новый баланс.
- Нужен owner-only paged roster с bounded размером страницы и session handles; размер страницы не ограничивает ownership. Отдельные команды summon/restore/cancel/recall проверяют owner/state/sequence, а restore дополнительно expected death ordinal, чтобы прежняя команда не потратила новую печать после следующей смерти.
- Печать — отдельное server item definition/consumable operation без нового acquisition/price/shop. Выбор и расход одной принадлежащей печати, Ready/full resources и owner checkpoint должны коммититься вместе. Нельзя публиковать расход или восстановление до commit. Cooldown/ACR не сбрасываются неоговорённым образом.
- Перед включением active roster перейти от переписывания всей коллекции к changed owned rows/removals в том же transaction fence. Текущий value cache избегает повторной SQL записи неизменного retained roster и не полагается на неизменяемость публичных массивов.
- Для отношений использовать server-only permission interface; точные причины/пороги 5★ из GDD 02 не выдумывать и не отправлять клиенту. Политику остановки уже начатой способности при отказе нужно утвердить до её подключения. Доступность добровольного отзыва в бою также не выводить из старого прототипа.

## Проверенное подключение coordinator / wire 69

Призыв, manual Signature, AI Basic и движение работают через общие combat/action/motion runners. D120 задаёт забывание за 7 м, D121 — отзыв только вне owner In Combat. Owner roster показывает Ready/Summoning/Active/RequiresRestoration, а Summoning раскрывает только sequence/execution текущего действия для точной отмены, в том числе после reconnect. Для этого ID120 EchoSummonControlCommand отдельный от skill cancel, поэтому совпавшие sequence разных типов не отменяют чужое действие. Временный diagnostic guard при исследовании follow удалён; fixed AI decision interval больше не сбрасывается каждым движением владельца.

Targeted `echo-runtime-final-targeted.trx`: 52 passed и один timeout старого continuous-input fixture; тот ожидал частный sequence floor как поток ack, хотя он отправляется только при входе. Fixture теперь проверяет фактическое потребление release и начало execution, сохраняя разрешение уже принятому удару дойти до контакта. Полная итоговая регрессия `echo-runtime-regression-final.trx`: **1829 passed / 0 failed / 7 skipped / 1836 total**. Реальные региональные UDP сценарии выполняют по три последовательных десятисекундных призыва, а не получают тела при login. Обычный reopen сохраняет Ready, региональный переход — Active, reconnect живого disconnect body сохраняет активные тела.

`dotnet build Game.slnx --no-restore -m:1` — 0 ошибок / 5 прежних dependency warnings; последняя дополнительная tests-only сборка — 0 ошибок / 4 прежних warnings. Content validation с исходной source verification и временным актуальным surface package — Schema21 / Balance17 / Weapons8 / Abilities18 / Creatures5, exit 0. Godot `LifeSmoke.tscn` — **ECHO_UI_OK + LIFE_UI_OK**: owned Ready/dead rows, явные summon/recall/cancel controls, общее combat body/HP/animation и cleanup. Первый запуск Godot внутри sandbox завершился signal11 до старта сцены; повтор вне sandbox прошёл, как и финальный повтор. Working DB и production surface exports не менялись.

Следующий persistence patch: optional `DatabaseEchoChanges(Upserts,Removed)` внутри прежнего DatabaseSave и transaction fence. Domain value cache сравнивает JSON по InstanceId и обновляется только после commit; provider пишет только изменённые строки и удалённые UUID, не удаляет/создаёт заново весь roster. Full replace API сохраняется для явной полной записи; нельзя одновременно передавать replace и delta. SQLite/Postgres используют composite owner/instance conflict target, сохраняя глобальный UNIQUE instance_id от присвоения чужого Эхо. Нужны проверки trigger-count (один update среди большого roster), удаления/добавления, чужого UUID и полного rollback при stale fence. Это не SQL migration и не смена DB strategy.

Перед закрытием §13 дополнительно проверить Root (запрещает движение, а не Basic) в coordinator и отсутствие дополнительного AI cadence cap от perception interval. При восстановлении печатью full resources рассчитываются общими stats/effects, без offline regen; нужны expected DeathId, фактический расход одной принадлежащей печати и тот же fenced checkpoint. Production flag выключен до завершения оставшихся требований.

## Текущий срез: печать восстановления / wire 70

Content schema22 / Balance18 добавляют server-only consumable UseKind и echoRecovery (одна печать), без starter grant, магазина или новых acquisition правил. Recovery использует common restore/capture на transient entity вне spatial/AOI с отдельной source identity, затем заполняет текущие максимумы ресурсов. Сохраняются DeathId, cooldown, ACR и ещё действующие эффекты; нет offline ticks или автоматического summon. До списания предмета подготовка завершается полностью; расход inventory и Ready/effects входят в один fenced checkpoint. Expected DeathId предотвращает повтор для другой смерти, owner-bound handle не адресует чужого Эхо. Command обрабатывается после боя текущего fixed frame; In Combat, смерть владельца, торговля и ещё не опубликованное мёртвое тело запрещают операцию.

Wire70 IDs121/122, private roster передаёт DeathId и публичную стоимость восстановления; страницы с восемью самыми длинными именами укладываются в regional MTU. Inventory допускает Slot.None только без экипировки и бонусов. Godot F3 показывает восстановление, инвентарь различает расходуемый предмет и не позволяет его экипировать. Изменяемая стоимость берётся из серверной страницы.

`echo-recovery-acceptance-final.trx` — **36 passed / 0 failed**, включая настоящий lossy UDP/checkpoint barrier, приватность и SQLite stale-fence rollback actual seal + Echo. Проверены истёкший Max debuff, оставшийся buff/ритм тиков, старение cooldown и ACR, отсутствие расхода при отказах и обязательный новый summon. Дополнительный итог после удаления старого ApplyEchoDamage bridge — `echo-recovery-common-cleanup.trx`: **60 passed / 0 failed**. Старые PvP/social/training tests теперь используют bound common Echo body. Native Godot LifeSmoke — **ECHO_UI_OK + LIFE_UI_OK**, включая recovery button. Последний Game.slnx build — 0 ошибок / 5 прежних dependency warnings, content validation Schema22/Balance18 — exit0; diff check чистый.

Последний полный объединённый прогон перед recovery: `echo-persistence-ai-regression.trx` — **1834 passed / 1 failed / 7 skipped / 1842 total**. Единственный ranged fixture читал Defense до его получения на login; ожидание baseline исправлено, `echo-ranged-bootstrap-final.trx` — **2 passed / 0 failed**. Новый полный прогон после recovery/permission patch ещё нужен; отдельные результаты нельзя выдавать за один зелёный full.

D122: nullable server-only Stars / RefusalDuringAction и IEchoCombatPermissionSource подключаются к fixed-frame common action eligibility. 5★ требует явно выбранной реакции, универсального отказа у других редкостей нет; shipped Mira не получает выдуманную редкость. Latest patch компилируется, но отдельные refusal tests/все случаи UI/region ещё не выполнены: boundary не считать завершённым. Источник отношений обязан отдельно хранить/восстанавливать решения и уведомлять coordinator; combat не придумывает thresholds/reasons/durations GDD02. Production Echo flag остаётся выключенным; crash Active policy по-прежнему ожидает решения.

## Следующие проверенные уточнения

Changed-row persistence подключена через optional `DatabaseEchoChanges`, без SQL migration: SQLite/Postgres выполняют owner-bound upsert/removal в прежнем fence. Domain сравнивает durable JSON по UUID и меняет cache только после commit. `echo-row-delta-targeted.trx` — **48 passed / 0 failed**; SQL triggers на 50 owned records подтверждают ноль записей при неизменном roster, ровно один UPDATE для одного ресурса, отдельные INSERT/DELETE, rollback/cache retry и запрет захвата чужого UUID. Live PostgreSQL по-прежнему требует внешнего окружения.

AI Basic использует common continuous runner с server-only намерением, повторно проверяемым на motion boundary. Infinity lease существует только внутри server AI и не доступна через player ingress; perception interval больше не ограничивает cadence, а движение/видимость/состояние прекращают намерение без отмены уже принятого удара. Root запрещает route, сохраняя разрешённую атаку. `echo-ai-cadence-targeted-final.trx` — **71 passed / 0 failed**, включая interval 0,03 с при world step 0,05 с и perception 0,2 с.

Обычный recall не применяет logout-removal эффектов. Ready record хранит optional `PendingLogoutRestore` в прежнем model v1: если игрок вошёл и перешёл регион до первого summon, marker переносит необходимость применить login policy вместе с исходными effects/maxima metadata. После реального body restore/capture он сбрасывается. `echo-lifecycle-effects-targeted.trx` — **46 passed / 0 failed**, включая recall → регион без потери non-logout effect и login → регион → summon с корректным удалением. Cooldown/effect clocks не обновляются искусственно для неактивных записей.

Native player input regression на wire69 — **COMBAT_INPUT_OK**, `.artifacts/combat-input/ded020730c3c473b898665fadfb455c6`. Общая регрессия 1829/0/7 выше предшествует этим трём уточнениям; их полный объединённый прогон ещё требуется.

Для seal recovery подготовка должна переиспользовать common restore/capture, чтобы не дублировать aging cooldown/charges/ACR/effects или заполнять ресурсы по устаревшему Max. Практический вариант: редкая transient common entity вне spatial/AOI, без продвижения simulation clock, с отдельной временной source UUID. Restore получает копию saved record/effect targets с этой временной target identity, применяет offline/login policy, затем ресурсы заполняются до вычисленных максимумов и capture возвращает новую Ready запись/aged effects. Перед cleanup original instance UUID/TargetEcho восстанавливаются в подготовленных immutable records. Временная source identity нужна, чтобы cleanup не завершал независимые эффекты исходного Эхо на других существах. Никаких damage ticks, AI, публикации или оплаты до успешной подготовки; затем расход одной принадлежащей печати и присвоение Ready snapshot идут атомарно в прежний character checkpoint.
