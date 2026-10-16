# Combat runtime — реализация GDD 01

Источник поведения: [GDD 01](../design/combat-system/gdd-01.md) и [решения пользователя / реестр реализации](../design/combat-system/implementation.md). Этот документ описывает целевую внутреннюю структуру. Он не означает, что все перечисленные модули уже реализованы. Фактический статус хранится в реестре.

## Неизменяемые границы

- `Content.Server`: определения контента, состояние боёв, проверки, геометрия, броски, ресурсы, эффекты, AI, ACR, Threat, lifecycle и persistence.
- `Content.Shared`: версии и ограниченные сетевые сообщения, публичные enums/IDs, безопасные геометрические примитивы без Godot и скрытого контента.
- `Content.Client`: намерения, предварительная анимация/области, reconciliation, подтверждённые исходы, VFX, HUD и журнал. Клиент не назначает цель попадания, урон, стоимость или успех защиты.
- Существующие Godot/.NET/LiteNetLib, fixed tick, региональный AOI и database adapter остаются. Нового процесса или правила распределения мира не требуется.
- ADR 0008 сохраняется: изменения persistent state фиксируются атомарно до публикации. Transport продолжает polling во время барьера сохранения; gameplay ждёт commit.

## Поток выполнения

`Input ingress → bounded intent → action arbitration → action phases → geometric candidates → numeric evasion → eligible defense → critical / defense / block → absorption → HP/effects → persistent checkpoint → replication / presentation`.

Входные команды несут sequence, client tick и направление/точку/намерение цели. Runtime entity ID, persistent character ID, input sequence и server execution ID остаются разными идентификаторами. Один `BeginContinuous` даёт несколько execution ID; heartbeat обновляет ввод, а не создаёт атаку.

Transport buffer хранит одну команду действия и управляющее состояние удержания. Отпускание не теряется при объединении heartbeat. Новое нажатие заменяет старое отложенное нажатие. Это технический буфер доставки, отдельно от одного логического gameplay input buffer с приоритетами Dodge/Parry → Block → skill → basic.

## Модель действия

`ActionDefinition` — server-only данные: stable ID, категория, способ наведения, форма выполнения, геометрия, фазы, правила движения/поворота, окна отмены, ресурсы, cooldown/charges, допустимость защит и payload эффектов. Наведение и способ попадания не выводятся друг из друга.

`ActionInstance` — серверный execution ID, actor, исходный input sequence, фаза и временные отметки, актуальное/зафиксированное направление согласно данным, состояние оплаты и активации. Единственный action runner владеет обычным действием актора. Постоянные эффекты и уже выпущенные снаряды живут отдельно от этой полосы исполнения.

Фазы: Preparation → Activation → Active → Recovery → Finished. Отсутствующая фаза имеет нулевую длительность. Instant, Cast, Channel, Charged, Toggle и Hold используют общий переходный механизм; конкретные правила фаз задаются данными. Passive не создаёт ActionInstance и не захватывает обычное действие.

Реализованный D066: `AbilityKind.Passive` / `Action.Form.Passive` требуют `PassiveDefinition` без активной геометрии, цены, cooldown, фаз и contact payload. Изучение включает пассив вне панели; ручная команда и AssignSlot отклоняются сервером. `CombatSimulation.Passives` применяет derived modifiers к базе экипировки перед timed effects и выдаёт явные grants для DoT criticals / unarmed Block. Условия — выбранные weapon IDs, profession IDs, required/excluded environment tags, Alive и InCombat; группы объединяются AND, альтернативы внутри группы — OR, отсутствие ограничения означает любое значение. Изменение progression/уровня, экипировки/поломки, профессии, среды, life или combat state пересчитывает результат event-driven. Idle actors ради пассивов не сканируются. ScaleModifiersWithSkillLevel явно использует существующую progression-кривую; сами пассивы не получают новый неутверждённый способ освоения.

Подготовка экипировки/preview учитывает будущий weapon profile, чтобы снятие или поломка не оставляли его пассивные бонусы. Изменение максимума не пополняет HP/MP; подписчики получают общий refresh stats/resources. NPC используют тот же evaluator для пассивов из creature definition. Generic triggered procs и остальные взаимодействия профессий подключаются к общим событиям отдельными срезами; этот evaluator не заменяет отсутствующий общий action scheduler.

