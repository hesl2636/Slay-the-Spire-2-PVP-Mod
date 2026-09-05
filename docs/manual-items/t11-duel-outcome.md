# Manual (attended) verification items — T11 DuelOutcome 双端落定（票 #23）

移交 T14 回归。全部条目需要真实双端会话（Steam 好友邀请、双端装 mod）或单机 harness（标注处）。
日志位置：Godot log（`%APPDATA%/Godot/app_userdata/SlayTheSpire2/logs` 或
`%APPDATA%/SlayTheSpire2/logs`，随安装而定）；本 mod 所有日志行带 `[pvpduel]` 前缀。
控制台命令 `pvpdueloutcome` 为 DebugOnly（modded console）。

验收 #1 —— 真双端完整对决，双端 `DuelHistory` 一致：

1. 双端进 2 人 co-op run，完整打完第 1 幕 boss 对决（任一结束理由）。
2. 双端各自打开 modded console 执行 `pvpdueloutcome dump`，逐字段比对（act/winner/loser/reason）。
3. grep 双端日志确认同一落定行：
   `grep -E "\[pvpduel\] duel outcome settled by both ends" <log>`
4. 预期：双端 dump 输出完全相同；日志行各出现一次；无 DIVERGENCE 行
   （`grep -c "\[pvpduel\] duel outcome DIVERGENCE" <log>` = 0）。

验收 #2 —— 人为制造不一致 → 双端中止 + 本地化错误：

1. 在其中一端（建议 client）执行 `pvpdueloutcome divergence on`。
2. 进入并打完任意一场 boss 对决。
3. 预期（双端一致）：双方立即回到主菜单并弹官方 StateDivergence 错误框；
   双端屏幕左上出现红色 mod 横幅（键 `PVP_ERROR_DIVERGENCE`）。
4. 双端 grep：
   `grep -E "\[pvpduel\] duel outcome DIVERGENCE" <log>`（各 1 条，含两端结果描述）
   `grep -E "\[pvpduel\] DEBUG divergence switch is ON" <log>`（仅开开关的一端）
5. 确认 `pvpdueloutcome dump` 在双端都**不**记录这场分歧对决（落定前不进 History）。
6. 完成后 `pvpdueloutcome divergence off`。

验收 #3 —— 第 3 幕落定触发终局事件钩子（消费方未接线，日志可观测）：

1. 双端连打三幕（或用 seed 快速到第 3 幕 boss 对决）并完成。
2. 预期：第 3 幕落定行照常出现（`duel outcome settled by both ends`）。
3. 终局钩子事件本身无消费者（空实现，本票不实现终局画面）；用反证确认无异常：
   `grep -E "\[pvpduel\].*(exception|failed)" <log>` 应无新增 settlement 相关错误。

回归 —— 单机 harness 路径不受接管影响（T7 行为保留）：

1. 单机 `pvpduel on` 起 run，进 boss 房打完假人对决。
2. 预期：落定行变为 `duel outcome settled locally (single-machine duel…)`：
   `grep -E "\[pvpduel\] duel outcome settled locally" <log>`
3. `pvpdueloutcome dump` 应出现该结果（单机立即入 History，无消息往返）。

回归 —— SL 恢复（与 #21 交互）：

1. 落定第 1 幕后保存退出，重新读档。
2. `pvpdueloutcome dump` 与存档前一致（History 经 side channel 还原）。
3. 读档后直接进第 2 幕对决，落定仍正常（无 stale pending：grep 无
   `dropping an unfinished wait`，或该行仅在预期场景出现）。

边界（可选，如网络工具可注入）—— 超时未收到对方消息：

1. 一端在本地报告发出后立即断网（模拟对端死亡）。
2. 预期 30s（`DuelOutcomeConsensus.DefaultRemoteWaitTimeout`）后该端走 divergence：
   `grep -E "\[pvpduel\] duel outcome: remote report overdue" <log>`
