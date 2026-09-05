# IsEnemy / Side==Enemy 消费点扫描清单（票 #17 / T5）

扫描范围：`decomp/`（游戏 v0.111.0 反编译），模式 `IsEnemy|Side == CombatSide.Enemy|Side != CombatSide.Enemy`，
共 **32 个命中点**（17 个文件）。每个命中点标注：**已 guard**（本票补丁）或**无需 guard**（+理由）。

## 翻转机制（spec §7 #2）

`Creature(Player, currentHp, maxHp)` 构造器把 `Side` 写死为 `CombatSide.Player`（`Creature.cs:364-370`，
只读自动属性，后备字段 `<Side>k__BackingField`）。对决对手 creature 由
`SideFlip` 补丁集的 ctor postfix 在 `DuelScope.IsDuelRoom` 门控下将后备字段改写为 `Enemy`：
`CombatState.AddCreature` 按 Side 分桶（`CombatState.cs:718-731`）随之自动把对手放进
Enemies 桶，`CombatState.Players`（由 `IsPlayer` 派生，`CombatState.cs:334-339`）仍包含它。
翻转后 `IsEnemy == true` 且 `Monster == null` —— 所有假设「Side==Enemy ⇒ Monster!=null」的
消费点都必须被 guard（下表）。

## 命中点逐条清单