`CharacterState.ActivePassiveModifiers` — optional bounded список stable skill IDs, действовавших при checkpoint. Он служит проверке прежнего MaxHP/MP при загрузке; для каждого ID требуется изученный passive source. Он не восстанавливает старые условия: после входа условия проверяются заново, expired InCombat и новая среда могут уменьшить максимум без offline refill. JSON extension сохраняется атомарно с прежним character document; DB schema 10 не меняется. Отдельных durable effect rows для пассивов нет.

D067: незавершённая подготовка Cast/Charged после аварийного перезапуска не возобновляется, не создаёт основной payload и не списывает ресурс/заряд/cooldown. Она не сериализуется как выполняющийся ActionInstance; прежние committed ресурсы и cooldowns восстанавливаются обычным способом. Это отличается от reattach продолжающего симуляцию тела и от уже активированного Channel/Toggle с PendingEnd по D058/D063.

До активации добровольная отмена стоит 0% без cooldown, вражеское прерывание — 50% с cooldown. Активация — 100% с cooldown. Channel оплачивает фактическую работу без дополнительного штрафа 50%. Отмена не откатывает уже совершённое попадание или созданный эффект. Обычный урон не прерывает Cast.

`ChannelDefinition` явно задаёт maximum/null, cadence и её speed source, activation pulse, MP/Stamina rates, damage calculation и Activation/Completion cooldown. Поддержаны GroundArea pulses через общий hit/support/effect resolver и Self-Recovery pulses; другие геометрии Channel отвергаются каталогом до запуска. `ChannelTimeline` отделяет оплачиваемое непрерывное время от cadence: дробный ресурс покрывает только доступную часть шага, endpoint pulse выполняется только при оплаченном времени. Runtime оплачивает границы воздействий по очереди, поэтому смерть от первого отражения не списывает остаток большого шага. Каждый pulse имеет собственный execution ID для control/hit dedup, а weapon receipt принадлежит всему поддержанию.

Hold использует тот же Channel runtime: key press начинает действие, release останавливает его до следующей неоплаченной работы. Release до активации не платит и не расходует заряд; после активации сохраняются уже оплаченная стоимость/работа и запускается явно отложенный cooldown. Поворот во время active-фазы разрешает aim updates. Существующий sword_breath переведён на явный Hold: прежние 1.2 с, восстановление 25 Stamina и cooldown Activation заданы данными, раннее отпускание не восстанавливает Stamina.

Toggle использует тот же оплачиваемый timeline и отдельную `ToggleDefinition`: OccupiesAction и дополнительные StopOn (Movement/Skill/BasicAttack/Defense/EnemyInterrupt). Подготовка занимает обычную полосу. После активации фоновый Toggle освобождает `Combatant.Action`, сохраняя свой effect/timeline; публичная Phase.Sustaining отличает его от обычного Channeling и не запускает анимацию Cast. Другие действия и их recovery не принадлежат фоновому Toggle. Владельцу передаются Off/Preparing/Active, ownership и StopOn. Данные могут оставить действие в обычной полосе. Фоновый recovery, претендующий на неё, отвергается каталогом.

Повторная клавиша отправляет явное намерение Deactivate, а не неявный flip серверного состояния. Повторное Use уже включённого Toggle не создаёт ещё одно исполнение. Соседние намерения одного Toggle объединяются в последнее желаемое состояние до simulation и за commit barrier; быстрое включение/выключение не оставляет залипшее поддержание. Снятие с панели завершает Toggle до копирования cooldown в новый loadout (D065). Смерть, полное удаление и crash прекращают исполнение, прежние наложенные эффекты живут независимо (D063). Оплата и восстановление — D064/D058. Cooldown runtime находит текущий слот по stable skill ID, поэтому перестановка loadout не теряет резерв.

Silence/Disarm передают отдельное capability interruption. Оно выбирает подходящие исполнения независимо: магический background Toggle не делает физический Cast магическим, а физический Cast не скрывает Toggle от Silence. Hard control сохраняет общий interrupt event. Остановленные до конца simulation эффекты исключаются из публикации в том же checkpoint; released projectiles из этого исключения выведены.

