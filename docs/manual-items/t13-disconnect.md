# Manual (attended) verification items — T13 掉线暂停与超时判负（票 #25）

移交 T14 回归。全部联机条目需要真实双端会话（Steam 好友邀请、双端装 mod）；
超时倒计时条目可先把双端 `pvpduel_config.json` 的 `disconnectTimeoutSec` 调小
（如 15）以缩短等待，测完改回。日志位置：Godot log
（`%APPDATA%/Godot/app_userdata/SlayTheSpire2/logs` 或 `%APPDATA%/SlayTheSpire2/logs`）；
本 mod 所有日志行带 `[pvpduel]` 前缀。**mod 不写任何补丁干扰官方断线路径**
（本票 0 Harmony——全部挂官方事件，跑图掉线必须与装 mod 前行为一致）。

前置：`pvpduel_config.json` 双端一致（`disconnectTimeoutSec` 计入配置 hash，
双端不一致会被 §4.4 门槛拒绝）。

验收 #1 —— 对决中一端断网：host 见暂停 + 倒计时；重连后从暂停点继续：

1. 双端进 2 人 co-op run，任一方进入第 1 幕 boss 对决房（对决进行中）。
2. **client 端**强退进程（或断网 ≥30s 再恢复，Steam 重连）。
3. 预期（host 端）：屏幕左上出现橙色倒计时横幅（键 `PVP_DUEL_RECONNECT`，
   中文形如「对手掉线——对决暂停，等待重连（剩余 N 秒）…」），数字每秒递减。
4. client 在倒计时内重进游戏并重新加入对局（官方 rejoin 流程）。
5. 预期（host 端）：倒计时横幅消失；对决从暂停点继续（client 经官方 rejoin
   恢复战斗状态 `NetFullCombatState`）；打完该场对决正常落定。
6. host 端 grep：
   - `grep -E "\[pvpduel\] duel disconnect: peer .* dropped mid-duel" <log>`（1 条）
   - `grep -E "\[pvpduel\] disconnect watch: player .* rejoined" <log>`（1 条）
   - `grep -E "\[pvpduel\] duel outcome settled" <log>`（该场对决最终照常落定）
7. client 端 grep：
   - `grep -E "\[pvpduel\] state backfill requested from the host" <log>`
   - `grep -E "\[pvpduel\] mod state backfill applied" <log>`（History/picks/分支表经网络还原）

验收 #2 —— 超时 120s 判负：落定 DisconnectTimeout，胜者继续推进：

1. 建议先把双端 `disconnectTimeoutSec` 调为 15（配置 hash 双端同步改）。
2. 同验收 #1 步骤 1-2，但 client 保持离线直到倒计时走完。
3. 预期（host 端）：倒计时结束出现红色判负横幅
   （键 `PVP_DUEL_DISCONNECT_TIMEOUT`：「{netId} 掉线——超时 N 秒，判负。」），
   随后按幕号推进：第 1/2 幕 → 胜者（host）进入「挑先古之民本体」事件房
   （Ancient 流程，票 #24）；第 3 幕 → 终局钩子（本票范围内仅落定+派发）。
4. host 端 grep：
   - `grep -E "\[pvpduel\] duel disconnect: reconnect timeout elapsed" <log>`（1 条）
   - `grep -E "\[pvpduel\] duel forfeit: act .* \(DisconnectTimeout\)" <log>`（1 条）
   - `grep -E "\[pvpduel\] duel outcome settled locally" <log>`（判负经结算层
     立即落定——对端缺席，不得出现 divergence）
   - `grep -c "\[pvpduel\] duel outcome DIVERGENCE" <log>` 必须为 0
     （超时判负 ≠ divergence，对局继续）
5. client 重新上线 rejoin：恢复到 host 推进后的幕（官方 rejoin 序列化），
   其 mod History 含该场 DisconnectTimeout 记录（backfill 应用后由
   `pvpdueloutcome dump` 比对双端一致，见 T11 manual 验收 #1 的 dump 流程）。

验收 #3 —— 跑图中掉线沿官方行为（回归红线）：

1. 双端在**跑图阶段**（非对决房）让 client 断网/强退。
2. 预期：host 不出现任何暂停倒计时、不判负；继续跑图（官方 co-op 行为：
   `RemotePlayerDisconnected` 仅登记输入断开）。
3. host 端 grep：`grep -c "\[pvpduel\] duel disconnect: peer .* dropped mid-duel" <log>`
   = 0（跑图掉线不触发 mod 等待层）。
4. client 重连后照常继续（官方 rejoin；mod 状态经 backfill 还原）。
5. 对照装 mod 前行为（卸载 mod 或同 seed 官方 co-op）：断线→重连的表现一致。

验收 #4 —— 倒计时数值走配置（§4.4）：

1. 双端 `disconnectTimeoutSec` 同步改为 15，重进对局重复验收 #1 步骤 2-3。
2. 预期：host 倒计时从 15 开始递减（横幅数字），约 15s 后判负。
3. 双端故意配置不一致（改单端）：对决开始前的配置一致性门槛应拒绝
   （`PVP_DUEL_CONFIG_MISMATCH` 横幅，票 #15/#17 行为，确认未被本票破坏）。

边界 —— 对决中掉线时**结算窗口**的已知窄沿（记录行为即可）：

1. 双方打完对决的瞬间（双端 DuelOutcomeMessage 交叉窗口 ~30s 内）让 client
   断网且其上报未能发出：host 的 30s 共识看门狗仍会按 divergence 中止
   （这是 T11 落定层语义，非本票等待层管辖）。
   `grep -E "\[pvpduel\] duel outcome: remote report overdue" <log>`。
2. 该沿属已知设计取舍：落定窗口极短，且中止为官方 teardown、无软锁；
   若实测频率高再升级（把 pending 共识并入等待层管辖）。

单机 harness 回归（无网络，反证本票不干扰既有路径）：

1. 单机 `pvpduel on` 起假人对决，正常打完。
2. `grep -c "\[pvpduel\] disconnect watch" <log>`：仅出现 install 行
   （单机无 RunLobby 断线事件，等待层静默）。
3. 对决落定行为与 T11 manual 回归条目一致（`settled locally`）。