| # | 位置 | 判定 | 说明 |
|---|------|------|------|
| 1 | `MegaCrit.Sts2.Core.Combat/CombatTurnState.cs:103` | 无需 guard | `IsEnemyTurnStarted` 是回合状态标志，非 creature 派生。 |
| 2 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:144` | 无需 guard | 同上（属性转发）。 |
| 3 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:838` | 无需 guard | 同上（置 false）。 |
| 4 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:844` | 无需 guard | 同上（置 true）。 |
| 5 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:1083` | 无需 guard | `EndEnemyTurn` 的 CurrentSide 校验，回合流自身，不触 creature。 |
| 6 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:1133` | **已 guard** | `AfterCreatureAdded(Creature, CombatState)`：`creature.IsEnemy ⇒ creature.Monster.RollMove(...)`（:1135）对翻转对手 NRE。`SideFlip` 集前缀跳过（`SideFlipPolicy.ShouldSkipMonsterRollMove`）；首行 `AfterAddedToRoom` 对翻转对手本身也是 no-op。 |
| 7 | `MegaCrit.Sts2.Core.Commands.Builders/AttackCommand.cs:310` | 无需 guard | `TargetSide = Attacker.Side==Enemy ? Player : Enemy` —— 翻转对手的攻击正应指向 Player 侧（我方），行为即所需。 |
| 8 | `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:72` | 无需 guard | `CreatureCmd.Add`：`IsMonster` 在前（:72-75），玩家 creature 不会进入 `PrepareForNextTurn`。 |
| 9 | `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:77` | 无需 guard | MonsterIds 历史记录：`creature.Monster != null` 显式前置判空。 |
| 10 | `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:372` | 无需 guard | 命中敌方音效：`SfxCmd.PlayDamage(monster, n)` 首行判 `monster != null`（`SfxCmd.cs:78-81`），null 安全。 |
| 11 | `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:556` | 无需 guard | 死亡移除路径：`CombatManager.RemoveCreature`（:1367-1376）对非 monster 只做退订/事件；`creature.Monster`（:559）null 时走 `monster != null` false 分支，不 NRE。翻转对手死亡后不再被移出 Enemies 桶，`IsCombatEnding` 依赖「无敌方存活主敌人」收束（见 #12），结算细节归 T6。 |
| 12 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs:428`（`IsCombatEnding`，非直接 grep 命中，`Enemies.Any(IsAlive && IsPrimaryEnemy)`） | 无需 guard | 翻转对手是存活主敌人时战斗继续；其死亡即触发战斗结束 —— 对决胜利的天然载体（T6 结算入口）。 |
| 13 | `MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:571` | 无需 guard | `KillWithoutCheckingWinCondition` 的 Enemy 分支：翻转对手死亡走此分支，`teammates`（同侧存活者）为空或为真怪物，`All(IsSecondaryEnemy)` 为空集时为 true → `Kill(teammates)` 空列表直接返回；不崩溃。行为语义（玩家死亡侧效果 `DeactivateHooks`/`HandlePlayerDeath` 被跳过）已在 T6 结算票范围记录。 |
| 14 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:244` | 翻转机制本体 | `IsEnemy => Side == CombatSide.Enemy` —— 后备字段改写后即为 true，是所有下游判定的开关。 |
| 15 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:257` | 无需 guard | `IsPrimaryEnemy`：翻转对手（无 secondary power）返回 true → 驱动 #12 胜利判定，行为即所需。 |
| 16 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:273` | 无需 guard | `IsSecondaryEnemy`：无 power → false，与 #15 一致。 |
| 17 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:417` | **已 guard** | `AfterAddedToRoom`：`Side==Enemy ⇒ await Monster.AfterAddedToRoom()` 对翻转对手 NRE。`SideFlip` 集前缀跳过（`SideFlipPolicy.ShouldSkipAfterAddedToRoom`）。 |
| 18 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:718` | **已 guard** | `TakeTurn`：非 monster 且 Side==Enemy 时 vanilla 直接抛 `InvalidOperationException("Only enemy monsters...")`，敌方回合循环（`CombatManager.cs:1419-1428` 遍历 Enemies 桶调 `TakeTurn`）会崩溃。`SideFlip` 集前缀跳过翻转对手的自动化回合（其真实回合由 T6/T7 网络对战实现）。 |
| 19 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:213`（`HoverTips`，:222-230 读 `Monster.NextMove.Intents`） | 无需 guard | `IsMonster` 显式前置（:222），翻转对手只返回 power tips。 |
| 20 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:325`（`IsStunned`） | 无需 guard | `Monster?.NextMove.Id` 空条件运算符，翻转对手恒 false。 |
| 21 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:525`（`StunInternal`，:535-536 读 `MoveStateMachine`） | 无需 guard | `Monster == null ⇒ throw("Can't stun a player.")`（:527-530）—— vanilla 已显式拒绝玩家眩晕，翻转对手同路径，行为可接受。 |
| 22 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:547`（`PrepareForNextTurn`，:550 读 `Monster.MoveStateMachine`） | **已 guard** | 玩家侧回合开始时对每个敌人调用（`CombatManager.cs:743-746`），翻转对手在此 NRE。`SideFlip` 集前缀跳过（`SideFlipPolicy.ShouldSkipPrepareForNextTurn`；玩家 creature 无意图节点，跳过原方法即可）。 |
| 23 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:704`（`OnSideSwitch`） | 无需 guard | `IsPlayer` 分支调 `Player.OnSideSwitch()`，翻转对手是玩家 creature，走正常玩家路径。 |
| 24 | `MegaCrit.Sts2.Core.Entities.Creatures/Creature.cs:691`（`AfterTurnStart`，非直接命中） | 无需 guard | `side==Player` 分支 `PlayerCombatState?.TurnNumber` 空安全；否则 `ClearBlock`（Hook 判定），不触 Monster。 |
| 25 | `MegaCrit.Sts2.Core.Models/EncounterModel.cs:401` | 无需 guard | `OnCreatureSpawned`：`Monster?.CanonicalInstance`（:405）空安全，翻转对手不进 SpawnedEnemies。 |
| 26 | `MegaCrit.Sts2.Core.Models/PowerModel.cs:154` | 无需 guard | Power 可见性：`Target.IsEnemy ⇒ 可见` —— 对对手 power 的可见即所需（对决信息明示，具体明暗度由 T6 设计）。 |
| 27 | `MegaCrit.Sts2.Core.Models.Events/TheArchitect.cs:169` | 无需 guard | 事件战斗专用（event combat creature 查找），对决房不是事件房，路径不可达。 |
| 28 | `MegaCrit.Sts2.Core.Models.Monsters/ToughEgg.cs:133` | 无需 guard | 数据语义：egg hatch 数量按 CurrentSide 判定，与 creature Side 无关。 |
| 29 | `MegaCrit.Sts2.Core.Models.Powers/PlatingPower.cs:31` | 无需 guard | 敌方 Plating 按玩家人数缩放 —— 若对决卡池把 Plating 给到翻转对手，按敌方规则缩放即正确语义（T6 卡池范围）。 |
| 30 | `MegaCrit.Sts2.Core.Models.Powers/PlatingPower.cs:72` | 无需 guard | 同上（回合数条件，`PlayerCombatState.TurnNumber` 在 `base.Owner.Player != null` 时才读 —— 翻转对手有 Player）。 |
| 31 | `MegaCrit.Sts2.Core.Models.Powers/PlatingPower.cs:74` | 无需 guard | 同上（敌方递减逻辑）。 |
| 32 | `MegaCrit.Sts2.Core.Models.Powers/RitualPower.cs:38` | 无需 guard | `WasJustAppliedByEnemy` 标志，纯行为语义。 |
| 33 | `MegaCrit.Sts2.Core.Models.Powers/SleightOfFleshPower.cs:23` | 无需 guard | 敌方施放者 flash 判定，纯行为语义。 |
| 34 | `MegaCrit.Sts2.Core.Models.Relics/FurCoat.cs:153` | 无需 guard | 敌方 FurCoat 行为；翻转对手携带时按敌方语义（T6 遗具范围）。 |
| 35 | `MegaCrit.Sts2.Core.Nodes.Combat/NPlayerHand.cs:895` | 无需 guard | `CurrentSide == Enemy` 时禁用本地手牌 —— 对决中「对手回合」本地手牌禁用即预期（T5 假对手不行动，回合瞬间轮转）。 |
| 36 | `MegaCrit.Sts2.Core.Nodes.Combat/NTargetManager.cs:513` | 无需 guard | `AnyEnemy` 目标可选判定 —— 翻转对手可被指向，正是验收 #1 所需。 |
| 37 | `MegaCrit.Sts2.Core.Nodes.Combat/NTargetManager.cs:597` | 无需 guard | 瞄准高亮取 `Entity.IsEnemy`，纯视觉。 |
| 38 | `MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer/NMultiplayerTest.cs:576` | 无需 guard | 游戏自带 multiplayer 测试 harness 的调试循环，正常游玩路径不可达。 |
| 39 | `MegaCrit.Sts2.Core.Nodes.Vfx/NBigSlashVfx.cs:90` | 无需 guard | VFX 参数，纯视觉。 |