Charged использует `ChargeDefinition`: fullSeconds, выбранный speed source, явное atFull Release/Hold, линейная кривая startPower/fullPower. `ActionInstance` не переходит из подготовки автоматически до release/явного auto-release. Сервер считает elapsed time; множитель влияет на урон и числовую power эффектов, а время контроля остаётся параметром его definition. Resources/charges/cooldown проходят общий барьер Cast D020. Снимок снаряда создаётся на фактическом выпуске. Client press/hold/release/Esc/ПКМ/focus loss создают только намерения; aim повторно проверяется на сервере.

Completion cooldown резервирует один заряд при активации без запуска его таймера; уже идущие интервалы других зарядов продолжаются. Поддержание/отмена/смерть не добавляют штраф 50%. Согласованный D031 расход заряда при срыве подготовки сохраняется. Окончание поддержания запускает deferred interval, затем используется recovery policy действия. Full reset восстанавливает также резерв; позднее завершение не списывает его второй раз. SavedCooldown.PendingEnd — optional additive поле старого character JSON, в том же checkpoint. После crash Channel не возобновляется, и по D058 его отложенный cooldown начинается при входе без offline credit. Обычное окончательное удаление персонажа завершает Channel до последнего checkpoint; forced disconnect продолжает симуляцию тела до удаления.

Общий chronological scheduler, обработка всех форм и общий input buffer остаются незавершёнными: отдельный Channel runner пока использует существующий порядок fixed-tick подсистем. Не заявлена полная приёмка B1/B6 или нагрузочная проверка произвольно высокой cadence.

Cooldown и execution lane независимы: отсутствие GCD и CDR=100% не разрешают параллельно выполнять обычные действия. Скорость не ограничивается скрытым минимумом в один tick. Планировщик обязан обрабатывать несколько последовательных активаций внутри tick, сохраняя порядок и отдельные execution ID. Защита от бесконечных zero-time циклов является техническим инвариантом планировщика; скрытый gameplay cap вместо согласованной формулы не допускается.

По D068 завершённый импульс фонового оружейного Toggle создаёт границу отложенного износа. Общий Inventory settlement сохраняет защиту ещё незавершённого обычного действия; при фактической поломке prepared equipment выбирает unarmed profile и тем же обновлением выключает требующие оружие фоновые Toggle. Их cooldown запускается штатным StopToggle, дополнительных импульсов/ресурсных списаний после остановки нет. Износ остаётся один раз за включение. AbilityOutcome/Availability.NeedsWeapon дают причину отказа безоружного запуска; HUD отличает её от NeedsSword и NeedsParry. Обычный занимающий полосу Channel сохраняет прежнюю границу завершения действия.

Оплата sustain на каждой границе читает актуальный AbilityActor: воздействие может изменить Passive/timed stats и заменить resource profile. Локальная устаревшая копия не используется для следующих списаний того же server step.

## Геометрия и разрешение попаданий

Broad phase использует существующий spatial index; narrow phase работает в 3D на серверных hurtbox и attack volumes / swept trajectories. Хитбокс актора зависит от боевого состояния, не от колебания визуальной анимации. Передний/боковой/задний контакт, airborne, prone и combo — теги результата. Бонус появляется только из явного определения действия/эффекта.

`HitResolver` принимает уже подтверждённый геометрический контакт и параметры действия. Он проверяет числовое уклонение до защит и крита; Block и Parry используют eligibility самого действия. Block Strength влияет на Stamina, а Block absorption — на урон. Ground AoE не блокируется; projectile может блокироваться, но не использует обычное melee Parry. Отрицательная защита усиливает урон. Armor Penetration отсутствует.

Снаряд проверяет swept collision с миром и допустимыми противниками, игнорируя союзников. Universal target cap для AoE не вводится: технические packet bounds реализуются разбиением сообщений, а не ограничением числа поражённых целей.

Реализованный срез D054–D057: текущие skill projectiles получают `DirectHitSnapshot` на границе выпуска после подготовки/активации. Он содержит силу, Accuracy с Blind, Critical Chance/Damage и целевой множитель Taunt; target Evasion, защита, guard и shields берутся при контакте. Для Block используется направление входящего снаряда, поэтому последующее движение источника не меняет сторону попадания. Числовые проверки остаются в общем direct-hit resolver.

