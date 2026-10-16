# Эффекты боевой системы GDD 01

Детализация B7/B8; наличие этого документа не означает завершённую реализацию. Основание: GDD 01 §7–9, D013–D047 и ADR 0008. Client/Server/Shared и технология БД сохраняются.

## Реализованная основа

`EffectDefinition`/`EffectOperations` и `TimedEffectInstance` подключены к `CombatSimulation.Effects`, `PeriodicEffects`, `DamageEffects`, `HealthPrices` и `EffectPersistence`. Есть server API для timed modifiers, stacks/refresh, условий/иммунитетов по tags, Cleanse/Dispel помеченных timed effects, snapshot/dynamic DoT/HoT, shields, Life/Mana Steal, reflection и HP price. Истечения и тики используют упорядоченное расписание без прохода по миру; refresh удаляет прежнюю запись расписания.

`CombatSourceId` + индекс разрешают persistent source в текущего runtime-актора. `ControlSet` хранит постоянные источники и UUID отдельных экземпляров; ACR сохраняется отдельно. `FrozenCharacterState` переносит effect documents между регионами, не применяя к переходу флаг logout.

В SQLite/PostgreSQL добавлена миграция 9: отдельные effect rows в той же fenced character transaction. Реальные SQLite restore/rollback проверены; live PostgreSQL недоступен в этой среде. Рабочая БД не открывалась. Регрессия после skill payload/lifecycle: 846 passed / 0 failed / 7 skipped и native Godot/UDP smoke; текущие последующие изменения отдельно отмечаются в реестре.

`AbilityDefinition.Effects` подключён к активации и попаданиям навыков: Activated, Contact, HealthDamage, UnblockedHealthDamage; Self/HitTarget задаются явно. Bleed мечника перенесён в общий runner: 4 секунды, четыре тика по 15% базового удара оружия, интервал 1 с, snapshot/refresh по общим правилам. Каталог проверяет ссылки/триггеры до запуска.

Schema 8 добавляет явные `SkillEffectApplication.Operation`: Timed с EffectId либо Heal/Resurrect с EffectPower. Каталог проверяет взаимоисключающие поля, положительную силу и допустимый trigger/target. GroundArea с `contactDisposition: Support` проверяет 3D-область, навигацию/LOS и право поддержки на impact; payload с `AllowOtherPlayers` разрешает других игроков только для своего применения. Direct Heal использует текущие статы/крит и effective healing; Resurrect вызывает общую жизнь с HP из данных. Activated Self Heal доступен отдельно; остальные support shapes пока не поддержаны. Эта граница реализации не является новым постоянным gameplay-ограничением.

Schema 9 добавляет `operation: "Control"` и `SkillControlDefinition`, общие для melee, projectile и GroundArea. Обычные статусы требуют положительную длительность; Slow/Blind/Taunt — явно заданную силу. Knockback/Pull требуют дистанцию, скорость и origin (Source/Impact); Knockup — длительность полёта, высоту и время лежания. Каталог отклоняет неоднозначные и неприменимые поля, повтор одного control kind/trigger/target, неподдержанную неявную level scaling. Geometry/contact, numeric evasion и eligible defense выполняются до payload; при наложении повторно проверяются цель, resistance и ACR. Cleansable metadata остаётся отдельной работой.

Удар навершием, Подрез, Круговой и Восходящий удар перенесены с отдельных полей MeleeTechnique в эти payloads с прежним HealthDamage trigger и прежними числами. Специальных вызовов контроля в melee hit runner больше нет. Existing saved control rows сохраняют тот же skill ID/source/kind/instance UUID; wire v44 и DB schema 10 этим переносом не изменяются. Balance Studio обходит вложенные массивы payloads, показывает отдельные числа контроля на странице навыка и позволяет редактировать/отменять их.

Wire v45 / CombatFeedback ID103 публикует подтверждённые числовые результаты по AOI после checkpoint. Source вне AOI заменяется на 0; persistent keys/definition IDs не передаются. У события отдельный runtime ordinal, у действия — собственный execution ID; клиент подавляет повтор ordinal. Репликация всех timed instances ещё требует интеграции. Закрытый журнал и разборы смерти D048/D049 подключены отдельно: [combat-journal.md](combat-journal.md). Старый периодический AbilityHit пока сохраняет совместимость существующих contact reactions/alerts; общий feedback не повторяет изменение HP.

`SkillEffectOperation.Cleanse/Dispel` использует `SkillRemovalDefinition`: Timed выбирается явно, Tags фильтрует только timed effects, Controls перечисляет разрешённые виды контроля (пустой список не является wildcard). Операция удаляет все подходящие экземпляры, не выбирая произвольный один. Timed Cleanse/Dispel соблюдает Cleansable/Dispellable и disposition определения. Control Cleanse удаляет только перечисленные виды, сохраняет использованный ACR и пересчитывает оставшиеся ограничения. Последнее принудительное перемещение прекращается в настоящем navigation mover; удаление Knockup не снимает независимый Knockdown. Поддержка перепроверяется при impact; Dispel проходит hostile contact resolver. Новых чисел или навыка профессии по умолчанию не добавлено.

Wire v45 добавляет journal kinds ControlRemoved/EffectsCleansed/EffectsDispelled без смены layout/IDs/размеров пакетов. Это owner-only подтверждения после общего checkpoint; клиент показывает причину снятия вместо строки урона.

Остаток B7/B8: authored ресурсные операции, per-instance Control Cleanse/Dispel tags/исключения и выбор ограниченного количества снимаемых эффектов, public timed-instance DTO/HUD, detailed immunity feedback и full actor/resource integration. Эти пункты нельзя считать выполненными по наличию ядра.

