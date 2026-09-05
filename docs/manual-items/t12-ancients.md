# Manual (attended) verification items — T12 胜者先选 Ancient 流程与互斥（票 #24）

移交 T14 回归。除"单机 harness"条目外都需要真实双端会话（Steam 好友邀请、双端装 mod）。
日志位置：Godot log（`%APPDATA%/Godot/app_userdata/SlayTheSpire2/logs` 或
`%APPDATA%/SlayTheSpire2/logs`，随安装而定）；本 mod 所有日志行带 `[pvpduel]` 前缀。
先古之民解锁进度会影响该幕可选池（Orobas/Darv 等 epoch 门控），双端解锁状态需一致（原生 run 状态镜像）。

## 设计要点（读日志前先看）

- 对决落定 → 双方回到地图（非战斗房）那一刻，双端各自编程式进入
  `EVENT.DUEL_ANCIENT_EVENT_MODEL` 挑本体房（官方 `EventConsoleCmd.cs:42-44` 先例）。
- 胜者先选 → `AncientPickMessage`（host 转发）→ 败者端解锁（胜者已占项锁定）。
  执行层兜底：`EventSynchronizer.ChooseOptionForEvent` prefix 互斥校验，拒绝时本地提示
  `PVP_ANCIENT_PICK_TAKEN`（橙字 toast，2.5s 消失）。
- 本体选完后同房间内进入"祝福三选一"页——祝福选项 = 所选先古之民自身
  `GenerateInitialOptions` 的原池随机抽取（按 run seed + 玩家槽位派生，双端一致）。
- 第 3 幕胜利直接走终局钩子，不进 Ancient 流（acceptance #2）。
- 美术缺口的绕行：房间资源（背景/头像/图标）在进房前别名到 Darv 的原版资源
  （日志 `room assets aliased onto the vanilla ancient 'darv'`）。

## 验收 #1 —— 第 1/2 幕完整流程（真双端）

1. 双端进 2 人 co-op run，打完第 1 幕 boss 对决，双方点过结算画面回到地图。
2. 预期：双端自动进入"先古之民抉择"事件房（先古之民布局 + 对话行），
   选项 = 该幕 Ancient 池（真实名称/描述）。
3. 胜者端：立即可选，点选一个本体 → 进入该先古之民的祝福三选一 →
   选祝福获得遗物 → DONE 页 → PROCEED 回地图。
4. 败者端：在胜者选择前全部选项呈锁定（等待文案 `pages.WAITING.description`）；
   收到 `AncientPickMessage` 后解锁，胜者已选项锁定无法点选。
5. 败者选一个不同本体 → 各自祝福三选一互不干涉 → PROCEED。
6. grep 双端日志：
   - `grep -E "\[pvpduel\] ancient flow: player .* claimed .* for act 1" <log>`（各 1 条，本体互异）
   - `grep -E "\[pvpduel\] ancient flow: opponent claimed .* — mirror lock applied" <log>`（各 1 条）
   - `grep -E "\[pvpduel\] opening the Ancient body pick room for act 1" <log>`（各 1 条）
   - `grep -c "\[pvpduel\] ancient mutex: pick of .* rejected" <log>`（正常流程 = 0）
7. 第 2 幕重复一次（act 2 版本的上述行）。

## 验收 #2 —— 互斥执行层（真双端）

1. 第 1 幕胜者先选本体 A，等败者端解锁后，用调试手段（或手速）让败者点已被占的 A。
2. 预期：A 选项不可点（锁定态）；若仍触发，则事件页面停留不动 + 橙色 toast
   （`PVP_ANCIENT_PICK_TAKEN`），不产生网络动作，随后可选其他本体。
3. grep：`grep -E "\[pvpduel\] ancient mutex: pick of .* rejected \(TakenByOpponent\)" <log>`（触发端 ≥1 条）。

## 验收 #3 —— 第 3 幕无 Ancient 环节（真双端）

1. 连打到第 3 幕 boss 对决并打完。
2. 预期：不进入先古之民房，直接走终局结算（T13+ 接终局画面；本票范围内表现为
   回到地图后无 pick 房，落定行照常出现）。
3. grep：`grep -E "\[pvpduel\] opening the Ancient body pick room for act 3" <log>`（双端应为 0 条）；
   `grep -c "\[pvpduel\] ancient flow: act 3 settled .* — the Ancient body pick opens" <log>`（应为 0）。

## 验收 #4 —— checksum 零分歧 + §12.1 分叉验证（真双端）

1. 跑完验收 #1 的两幕后，连续正常游玩至第 3 幕（含跑图、商店、事件、休息）。
2. 全程无官方 StateDivergence 断线；grep：
   `grep -c "state divergence\|StateDivergence" <log>`（= 0，官方关键词）；
   `grep -c "\[pvpduel\] duel outcome DIVERGENCE" <log>`（= 0，mod 关键词）。
3. 分叉选择本身（胜者选 Darv、败者选 Orobas 这类"同房不同体"组合）即 §12.1 的
   实测项：镜像同步缓解 = 同一事件房内双方实例 + 选项执行经
   `OptionIndexChosenMessage` 镜像（ relic 落袋双端同步）；兜底 = 祝福池无可移植
   选项时统一 PROCEED（日志
   `grep -E "\[pvpduel\] ancient flow: .* yielded no transplantable blessing options" <log>`，
   出现即记录该兜底生效，需附日志上报）。
4. SL 验证：第 1 幕 pick 完成后保存退出读档，`pvpdueloutcome dump` 中 History 不变；
   若存档发生在 pick 房中途，读档后 pick 房重开、已记录本体不可更改（换本体会被
   AlreadyPicked 拒绝并 toast），祝福重新抽取（种子派生，与存档前一致）。

## 单机 harness 路径（单机即可）

1. 控制台 `pvpduel on` 起 run，`pvpdueloutcome` 观察第 1 幕假人对决。
2. 预期：落定行 `duel outcome settled locally` → 回地图后自动进入 pick 房
   （单玩家、无对手锁）→ 选本体 → 祝福 → DONE → PROCEED 继续跑图。
3. grep：`grep -E "\[pvpduel\] ancient flow: player .* claimed .* for act 1" <log>`（1 条）。

## 已知缺口（acceptance #4 文档化）

- `ActModel:PullAncient` 无 hook：地图生成层不经过本 mod（本 mod 拆分地图本就无
  每幕 Ancient 房），先古之民只能经对决胜利获取——属规格预期行为。
- 祝福池硬编码：无法 hook 各先古之民的池定义；实现以"工具克隆 + 反射调用原
  `GenerateInitialOptions` + `EventOption.FromRelic` 移植"复用原逻辑（双端同种子
  派生，确定性）。非遗物型祝福选项无法移植，跳过并记录；全池不可移植时统一
  PROCEED（见验收 #4.3）。
- 共享池子集（`ActModel._sharedAncientSubset`）无公开 getter：反射读取；字段漂移时
  双端一致降级为"仅该幕解锁池"（日志
  `grep -E "\[pvpduel\] ancient pool: shared subset unreadable" <log>`）。