`ReleasedSourceSnapshot` принадлежит bounded исполнению снаряда и сохраняет persistent source identity, runtime actor, позицию выпуска, параметры для новых эффектов и явный PvP projection. Смерть/отмена/удаление источника не удаляют летящий результат. После исчезновения/смерти источника authored DoT/control используют его release context; живому источнику новые timed effects по-прежнему рассчитываются в момент наложения. Принадлежность контроля/эффекта записывается через UUID, без фиктивного актора. Source-side steal/healing не воскрешает источник. Threat/contribution, offline source-side item/progression/legal mutations и все виды ranged basic ещё требуют общего завершения следующих срезов.

PvP permission проверяется заново при каждом возможном контакте. Пока источник находится в мире, берутся его текущие mode/party/position; после удаления — сохранённые mode/party/release position. Target mode/protection/safe zone актуальны всегда. Смерть цели от удалённого источника получает target-side legal attribution до общего death hook. Flying effects имеют собственную AOI relevance; клиент может показать и завершить их без caster node. Layouts wire v45 не изменены. Урон цели и finished event по-прежнему публикуются после checkpoint ADR 0008. Runtime projectiles не добавлены в durable effect rows.

## Эффекты и контроль

Effect runtime владеет экземплярами `(source, target, definition)`, стеками, refresh, временем окончания, периодической работой и флагами persistence/death/logout. Изменяемое состояние хранится отдельно от immutable definitions. Урон, лечение, shields, Cleanse/Dispel, steal/reflect/self-cost проходят через явные операции; специальные взаимодействия подключаются после согласования.

ACR принадлежит цели: пять независимых ledger, общий бюджет категории для всех источников. Заряд бюджета, пересечения, refresh, раннее снятие и перемещения следуют D013–D015. Неуспешная попытка не продлевает сброс. Immunity/resistance и семантика отдельных контролей не подменяются старым общим diminishing-returns счётчиком.

## Общие акторы и lifecycle

Player, NPC и боевой Echo используют одни проверки попаданий, защиты, статов и контролей. Echo получает собственные HP, действие, AI/target intent и Threat; contribution связывается с владельцем без дополнительных loot rights. Summon/restore и refusal interface ограничены §13; gacha/relationships не разрабатываются.

In Combat — отдельное серверное состояние на 30 секунд по D009, применимое к PvE и PvP. Оно не должно зависеть от включения системы репутации/PK. Forced disconnect и corpse lifetime реализуют 60 секунд по GDD, с отдельными runtime и persistent идентификаторами. Открытые детали lifecycle и durability фиксируются до подключения.

Реализованный срез: `CombatSimulation.Presence` хранит таймер и affected/dirty sets; `ServerWorld.Logout` проверяет запрос после боевой работы fixed tick. `GameServerService` удерживает актор и lease до финального общего checkpoint, затем публикует Accepted/despawn. Forced disconnect перепривязывает прежний актор к отдельному session handle на полные 60 секунд; движение, эффекты, получение урона и ресурсы продолжаются. Переходы состояния не зависят от packet arrival frequency.

`CombatSimulation.Life` владеет death ordinal, Dead и deadlines тела/respawn. Две записи sorted schedule на смерть не требуют сканирования мира. `SavedCombatLife` — versioned компонент в том же character checkpoint с HP; offline уменьшает сроки, но не выполняет respawn. PK-последствия остаются потребителем события смерти и учитываются даже при воскрешении в том же тике. `ServerWorld.Life` валидирует respawn и применяет серверную точку региона. D044–D046 задают 60 секунд до respawn, 50% HP/MP и полную Stamina при сохранённых cooldowns/ACR/прочности/persist-through-death; resurrection API допускается только за 60 секунд жизни тела, HP явно передаётся навыком, автоматической неуязвимости нет.

Жизнь/таймеры публикуются через AOI после checkpoint; команда respawn и её результат отделены от legal PvP commands. Общий `LifePresentation` не требует PvP UI. Тело скрывается при истечении срока; владелец продолжает видеть камеру, UI и действие respawn. Новая NPC spawn-жизнь получает новый runtime ID, прежнее тело сохраняет свой срок. Реестр NPC-акторов отделён от spawn-слотов: прежняя жизнь после воскрешения получает отдельный AI из того же профиля, movement/control, snapshots и telegraphs; новая spawn-жизнь работает независимо. Воскрешение до replacement отменяет pending respawn. Временные независимые NPC пока не имеют полной persistence после restart.