## 相邻消费点（同机制，非 `IsEnemy` 字面命中，一并标注）

- `MegaCrit.Sts2.Core.Combat/CombatState.cs:718-731` `AddCreature` 按 `Side` 分桶 —— 翻转机制落点，无需 guard。
- `MegaCrit.Sts2.Core.Combat/CombatState.cs:334-339` `PlayerCreatures/Players` 按 `IsPlayer` 派生 —— 翻转对手仍在 `Players` 中（对手 creature 参与目标列表、意图 target、`NetCombatCardDb.StartCombat`），无需 guard；其战斗内牌堆由 harness 注入时初始化。
- `MegaCrit.Sts2.Core.Combat/CombatManager.cs:1101-1115` `AddCreature`：`Monster?.SetUpForCombat()` 空安全，无需 guard。
- `MegaCrit.Sts2.Core.Combat/CombatManager.cs:1367-1376` `RemoveCreature`：`IsMonster` 前置，无需 guard。
- `MegaCrit.Sts2.Core.Nodes.Rooms/NCombatRoom.cs:871` `Side != Player && Monster != null` 双重前置，无需 guard。
- `MegaCrit.Sts2.Core.Combat/SentryService.cs:515-516`（crash 报告枚举 Enemies）：`e.Monster?.Id` 空安全，无需 guard。
- 控制台命令 `kill`/`damage`/`win`（`DevConsole.ConsoleCommands/*`）按 Enemies 桶操作 —— 对翻转对手同样生效，是 harness 的驱动工具，无需 guard。
- `CardModel.cs:1742`（`AnyAlly` 无存活队友判定）与 `AttackCommand.cs:174` 等以 `PlayerCreatures` 为「我方」的卡牌目标语义：翻转对手会被计入 `PlayerCreatures` —— 不崩溃，但「ally」语义在 T6 卡牌目标设计票需要显式裁决（记录为已知边缘，不在本票改）。

## T5 新增补丁（注册于 PatchTargetCatalog）

