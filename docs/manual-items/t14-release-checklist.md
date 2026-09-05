# T14 发布 —— 集中实机验证清单（需要你参与）

自动化部分已由 orchestrator 完成：全量测试 **346/346**、本地化键终审（无缺失/双语对称/镜像同步）、manifest 终值、README + EA runbook 终版。以下为**禁令解除后**的集中验证（一次性，全部通过即可关闭 #26 与 epic #12）。

总原则：装回 mod（`build/mods/pvpduel/` → `<game>/mods/pvpduel/`，双机同版本同配置哈希）；每项的取证 grep 命令在对应手册文档里；日志为 Godot `godot.log`。

## A. 启动自检（T1/T3/T4 基线回归）

- [ ] `patch self-check passed`，目标数 = 各票汇总（PatchTargetCatalog 全集）；`grep -c "patch set disabled"` = 0
- [ ] 主菜单无错误横幅；`Loaded 1 mods`

## B. 单机 harness（无对手，可单人完成）

- [ ] `pvpduel on` 开局 → 分幕双路径图（T4）；boss 对决房进入（T3）
- [ ] 对手回合严格交替 ≥5 回合（`pvpduelturn end` 驱动，能量重置/抽 5，T6 五项取证）
- [ ] 我方死亡 → Fate Thread 复活 1HP → 对手被强制击杀 → 原版胜利收束（T7 M1-M5）
- [ ] `pvpduel outcome dump` 显示本地 History；`pvpdueloutcome status` 正常
- [ ] Ancient 流程单机路径（T12 手册「单机 harness 路径」）
- [ ] 存档侧通道：退出重进 run 恢复（grep `save side channel written/present/restored`；文件含 `"schema":1`；损坏 JSON → 降级警告，官方存档不受影响，T9 六项）
- [ ] 回归红线：`pvpduel off` 普通战斗 AI/意图/敌方回合零变化；co-op（不开对决）跑图/战斗/事件/boss 全程与未装一致

## C. 真双端联机（核心验收，规格 §11）

- [ ] 幕计时与先手：`act N first hand -> player <id>` 双端一致；<1s 差 → `rngFallback=True` 回退（T10 四项）
- [ ] 结算一致：出牌/结束全 lockstep，ChecksumTracker 零分歧；`second-hand machine hosts turn 1` 仅非先手端（T6）
- [ ] Ancient 流程真双端：胜者先选 → 对手镜像锁定；重复选 → `PVP_ANCIENT_PICK_TAKEN`；幕 3 不进 Ancient（T12 验收 1-3）
- [ ] §12.1 分叉检查：Ancient 环节后双端 checksum 零分歧（T12 验收 4）
- [ ] 掉线：对决中掉线 → 倒计时横幅 → 重连恢复（官方战斗态 + mod 状态回填）；120s 超时 → DisconnectTimeout 判负、对局继续；跑图中掉线 = 官方行为零差异（T13 四项）
- [ ] 结算分歧注入（`pvpdueloutcome on`）→ `PVP_ERROR_DIVERGENCE` → 官方断线到主菜单，双端同走（T11 验收 2）

## D. 发布动作（A-C 全过后）

- [ ] Workstation 双机干净订阅/安装验证（无开发环境机器能进房对局）
- [ ] Workshop 上传（`megacrit/sts2-mod-uploader`；manifest version=1.0.0）；描述页文案可用 README Features 一节
- [ ] 规格验收 §11 逐条附证据（日志/录屏）评论到 #26；关闭 #26 → 关闭 epic #12 + 地图 #1 终态

## 已知边界（预期内，不算失败）

- 结算窗口（~30s）内掉线走官方分歧中止，无软锁
- 祝福无可移植项 → 双端统一 PROCEED
- 客户端目击 host 掉线 → 纯官方拆线