`SkillEffectApplication.Operation` различает Timed, Heal и Resurrect; `AbilityContactDisposition.Support` включает поддержку в GroundArea. Геометрия/LOS и D047 проверяются при воздействии, в том числе после смены группы за время Cast. Лечение использует общий Critical Chance/Damage и фактически восстановленный HP; воскрешение требует действующее тело и положительный HP из данных навыка, не выполняя дополнительный крит. Support area не создаёт враждебный direct hit. Другие support shapes и mixed-disposition payloads пока не реализованы и отвергаются каталогом, а не получают неявную механику перехвата.

Wire v43 добавляет `CombatFeedback` ID103: runtime EventId/ExecutionId, видимый runtime источник или 0, цель, tick, kind, положительный amount и critical. Сообщение bounded (46-byte body), ReliableOrdered по AOI цели после checkpoint; UUID и определения не публикуются. После промежуточных attack/ability/Echo hit events отправляется итоговый CombatState изменённого актора, затем feedback. Это сохраняет финальный HP при уроне и лечении в одном тике. Presentation различает лечение, воскрешение, shield absorption, steal, reflection и HP price; EventId подавляет повтор. Wire v45 также содержит закрытый журнал владельца и сохраняемые разборы смерти D048/D049; архитектура и пределы описаны в [combat-journal.md](combat-journal.md). Public timed instances ещё требуют интеграции.

D047: поддержка разрешена самому источнику и его группе; `AllowOtherPlayers` разрешает остальных игроков явно для skill payload. Проверка принадлежности выполняется сервером, а уже наложенные HoT/щиты сохраняют полученное разрешение и живут независимо от последующих изменений группы/источника.

Threat и contribution хранятся раздельно: первый выбирает AI-цель, второй учитывает участие. Damage/healing/control и Echo attribution должны использовать общие события resolver, а не дублирование логики в transport.

## Данные, сохранение и сеть

Content schema 10 содержит `unarmedWeaponId`: owned equipment preparation начинает с этого профиля и заменяет его только исправным надетым оружием. Отсутствие и поломка не возвращают creature fallback sword. D051 задаёт начальные числа безоружного профиля, а capabilities определяют зависимость basic от Weapon/Magical/Movement control. Чистые creature fixtures без item inventory продолжают использовать собственный WeaponId определения.

Block проверяет наличие исправного оружия независимо от Parry. Явный `allowsUnarmedBlock` профессии или timed beneficial effect разрешает исключение; guard lease ограничен окончанием разрешения, его потеря пересчитывается event-driven. Unarmed Parry следует обычным стоимости/окну/сектору D012/D050. DefenseOutcome.NeedsWeapon wire v45 сообщает владельцу причину отказа без раскрытия hidden profession rules.

Реализованный D052: Inventory регистрирует bounded runtime receipt конкретного weapon execution и UUID использованного предмета. Подтверждённая потеря HP помечает его один раз; periodic/reflected/self-price обходят этот путь. PendingWear хранится на condition предмета в том же fenced checkpoint. Во время текущего действия Current и действующие бонусы сохраняются; завершение/отмена/смерть создаёт границу для списания и пересчёта. Перед death drop износ завершается после остановки действия; restore без незавершённых actions завершает сохранённый PendingWear до публикации. Receipt runtime execution ID не сохраняется как DB identity. Поздний projectile и несколько целей одного умения используют отдельные receipts до завершения исполнения, поэтому порядок попаданий не теряет и не дублирует износ. Следующее действие и Block повторно проверяют сломанный профиль после отмены предыдущего действия.

Баланс редактируется в `Content.Server/Data/prototype.json` и Balance Studio; schema/reference validation выполняется до запуска мира. WIS, Accuracy/Evasion, Critical Damage и Block Strength — отдельные производные параметры с явными коэффициентами и модификаторами.

Character document v2 хранит семь характеристик. Разрешённый сброс v1 выполняется под эксклюзивной lease: character, inventory, Echo и progression сохраняются одним checkpoint до появления актора. Credential/character identity сохраняются; чужие world records не удаляются. Неизвестная/повреждённая модель не сбрасывается автоматически.

Wire protocol v52 несовместим с прежними layouts. ID97 ActionCancelCommand: cancel sequence u32 + action input sequence u32 + ExecutionId u64, body 16, ReliableOrdered. ID108 AbilityControlCommand: control sequence u32, action sequence u32, observed tick u32, Aim/Release byte, Aim Vector2, AimHeight/DirectionY float + ExecutionId u64, body 37, ReliableOrdered. ID19 AbilityCommand дополняет прежний body намерением Use/Deactivate byte (итого 31). Ownership, monotonic sequence, rate, action identity, geometry/state проверяются fixed-tick; bounded transport coalescing сохраняет release/последнее состояние Toggle за commit barrier ADR0008.

