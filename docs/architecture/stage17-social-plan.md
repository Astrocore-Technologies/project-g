# Этап17 — группы и гильдии

Статус: реализовано как прототип, визуальная приёмка ожидается. Пакет правил утверждён пользователем 2026-10-08: ../design/stage17-social-rules-proposal.md.

## Authority и границы

Один SocialSimulation в процессе prototype realm. Content.Server/Social отвечает за составы, роли, invitations/handoff, лидерство, индексы, receipt и dirty rows. World связывает CharacterId с присутствием/боевым EntityId и выполняет команды на fixed tick. Transport только буферизует bounded intentions и публикует подтверждённое состояние. Content.Database хранит opaque documents без зависимости от Server/Shared/Godot. Client не назначает состав, роли или HP.

Membership привязан к CharacterId; connection, PlayerId и EntityId меняются при обычном reconnect. Публичные member/group handles — монотонные persistent числовые ID, никогда не credential и не переиспользуются. UUID остаётся на сервере. Временные публичные подписи «Игрок #handle» не вводят постоянную систему имён.

## Persistence и миграция8

Additive schema8 создаёт social_records(realm/key/kind/revision/state), social_members(unique realm/kind/CharacterId), social_names(unique active normalized name). FK связывает membership с существующим персонажем и aggregate, name с aggregate. SQLite включает foreign_keys. PostgreSQL FK ссылается на project_g_characters; SQLite — на characters.

Kind0: version1 allocator; kind1: version1 identity + последняя operation/fingerprint/outcome/offline combat deadline; kind2/3: version1 party/guild, capacity, leader, join ordinals, roles, closed tombstone. Каждый документ ≤8192UTF-8bytes. Restore проверяет version/unknown/duplicate fields, идентификаторы, единственного leader, role/capacity, нормализованное имя и соответствие SQL-проекции JSON. Закрытые ID сохраняются; освобождённое имя получает новый ID.

Отдельная realm lease social_prototype использует существующие SQLite file ownership / PostgreSQL advisory lock и owner+revision CAS. SaveCheckpointAsync сохраняет character/world/item ownership/social/audit одной SQL-транзакцией. Row revisions строго возрастают; stale realm/character/row либо duplicate membership/name откатывает весь batch. Social-only dirty тоже запускает barrier. Публикация результата/состава только после commit. Offline kick не редактирует чужой character JSON и не требует открытия его session. Lease удерживается до финального checkpoint и освобождается при shutdown/failed startup.

## Команды и время

Одна pending social intention на connection, максимум текущего session budget. Persistent operation watermark и SHA256 fingerprint точного исходного запроса: retry возвращает прежний outcome, изменённый payload/старый operation отвергается, в том числе после restart. При Invite временный EntityId разрешается только в online персонажа в AOI на execution tick; внутри домена используется public member handle.

Crypto64bit tokens, TTL30с, максимум4outgoing/2incoming и256всего. Invite не резервирует слот. Accept перепроверяет caller/target/revision/capacity/права/combat eligibility. Изменение состава/roles/revision, disconnect и restart инвалидируют ephemeral invitations. Decline явный. Guild handoff требует отдельного acceptance текущим online участником.

Party leader offline получает120с grace. Кандидат — online участник с ранним join ordinal; смена сохраняется до публикации. Restart начинает grace заново. Guild не имеет offline takeover. Членство и offline слоты переживают смерть/logout/restart.

## Combat и rewards

PvpCanHit проверяет same party до урона/tag/reputation/PK на impact для melee/projectile/AoE/Echo owner. Guild сама не даёт иммунитета. Party membership/leadership/resize меняется только вне тега/aggressor/cast/active effects всех затронутых участников; приглашение само combat policy не меняет. Offline combat deadline сохраняется отдельно, disconnect не очищает ограничение.

EXP остаётся личным; party bonus/split/passive grants отсутствуют. Лут ручной, без групповой резервации/round-robin/need-greed. Существующие equipped unbound PvP drop, личный tag+5с pickup, TTL30мин и voluntary trade сохранены. Boss autoscale и contribution/rewards не добавляются.

## Protocol23 и relevance

76SocialCommand,77SocialResult,78SocialRoster,79SocialInvites,80PartyPresence. ReliableOrdered command/result/roster/invites; roster pages≤8 для20party/32guild. PartyPresence — Unreliable≤2Hz, pages≤8, owner/party/roster revision/tick guard. Presence на20человек — три packet≤232bytes каждый, суммарно менее700bytes/обновление/получатель (до1.4KB/s без transport overhead).

Своей группе передаются roster/online/dead/HP/позиция. Актуальные HP/координаты только online участников текущего prototype региона; offline — неизвестно, detached combat actor отдельно помечен. Дальний участник не создаёт AOI spawn, avatar, Echo, effect, fog reveal или секретную геометрию. Гильдия получает только roster/roles/online. Outsider не получает чужие roster/invitations.

## Клиент и бюджеты

N — группа, O — гильдия. Панели со scroll, create/invite видимого игрока/accept/decline/leave/kick/resize/roles/transfer. Guild handoff подтверждает получатель; sole leader disband требует checkbox. HP/позиции группы показаны в HUD; данные старше3с не выдаются как актуальные. Нет optimistic membership; pending command повторяется каждые2с, exact replay безопасен. Events симметрично снимаются при удалении панели/disconnect.

Social.Enabled=true, MaxParties=64 (1..64), MaxGuilds=32 (1..32), startup validation. Identity budget4096, total group records incl tombstones2048, loaded rows≤8192; exhausted IDs/budgets fail closed. Public handles indexed; timers обходят только bounded active parties/invitations, не tombstones. Это prototype budget, не доказательство MMO load.

## Verification и приёмка

Domain:6/20, race, downgrade, offline leader/restart, combat blockers, TTL/logout, durable replay, guild role escalation/handoff/name/tombstone. Wire: roundtrip, every truncation/trailing byte, enum/count/NaN/offline coordinate checks,20-member packet budget. Real projectile/AoE/Echo policy and personalEXP; guild outsider damage. SQLite+livePG: restart/offline kick, stale realm atomic rollback, SQL uniqueness; schema7→8 preserves character/equipment/PvP/world. Lossy UDP100–150ms/10%: commit gate, private invitations, accepted roster, stable reconnect handles.

Домашняя Godot-приёмка остаётся отдельной: см. ../design/stage17-social-acceptance.md. Несколько регионов потребуют отдельного решения о routing social authority; нельзя запускать независимый social registry на каждый регион.

Финальная автоматическая проверка 2026-10-08: Game.slnx build —0ошибок; content validation успешна;408/408 tests,0skips, live PostgreSQL16 включена. NU1903 SQLitePCLRaw.lib.e_sqlite3 2.1.11 — существующее предупреждение. Test PostgreSQL временная, tmpfs, loopback; после проверки остановлена. Рабочая .data/пользовательские процессы не изменены; commit/push не выполнены.