- 集 **SideFlip**：`Creature..ctor(Player,int,int)` postfix（翻转）、`Creature.AfterAddedToRoom` 前缀、`Creature.PrepareForNextTurn` 前缀、`Creature.TakeTurn` 前缀、`CombatManager.AfterCreatureAdded(Creature, CombatState)` 前缀。全部 `DuelScope.IsDuelRoom` 门控。
- 集 **DuelHarness**（单机假玩家，测试专用）：`RoomSet.Boss` setter 第二前缀（debug 标志开时单机也换入对决遭遇）、`PvpDuelEncounter.GenerateMonsters` postfix（对决房压掉占位怪）、`CombatRoom.StartCombat` 前缀（战斗建立前注入假对手 creature，使其获得可指向的 NCreature 节点）。

## 假玩家对决 harness：attended 触发步骤（单机）

前置：mod 装入 mods/（orchestrator 统一 staging，本票不落盘）；游戏本体可运行。

1. 主菜单按 `` ` ``/`'` 打开 modded console（`NDevConsole.cs:359`：`ModManager.IsRunningModded()` 即允许 debug 命令），输入 `pvpduel on` → 回显 `pvpduel harness ON`。
2. 正常开一局单机 run（任意角色/种子）。必须**先开标志再开 run**（act 房间在 run 创建时生成）。
3. 走图到本层 Boss 节点进入：房间应为对决房（boss 节点用 vanilla boss 贴图、`PvpDuelEncounter` 遭遇、无怪物），战斗开始时自动注入假对手（同角色、NetId 2、侧别 Enemy，立绘在敌方一侧）。
   - 兜底：若自动注入未发生（如标志晚开），战斗中执行 `pvpduel spawn` 手动注入；命令回显侧别与 HP。
4. 取证（取证 grep 走 log 文件，命令如下）：
   - `grep -E "\[pvpduel\] (duel room entered|side flip|fake opponent spawned|placeholder monsters suppressed)" <godot_log>` 应各出现 ≥1 条；
   - `grep -E "patch self-check passed \([0-9]+ target" <godot_log>` —— 目标/set 数随 catalog 各票汇总（本票贡献 8 个：SideFlip 5 + DuelHarness 3）；
   - `grep -E "DuplicateModel|TypeInitialization|0xC0000005" <godot_log>` 应无命中。
5. 交互断言（视觉/操作确认）：
   - 假对手在敌方一侧、可被 `AnyEnemy` 卡牌指向（意图箭头、单体选定框）；
   - 打出攻击牌：伤害数字/扣血出现在假对手身上，`ScreenShake` 触发（伤害链路指向对方）；
   - 给自己上格挡，让对手侧……（假对手不自动行动，改用控制台）执行 `damage <index>` 或直接攻击后，确认我方 block 正常抵扣（格挡链路）；
   - 双方互殴 3+ 回合（每回合结束轮转正常，敌方回合无异常日志）；
   - 执行 `kill`（无参杀第一个敌方 = 假对手）：其死亡可被检测（死亡动画、`IsCombatEnding` 收束、战斗以无奖励胜利收场 `ProceedWithoutRewards`），日志无未处理异常。
6. 回归冒烟（普通战斗零变化）：`pvpduel off` 后开新 run 打一场普通怪物战斗：怪物 AI、意图图标、击败流程与 vanilla 一致，日志无 `side flip` 行。

## manual items（无人值守不可执行，attended 统一验证阶段）

- 上述 attended 步骤 1-6 的全部实机行为（启动游戏、进入对决房、目标选择/伤害/格挡链路、假对手死亡收束、普通战斗回归）—— 依用户指令，本票不启动游戏，全部留给最终统一验证阶段。
- 视觉确认：假对手立绘位于敌方一侧且血条正常（`NCombatRoom` 布局按桶定位，代码层无 guard 点，需视觉确认）。
- 假对手使用 `me.Character` 实例创建 —— 若实机出现角色模型共享相关的意外（理论不期望），备选方案为 `ModelDb.GetByIdOrNull<CharacterModel>(me.Character.Id)` 取 canonical 实例（见 `DuelHarness.InjectFakeOpponent` 注释）。
