# Архитектура и план этапа 15

Статус: прототип этапа 15 реализован, визуальная приёмка ожидается. Временный баланс явно утверждён пользователем 2026-10-08; docs/design/stage15-balance-proposal.md. Документ описывает фактическую реализацию и ограничения, не меняет permanent gameplay contract или ADR 0008/0009.

## Цикл и границы

Добыть → переплавить → изготовить → использовать/ремонтировать → развить/заточить → обменять/продать. ServerWorld определяет результат на fixed tick; клиент передаёт намерение и явно подтверждает цену/риск. Content.Database — библиотека инфраструктуры, не новый сервис. Shared содержит bounded DTO/сериализацию, без формул/RNG/credentials. Godot показывает приватные состояния и публичные карточки.

~~~mermaid
flowchart LR
    UI[Godot: расчёт и подтверждение] --> Intent[Ограниченный inbox]
    Intent --> Tick[Fixed tick: ownership, range, revision]
    Tick --> Prepare[Подготовить все изменения]
    Prepare --> Save[Атомарный checkpoint]
    Save --> Inventory[Inventory, wallet, receipts]
    Save --> World[Stock, escrow, credits, audit]
    Save --> Registry[UUID ownership]
    Save --> Publish[Ответ клиентам после commit]
~~~

## Реализованные срезы

| Срез | Поведение |
|---|---|
| 15a | Общие запасы, приватные материалы, переплавка и крафт клинка; C. |
| 15b | Износ от успешного основного удара, current/maximum/revision, ноль отключает оружие, ремонт по quote в C. |
| 15c | Одноразовая эволюция того же UUID; заточка +0…+5, server RNG, потеря maximum/уничтожение; J. |
| 15d | Приглашение/предложения/двустороннее подтверждение, отмена при disconnect/движении/expiry, вещи и материалы; B. |
| 15e | Целые игровые монеты, скупщик руды, локальный escrow market, покупка/отмена/выручка офлайн-продавца; J. |
| Gathering | Срок восстановления фиксируется при полном истощении; restart сохраняет deadline; возврат исходного stock через 600 секунд. |

## Persistence и идентичность

SQL schema 7: unique UUID → owner registry в обоих адаптерах. Владельцы — character, ground, world escrow; removed UUID получает terminal запись. Batch переносит владельца только при включённых исходном/целевом агрегатах. Duplicate UUID fail closed, без автоматического исправления/reset. Перед применением migration к пользовательской БД нужна обычная резервная копия. В реализации использовались только отдельные test SQLite; рабочая .data не изменялась.

SavedInventory v1 остаётся совместимым: optional condition, maintenance receipt, enhancement level, SavedEconomy v1. Кошелёк и один последний receipt + monotonic operation watermark сохраняются с inventory. Это фактическая замена предложенной отдельной SQL receipt table для текущего bounded прототипа. Последний exact replay возвращает сохранённый эффект; старые операции не выполняются снова, но полный исторический результат старой операции не восстанавливается. Repair и economy имеют раздельные counters.

SavedWorldNode v1: optional resource deadlines и SavedMarket v1 (8 listings, 16 seller credits). Listing хранит полный экземпляр; UUID передаётся DatabaseWorldSave escrow metadata. Продажа начисляет выручку в world credit, кошелёк продавца пополняется отдельной идемпотентной операцией Claim. Онлайн/офлайн продавца не меняет порядок владения. Каждая покупка/отмена/получение сохраняет wallet/inventory/receipt/market/audit/ownership одним SaveWithWorldAsync под session/world leases и revision fences.

## Проверки до мутации и публикации

Economy operation: следующий monotonic ID → actor lock → alive/not moving/casting/dashing → станция/range/LOS → UUID/runtime handle/revision → costs/capacity → exact quote ≤15 sim seconds → prepare → один RNG для попытки → один checkpoint. Клиентский timeout не определяет server RNG или срок quote. После провала максимум уменьшается; ремонт не возвращает потерю. Эволюция сохраняет UUID, уровень и состояние прочности. Пересчёт экипировки не лечит/не сбрасывает cooldown.

Trade: до 32 сессий, 4 items/4 material types per side, timeout 60 sim seconds; offer change снимает оба подтверждения. Gear/craft/repair/economy на зарезервированном actor блокируются. Новые runtime handles после передачи исключают использование старых handles. Invite/offer не durable; завершение atomically сохраняет оба inventory и audit перед Completed. После restart старый session ID отклоняется; retrieval старого результата по session ID не реализован.

GameServerService замораживает следующий tick/публикацию на время checkpoint; bounded intentions ждут. Сбой save не публикует успех. Незафикисированный результат RNG после fatal failure не виден клиенту; restart использует сохранённый receipt/watermark.

## Протокол и бюджеты

Protocol v21, BalanceVersion 2. Repair 57–60, trade 61–63, economy 64–67, market 68. ReliableOrdered; приватные condition/wallet/receipts адресуются владельцу. Market listings передаются только рядом с верстаком; уход очищает snapshot, возврат получает текущие объявления. Приватная выручка может обновляться вдали. UUID/seller GUID/credentials не передаются клиентам.

Wallet/credit ≤1 000 000; material stack ≤999; inventory ≤8; market ≤8, seller ≤2, offline credit owners ≤16. Одно действие одного типа/session/tick; quotes по одному/session, receipts по одному/actor. Snapshot market ≤900 bytes, все сообщения ≤1200. Нет world-wide history scans, новых workers/sharding/services, налогов или RMT. Credit capacity/capacity inventory/wallet отклоняют целиком, не создают недоступную выручку. World JSON budget 8192 UTF-8 bytes, depth 8 для полного escrow item; строгие модели/duplicates/reference validation.

## Верификация и приёмка

C# solution build, полный тестовый набор, validated content и diff check. Domain/wire/SQLite/lossy UDP проверяют риск, exact replay, restart, trade, two-buyer race, private states, AOI, commit gate, world fence rollback и terminal ownership. RNG/clock injection доступны только test assembly. Старый lossy gathering race test проверяет любого одного победителя, а не порядок доставки между peer.

Godot executable отсутствует в текущем Codespace; visual main scene не запускалась. Live PostgreSQL не проверялся; до смены рабочего provider нужна отдельная проверка. Existing SQLite dependency выдаёт NU1903; она не обновлялась в рамках этапа. Пользовательские процессы не останавливались, commit/push не выполнялись.

Домашняя приёмка: C — gather/craft/repair; I — gear; J — evolve/enhance/vendor/list/buy/cancel/claim; B — trade с другим клиентом. Результаты должны сохраняться после штатного restart. Этап 16 не начинается автоматически.