## Определения и экземпляры

Для GroundArea поддержки задаются `contactDisposition: "Support"`, `power: 0`, `magicAttackScale: 0`; существующие `action`, `range`, `radius`, стоимость и cooldown остаются обычными данными навыка. Пример payload лечения:

```json
{"operation":"Heal","trigger":"Contact","target":"HitTarget","power":{"flat":10,"magicScale":0.5},"allowOtherPlayers":false}
```

Для воскрешения заменяется операция на `Resurrect`; сила задаёт возвращаемый HP. Для щита используется `operation: "Timed"` с `effectId` определения shield вместо `power`. Разрешение остальных игроков задаётся отдельно у каждого payload. Пример описывает формат; числа не утверждают новый навык профессии или новый общий баланс.

Пример очищения явно разрешённых контролей и отрицательных timed effects с тегом poison:

```json
{"operation":"Cleanse","trigger":"Contact","target":"HitTarget","removal":{"timed":true,"tags":["poison"],"controls":["Root","Freeze"]},"allowOtherPlayers":false}
```

Такой HitTarget Cleanse принадлежит Support area. Для Dispel нужен hostile payload с `operation: "Dispel"`, пустым Controls и явно выбранными тегами положительных эффектов; `tags: null` означает все подходящие timed effects. Данные формата не утверждают конкретный набор очищений новой профессии.

`EffectDefinition` — server-only immutable content: стабильный ID, публичное описание отдельно от внутреннего имени, тип, время, период, режим расчёта силы, правила stacks/refresh, условия окружения, модификаторы, Control/Dispel/Cleanse/Immunity и флаги жизни при смерти/выходе. Навык ссылается на определения; непосредственная геометрия и действие остаются отдельными concerns.

`EffectInstance` — UUID экземпляра, постоянная ссылка на источник, цель, definition ID, stacks, оставшееся время, следующий тик, порядок наложения и числовой снимок. Источники независимы. Повтор того же определения тем же источником изменяет существующий экземпляр по его политике. Глобального дизайнерского ограничения числа разных баффов нет.

Постоянная ссылка на источник отличается от `NetworkEntityId`. Для игрока используется character identity, для Echo — сохранённый instance UUID; ссылка на NPC обозначает конкретную серверную жизнь/источник. Сеть не получает эти ключи БД. Поиск текущего runtime-источника выполняется через индекс, без global scan.

## Время и числовые операции

Серверный runner обрабатывает только активные эффекты и наступившие события. Идентичность экземпляра/версия защищают от повторного применения устаревшей записи расписания. Начисление периодической работы не связано с client FPS или числом пакетов.

DoT/HoT следуют D032: snapshot при наложении, обновление snapshot при refresh без сброса ритма. Динамический режим задаётся явно; поведение при недоступном источнике должно быть частью такого определения. Офлайн время уменьшает срок; пропущенные тики не возвращаются пачкой при входе (D033).

Урон, лечение, shield absorption, resource changes, control и снятие эффектов используют явные операции. Контекст различает direct hit, periodic, reflected, self-damage и resource price, чтобы исключить случайные рекурсивные proc chains. Неопределённые постоянные правила этих взаимодействий сначала согласуются.

Щиты расходуются после mitigation, до HP, по сроку и порядку наложения (D034). Effective healing ограничивается недостающим HP; overheal не становится боевым вкладом. ACR владеет бюджетом цели отдельно от экземпляров контроля; удаление эффекта не возвращает уже потраченный бюджет.

## Характеристики и жизненный цикл

Характеристики от primary/equipment хранятся отдельно от временных прибавок. Runner пересчитывает затронутого актора при изменении эффекта/stacks/окружения; не прибавляет модификатор повторно к уже модифицированному результату. Изменения maxima не лечат автоматически. Ability resources/CDR получают тот же итоговый профиль, без второй формулы в клиенте.

Смерть удаляет обычные временные эффекты; persist-through-death сохраняет явно помеченные. Online checkpoint хранит все активные экземпляры, чтобы HP/MP и maxima совпадали с runtime. На входе восстанавливаются только persist-through-logout; при переходе региона этот фильтр не применяется. Forced-disconnect body продолжает получать эффекты до удаления. Reattach использует прежнего актора без restore. D043 завершает Knockback/Pull/незавершённый полёт после аварийного перезапуска; уже наступивший Knockdown и остальные отдельные controls восстанавливаются с исходным источником, без возврата ACR.

## Сохранение

Предел 8 KiB существующего character document не должен превращаться в cap числа баффов. Эффекты сохраняются отдельными versioned записями экземпляров под той же character lease и revision, в том же atomic checkpoint с HP/MP/charges/ACR. Добавочная таблица не заменяет SQLite/PostgreSQL, не удаляет существующие документы и не меняет ownership boundary.

Database layer хранит opaque документы и UUID; gameplay validation остаётся в Server. Legacy character без component получает пустой набор. Неизвестный/повреждённый effect document не отбрасывается молча. Изменения проверяются на изолированной БД; рабочая БД не открывается для разработки.

## Сеть и проверка

Публичные DTO ограничены размером одного пакета, но набор эффектов разбивается на сообщения без gameplay cap. Owner получает нужные ограничения, срок и причины; чужие акторы — только разрешённое AOI/presentation состояние. Внутренние определения, скрытые профессии/условия и постоянные source keys не публикуются.

Проверки: независимые источники и refresh/stacks; отсутствие дрейфа характеристик; ритм DoT/HoT; critical eligibility; shield ordering; control/ACR и Cleanse/Dispel; смерть/выход/offline; transactional restart и отсутствие publish до commit; bounded malformed packets; настоящие Godot state/feedback.
