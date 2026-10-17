# Приёмка GDD 01

Источник: `gdd-01.md` §16 с последующими явными решениями из `implementation.md`. Здесь сопоставлены сценарии и исполняемые проверки. Сам перечень тестов не является процентом готовности; результаты конкретных прогонов фиксируются в реестре.

| §16 | Сценарий | Проверки в `Content.Tests` / Godot |
|---|---|---|
| 1 | Геометрия до числового уклонения | `BasicVolumeRuntimeTests`, `MeleeSkillGeometryTests`, `ProjectileHurtboxTests`, `GroundCylinderTests`, `HitContextEvidenceTests` |
| 2 | Воспроизводимый урон, отдельные броски | `StatCalculatorTests`, `CombatSimulationTests`, `DefenseSimulationTests` |
| 3 | Сложение скоростей | `StatCalculatorTests`, `BasicActionPhaseTests`, `ChannelingTests` |
| 4 | Отрицательная защита и finite значения | `StatCalculatorTests`, protocol malformed tests |
| 5 | Нет скрытого level modifier | `StatCalculatorTests`, progression/server validation tests |
| 6 | Пустой Basic без автоподхода | `Basic3DAimTests`, `BasicVolumeRuntimeTests`, `CombatInputSmoke.tscn` |
| 7 | Разрешённые отмены, отсутствие rewind | `BasicControlTests`, `ActionLifecycleTests`, `DodgeTests`, `CombatTimelineTests` |
| 8 | Фронтальный Block, Stamina, Pressure | `DefenseSimulationTests`, `ShieldEquipmentTests`, `CommonResourceTests` |
| 9 | Окно Parry, допустимый boss hit | `DefenseSimulationTests`, `BossSimulationTests`, `CombatTickRateComparisonTests` |
| 10 | Отмена/сбивание Cast и цены | `ActionLifecycleTests`, `ChargedAbilityTests`, `HealthPriceAbilityTests`, `CombatInputSmoke.tscn` |
| 11 | Одна ordinary lane при 100% CDR | `OrdinaryInputTests`, `CombatReceiptTests`, `SkillChargeIntegrationTests`, `AuthoredResourceOperationTests` |
| 12 | Refresh и независимые источники | `TimedEffectTests`, `PeriodicEffectTests`, `ControlStateTests` |
| 13 | Контроль, ACR, removal, immunity | `AdaptiveControlTests`, `AuthoredControlTests`, `ControlRemovalPolicyTests`, `ControlProtectionSourcesTests` |
| 14 | Нет friendly fire и физической стены союзников | `CompanionCombatTests`, `CompanionPvpTests`, `ProjectileCollisionTests`, `CombatMotionGeometryTests` |
| 15 | Disconnect 60 с и запрещённый logout | presence/logout integration tests, `CombatInputSmoke.tscn` |
| 16 | Гибель/печать/повторный summon Эхо | `EchoRuntimeTests`, `EchoRecoveryTests`, `CompanionSummonTests`, `EchoRecoveryIntegrationTests`, `LifeSmoke.tscn` |
| 17 | Истина сервера при задержке | lossy network integration tests, checkpoint-gated combat tests, `CombatTickRateComparisonTests`, native UDP smoke на 30/20 TPS |
| 18 | Поломка оружия в бою | `WeaponWearTests`, `RangedBasicWearTests`, `LateWeaponWearTests`, `ItemWearStorageTests`, `ToggleAbilityTests` |

Дополнительные согласованные случаи: `SimultaneousContactIntegrationTests` проверяет взаимные смертельные контакты и атомарный drop; `AbilityLoadoutSegmentsTests` — MTU и атомарную сборку; `ControlFeedbackIntegrationTests` — durability/AOI/privacy; `EchoPermissionTests` — границу внешней системы отношений; `HitContextTests` — authored tags/HP predicate без скрытого бонуса.

## Воспроизведение

```powershell
dotnet build Game.slnx --no-restore -m:1
dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore
dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content --RegionExports:AutoExport=false --RegionExports:SurfacePackagePath=D:/projects/project-g/.artifacts/combat-input/current-surfaces.json
./tools/Smoke-CombatInput.ps1 -PhasedBasics -TickRate 30 -SurfacePackagePath .artifacts/combat-input/current-surfaces.json
./tools/Smoke-CombatInput.ps1 -PhasedBasics -TickRate 20 -SurfacePackagePath .artifacts/combat-input/current-surfaces.json
```

`Smoke-CombatInput` создаёт новый UUID-каталог и отдельную SQLite в `.artifacts/combat-input`; рабочая БД не нужна. Native Godot дополнительно запускает `res://Tests/UI/LifeSmoke.tscn` и `tools/BalanceStudio -- --smoke`.

Для CPU/window сравнения отдельно от общей регрессии:

```powershell
$env:PROJECT_G_TPS_REPORT="D:/projects/project-g/.artifacts/test-results/gdd01/tps-comparison.json"
dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~CombatTickRateComparisonTests
```

В отчёте явно указана конфигурация сборки. Стенд сравнивает одинаковые 128 combat bodies, 30 с симуляции и три чередующихся прогона каждого TPS; отдельные probes используют 50/100/150 мс RTT как контролируемое серверное время получения. Transport jitter, БД, AOI serialization и render не включены в CPU-оценку. Это сравнение clock/runtime, а не обещание ёмкости сервера. Lag compensation выключена D124.

Шесть opt-in PostgreSQL сценариев требуют `PROJECT_G_TEST_POSTGRES` с изолированной disposable БД. Development-copy migration требует явно выбранную копию через `PROJECT_G_MIGRATION_SOURCE`. Без этих входов они честно пропускаются; рабочая БД не используется для снятия skip. Согласованный wipe старых персонажей не запускается автоматически.
