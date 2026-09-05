# EA 更新迁移 Runbook（PvpDuel）

适用场景：杀戮尖塔 2 发布新 EA 版本后，mod 需要跟进。目标：**游戏永不因签名漂移崩溃**——自检框架会把失配补丁集整体禁用，我们要做的是把漂移找回来。

## 步骤

### 1. 环境

- 安装新版本游戏；确认 `sts2.dll` 路径未变（默认 `data_sts2_windows_x86_64/sts2.dll`）。
- 反编译对齐：用 `ilspycmd`（需 `export DOTNET_ROOT='C:/Program Files/dotnet'`）把新 `sts2.dll` 转储到 `decomp/`（本地对照，不提交）。

### 2. 构建 + 测试

```powershell
dotnet build          # 0 错误
dotnet test           # 全量套件须全绿；反射/活体契约测试会直接暴露目标签名漂移
```

反射契约测试（各票的 `*PatchTargetTests` / `*GameSurfaceTests`）在测试进程内对真实 `sts2.dll` 断言目标方法形状——它们红 = 哪个补丁目标漂了，一眼定位。

### 3. 自检冒烟

1. `scripts/build.ps1` 产出 `build/mods/pvpduel/`，复制到 `<game>/mods/pvpduel/`。
2. 启动游戏，主菜单 grep 日志：
   - `grep "patch self-check passed" <godot_log>` —— 出现；
   - 目标数 = `PatchTargetCatalog` 汇总（各票贡献之和，见各 resolution 评论）；
   - **`grep -c "patch set disabled"` 应为 0**。非 0 → 记下被禁用的补丁集名。

### 4. 定位漂移补丁

- 被禁用集 → 该集对应的 decomp 类:方法，diff 旧版转储找签名/调用点变化。
- 修 `PatchTargetCatalog` 声明与补丁类；若扇出异步前缀，确认仍是 `ref Task __result` 模式（活体契约测试会兜底）。
- 重跑步骤 2、3 至全绿。

### 5. 冒烟 1-3 + 回归红线

- 冒烟 1：`pvpduel on` 开局进 boss 对决房，跑一幕（T6/T7 手册项）。
- 冒烟 2：双端联机一幕，出牌/结束全走 lockstep，`grep "second-hand machine hosts turn 1"` 仅非先手端。
- 冒烟 3：`pvpduel off` 后打一场普通战斗（AI/意图/敌方回合照常，`grep -c "turn drive"` = 0）。
- 回归红线：co-op 全流程（跑图/战斗/事件/boss）+ 单人全流程，行为与未装 mod 一致。

### 6. 发布

- 更新 `mod_manifest.json` 的 `version`（`min_game_version` 对齐新 EA 版本号）。
- README 故障排查表如有新症状一并更新。
- `megacrit/sts2-mod-uploader` 重新上传（Workshop 订阅者自动获得更新）。