ID21 AbilityLoadout: общий профиль 98 байт, включая ExecutionForm/PendingEndCharges; Channel добавляет 24 байта rates/maximum, Hold — 25 байт с SustainCanTurn, Charged — 10 байт (ChargeSeconds double, AutoRelease/CanTurn bool), Toggle — 27 байт с ToggleState/OccupiesAction/StopOn. Предел 9 профилей: максимум 1160 байт с header, 1170 с региональным envelope. Passive не попадает в этот список. Strict variable-layout parser отвергает truncation, trailing data, enum/boolean/finite ошибки и поля чужой execution form. MaximumSeconds=0 означает явно неограниченное поддержание. ID22 body 90: Channeling=6, Charging=7, Sustaining=8; lifecycle ReliableOrdered по фазе/AOI, pose updates Unreliable примерно каждые 100 мс и при изменении. Client bounded stream gate принимает unreliable updates только после reliable начала и до reliable завершения, отбрасывая старые ticks; поздний пакет не воскрешает действие.

Wire 51 дополняет каждый `ProgressionState.SkillProgress` полем IsPassive bool после Learnable. Запись навыка — 17 байт, фиксированная часть body — 86; 16 навыков дают 360 байт с header, 370 с региональным envelope. Reader проверяет точный размер до выделения массива и строгие boolean values; у Passive Slot должен быть 0. Это owner-only ReliableOrdered состояние только известных навыков; серверные условия и полный каталог не передаются. Godot отображает пояснение пассива и скрывает назначение активного слота.

Wire 52 добавляет `AbilityLoadout.LastInputSequence` u32 перед count. Фиксированная часть body — 33 байта (35 с header). При перепривязке продолжающего симуляцию тела ability sequence не обнуляется; новый клиент поднимает локальный счётчик до owner floor до первого ввода. Runtime counter не становится persistent ID и не сохраняется в БД. В ID97/108 ExecutionId указывает подтверждённое исполнение вместе с исходным input sequence; 0 сохраняет отмену/release до ack. Сервер всегда проверяет actor ownership, sequence и execution, а explicit ID не удаляет другой pending input. Клиент передаёт ID из подтверждённого AbilityEffectState; server execution IDs разных действий не заменяются input sequence. Coalescing release/aim учитывает target execution.

Derived preview содержит 22 показателя, body 360. DefenseState ID85: owner-only InCombatRemaining, body 138, Unreliable. LogoutCommand ID98/body4 и LogoutResult ID99/body9 ReliableOrdered; Accepted отправляется только после commit. ActorLifeState ID100/body37, RespawnCommand ID101/body12 и RespawnResult ID102/body9 используют ReliableOrdered с AOI/owner routing. Остальные IDs и frequent movement delivery сохраняются.

Заряды принадлежат SkillCooldown, отдельно от ActionInstance: последовательные интервалы, один заряд за обычную активацию/штрафной срыв, полный reset. Текущий интервал сохраняет зафиксированную длительность; последующие используют актуальную настройку CDR. Runtime обновляет только акторов с восстанавливающимися зарядами. AvailableCharges, RechargeIn и длительность текущего интервала сохраняются атомарно с персонажем; offline time последовательно завершает несколько интервалов.

## Проверки и порядок завершения

1. Ввод basic, последовательности и commit barrier; parity Manual/Continuous; отсутствие преследования.
2. Семь статов, формулы и preview/commit/persistence; 3D hit resolver и индивидуальные защиты.
3. Общий action runner, формы выполнения, 0/50/100%, charges/CDR, окна отмены и единый input buffer.
4. Effect runtime и ACR с согласованными спорными правилами; lifecycle, Threat/contribution и Echo.
5. Bounded history / компенсация, сравнение 20/30 TPS, HUD и журнал; все сценарии §16.

Каждый срез проверяется domain + malformed/round-trip wire + релевантным UDP/persistence сценарием. Client changes дополнительно проверяются настоящей Godot-сценой. Весь GDD считается выполненным только после runtime-интеграции и прохождения полного реестра, а не после появления интерфейсов или отдельных unit tests.
