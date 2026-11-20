# 计划 - CLI 大厅协议与准入网关

> 目标：让 CLI 参与 pcl-next 联机房间时「像正经成员」，并把「谁都能连 MC 端口」改成「没有协议凭证就进不来」。

## 1 目标与验收

1. CLI `link join` 加入 p-next 房间后，房主成员列表里出现该成员的**昵称**（不再只是 hostname），且可被 kick；被 kick 后 CLI 自动退出（或至少断开转发）。
2. 邀请码不含端口时，CLI 能通过房主协议问出 MC 端口，不必手填 `--port`。
3. 房主启用网关后，EasyTier 只放行 gate 端口与协议端口，**MC 端口不再直接暴露**；未完成协议认证的连接一律拒绝并审计。
4. 认证具备：新鲜 nonce（防重放）、随机会话凭证（只在协议通道下发）、失败限速与超时、被踢立即撤销。
5. 兼容边界：网关只对 pcl-next 自家房间（`LobbyKind.PclNext`）启用；PCL2-CE / 陶瓦 互通房间保持旧直连行为，避免把对方挡在门外。

## 2 现状与问题

- 大厅协议 `ModLinkScaffold`（帧：`1 字节类型长度 + 类型 + 4 字节大端正文长度`；应答：`1 字节状态 + 4 字节长度`）只存在于 GUI 工程，CLI 没有实现，因此：
  - 房主成员列表看不到 CLI（列表只来自协议登记）；
  - 端口不在码里时 CLI 无法自动发现；
  - kick 对 CLI 完全无效。
- MC 端口由 EasyTier 直接 whitelist + 端口转发暴露，任何进了 overlay 的节点（哪怕没有邀请码外的凭证）都能连 25565，房主侧无任何认证面。

## 3 设计

### 3.1 协议客户端（PCL.Core/Link/PclLinkScaffoldClient.cs）

- 复用 GUI 的帧格式与类型名（`c:ping` / `c:protocols` / `c:server_port` / `c:player_ping` / `c:player_profiles_list`），新增 `c:gate`。
- 提供：`Register(name, machineId, vendor)`（即 `c:player_ping` 心跳）、`GetPlayers()`、`GetServerPort()`、`GetGate()`。
- 昵称：`pclcli-<4 位随机>`，machine_id 落盘在 `<workdir>/link/machine-id`。
- 端口发现：房主主机名形如 `pclnext-mc-server-<scaffoldPort>`，从中取协议端口；加入侧先为本机开一条 `127.0.0.1:<scaffoldLocal> → overlay:scaffoldPort` 的转发。

### 3.2 准入网关（PCL.Core/Link/PclLinkGate.cs）

- 房主：`PclLinkGateHost` 监听 `127.0.0.1:<gatePort>`，只向协议通道发放一次性凭据（`c:gate` 返回 `{port, ticket, expires}`）；gate 端口由 EasyTier whitelist 放行，MC 端口不再放行。
- 握手（行协议，均为 UTF-8 单行）：
  1. 房主 → `PCLGATE1 <nonce>`
  2. 加入 → `<ticket> <proof>`，`proof = HMACSHA256(roomKey, nonce + "|" + ticket)`，`roomKey = SHA256("pclnext-gate|" + networkSecret)`
  3. 房主校验 ticket 存在、未撤销、未过期、proof 正确 → `OK`，随后原样双向转发到 `127.0.0.1:<mcPort>`；任一步失败 → `DENY <reason>` 并断开。
- 加入侧：`PclLinkGateClient` 监听 `127.0.0.1:<localPort>`，MC 每来一条连接就向 `overlay:<gatePort>` 握手，成功后双向转发。
- 加固：握手 5 秒超时；同一房主同时最多 2 个未完成握手；失败计数超过阈值后 60 秒内拒绝新握手；ticket 12 小时过期；kick 时撤销 machine_id 对应 ticket 并断开在途会话；所有接受/拒绝写日志（含 machine_id、原因、时间）。

### 3.3 接线

- GUI 房主（仅 PclNext 房间）：启动 gate → EasyTier 参数里 whitelist 改为「协议端口 + gate 端口」，去掉 MC 端口；`c:gate` 由 `ModLinkScaffold.Server` 处理并调用 gate 发放凭据。
- GUI 加入者：拿到 gate 信息就起隧道，否则回退旧端口转发。
- CLI 加入：注册协议 → 问端口（若码里没有）→ 请求 gate → 起隧道；拿不到 gate 则回退旧转发并提示。
- CLI 房主：本轮不启用 gate（CLI 无大厅服务端，无法发放凭据），保持旧行为，文档注明。

## 4 改动文件

- 新增 `PCL.Core/Link/PclLinkScaffoldClient.cs`、`PCL.Core/Link/PclLinkGate.cs`。
- `PCL.Core/Link/PclLinkSession.cs`：join 接入协议与网关；LinkState 增加 `ScaffoldPort` / `GatePort` / `GateLocalPort` / `Nickname`。
- `PCL.Cli/Commands/LinkCommand.cs`：状态输出补昵称/网关信息；`--port` 变为可选。
- `pcl-next/Modules/Link/ModLinkScaffold.cs`：新增 `c:gate` 处理、ticket 撤销接口。
- `pcl-next/Modules/Link/ModLinkSession.cs`：房主启动 gate / 白名单调整；加入者走隧道。
- `docs/plans/计划-CLI联机模块.md`：追加实测记录。

## 5 验证

1. CLI↔CLI（无 GUI）：create + join，确认旧路径仍可用。
2. GUI 房主 + CLI 加入：房主成员列表出现 `pclcli-xxxx`；CLI 无需 `--port`。
3. 绕过测试：直接连 `127.0.0.1:<MC 端口转发>` 应失败（whitelist 不含 MC 端口）；不发握手的裸连接应在 5 秒内被拒并有审计日志。
4. kick 测试：房主踢出后，CLI 现有隧道断开、重连被拒。
5. 回归：PCL2-CE 互通房间（旧直连）行为不变。

## 6 回滚

删除两个新文件、还原上述 5 个文件的对应改动即可；gate 只在 PclNext 房间启用，回滚后自动回到「EasyTier 直连 MC 端口」旧行为，不影响既有房间。
