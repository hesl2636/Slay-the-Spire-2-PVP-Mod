# Manual items — Ticket #22 (T10 幕计时与先手裁定)

实机项（attended，双端联机）。单元/契约层（计时聚合、<1s 回退、RNG 派生确定性、
消息往返）已由 `tests/PvpDuel.Core.Tests/Timing/` 覆盖；以下为真双端才能验证的项。
完成后请在本文件勾选并回填日志证据。

日志通道：`[pvpduel]` 前缀，Godot 日志
`%APPDATA%/Godot/app_userdata/SlayTheSpire2/logs`（或
`%APPDATA%/SlayTheSpire2/logs`，视安装方式）。

前置：双端安装同版本 pvpduel.dll，Steam 好友邀请进 co-op 房，同种子开跑。

## 1. 先手落定与双端一致（验收 #1）

步骤（每幕重复，至少跑两幕）：

1. 双端各自进图、走各自分支路径，抵达 boss 房等待。
2. 汇合后进入对决，观察第一回合先行动方（先手方先行，T6 回合驱动消费 `DuelContext.FirstHandNetId`）。
3. 两端分别核对：先行动的玩家是否相同（多幕多次）。

grep（两端日志各跑一次）：

```bash
grep "act timer: act" <godot.log>
# 期望每幕一行：act N first hand -> player <netId> (rngFallback=False, players X/Y at Ams/Bms)
grep "duel room entered" <godot.log>   # 对决房窗口开启
```

判定：两端同幕的 `first hand -> player` 值一致；先行动方与该值一致。

## 2. 计时不影响跑图（验收 #3）

步骤：整幕正常跑图（事件/商店/宝箱/休息/精英各一间），确认无卡顿、无额外
网络动作、无 checksum 分叉提示。

```bash
grep -c "act timer" <godot.log>            # 仅汇合时出现两条/幕（host 发送、client 接收）
grep "StateDivergence\|divergence" <godot.log>   # 期望 0 命中
```

判定：跑图行为与不装 mod 的 vanilla co-op 无差异。

## 3. RNG 回退路径（验收 #2）

同秒/极差场景天然难复现。确定性本身已由单元测试钉死（同种子同结果；
FirstHandRngSurfaceTests 对 sts2.dll 活体流逐位等价）。实机建议（可选）：

1. 两端同时点同一起点出发、同时抵达 boss（尽量同秒）。
2. 若日志出现 `rngFallback=True`，核对两端 `first hand -> player` 一致。

```bash
grep "rngFallback=True" <godot.log>
```

判定：命中时两端 winner 一致；未命中不阻塞（概率性场景，单元已覆盖）。

## 4. SL 恢复边界（已知限制确认）

步骤：幕内存档读档后跑完该幕。

```bash
grep "act timer" <godot.log>
```

已知限制：读档后的当幕计时从读档点重新起算（SetActInternal 重触发），先手
可能偏随机但双端仍由同一规则+同种子推导、保持一致；若日志未出现 act timer
行，先手回退 lower-NetId 规则（两端一致，可接受）。确认：不崩、不软锁、
两端先手一致。
