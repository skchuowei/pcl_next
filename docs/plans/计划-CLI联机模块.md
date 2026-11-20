# 计划-CLI 联机模块（PCL.Core/Link + pclcli link）

> 状态：已落地（2026-11-20，构建 `2.100.0.7-2.13`，Windows 0 错误；本机双实例全链路实测通过）
> 关联：`docs/plans/计划-内核与GUI拆分（CLI）.md`（T2 拆壳主线）、`docs/plans/计划-联机大厅移植.md`（GUI 侧联机）、`AGENTS.md` §0.1 / §3.1。
> 一句话：把联机从「只能点 GUI 卡片」扩成「纯命令行可建房/加入/验证」，让无 GUI 的云端 Linux 与本地 Windows 用同一条 EasyTier overlay 链路做联机测试，**不启动游戏**。

## 1 目标与验收标准

1. `pclcli link install / create / join / status / stop / probe / echo / selftest` 可用，纯 BCL、零第三方依赖，Windows 与 Linux 同一套代码。
2. 会话可跨进程：`link create` 返回后核心进程继续后台运行，`status / stop` 按状态文件里的 PID 与 rpc-portal 端口控制。
3. 联机链路可被验证，而不是「编译通过就当成功」：房主跑 `link echo` 当假服务端，加入者 `link probe` 打一次 TCP 回显，回显一致才判通。
4. 不依赖启动器界面、不启动 Minecraft、不改 GUI 代码。

## 2 现状与阻塞

- GUI 侧 `pcl-next/Modules/Link/*` 已经有一套自研 EasyTier 封装，但引用 `MeloongCore` / `Newtonsoft.Json` / `Settings`，且进程由 GUI 进程持有，**CLI 无法直接复用**。
- GUI 内置公共节点 `public.easytier.top:11010`、`public2.easytier.cn:54321` 在 2026-11-20 实测**已无 A 记录**（阿里 DNS 权威返回 NXDOMAIN），Windows / 云端 Linux 均 `connect timeout`。这不是本机封端口。
- 云端 Linux 无法操作 GUI，且默认无 .NET 运行时（本次实测 `~/.dotnet` 已装 10.0.403 SDK，只是没进 PATH）。

## 3 改动文件清单

| 文件 | 作用 |
|---|---|
| `PCL.Core/Link/PclLinkCode.cs` | 邀请码编解码（与 GUI `ModLinkCodec` 同格式：5-5-8，兼容旧 9-5-4） |
| `PCL.Core/Link/PclLinkEasyTier.cs` | 按平台下载/解压 EasyTier、启动/停止、`easytier-cli -o json` 查询、空闲端口、peer 过滤 |
| `PCL.Core/Link/PclLinkSession.cs` | 会话状态文件、建房/加入状态机、TCP 探测、TCP 回显服务 |
| `PCL.Cli/Commands/LinkCommand.cs` | `link` 子命令与 JSON Lines 输出 |
| `PCL.Cli/Program.cs` / `CliExitCode.cs` | 注册 `link`、补帮助；新增退出码 `7 = 联机失败` |

**明确不做**：不搬 GUI 的 Scaffolding 大厅协议、不做公开房间列表、不启动游戏、不修改任何 GUI 文件、不把 EasyTier 写进仓库（运行期按平台下载到 `--workdir/link/easytier`）。

## 4 关键实现点

1. **平台差异只有三处**：发行包名（`easytier-windows-x86_64-...zip` / `easytier-linux-x86_64-...zip`）、可执行名（`.exe` 后缀）、解压后 `chmod +x`。其余参数完全一致。
2. **参数与 GUI 对齐**：`--no-tun`、`--network-name`、`--network-secret`、`--relay-network-whitelist`、`--listeners tcp/udp`（显式给协议，避免 EasyTier 顺带占 +1/+2/+3 端口）、`--rpc-portal 127.0.0.1`、`--private-mode true`；房主额外 `-i 10.114.114.114` + 端口白名单；加入者 `-d` + `--port-forward tcp/udp 127.0.0.1:<local>/10.114.114.114:<port>`。
3. **进程脱离 CLI**：写一个启动脚本（Windows `.cmd` / Linux `.sh`）把核心 stdout/stderr 重定向到 `link/easytier-<tag>.log`，再启动脚本。Linux 用 `exec` 让 sh 变成 core（PID 直接可 kill）；Windows 保留 `cmd.exe` 父进程，`Process.Kill(entireProcessTree: true)` 连子进程一起收。
4. **状态文件**：`<workdir>/link/session.json`（mode / 邀请码 / 网络名/密钥 / overlay IP / 服务端口 / 本地转发端口 / rpcPort / PID / 日志路径）。每次 CLI 调用独立进程，全靠它续接。
5. **peer 口径**：EasyTier 的 `peer list` 同时包含公共共享节点（`hostname = PublicServer_*`）。`remotePeers` 必须排除它们，否则「加入成功」会在根本没连上房主时成立；加入判定改为「列表里出现 `pclcli-host-` 主机名或房主 overlay IP」。
6. **下载源排序 + 慢源熔断**：云端 Linux 上 GitHub 直连实测 3 KB/s（24 MB 要 40 小时级），`ghfast.top` 约 120 KB/s。因此镜像顺序改为 `ghfast → gh-proxy → 直连 → ghproxy`，并加「20 秒不足 512 KB 立即换源」的熔断，避免耗死在一个源上。

## 5 验证步骤与证据

### 5.1 本机（Windows）双实例全链路

```powershell
pclcli link echo --port 25566                                   # 假服务端
pclcli link create --port 25566 --workdir <A>                   # 房主
pclcli link join <邀请码> --port 25566 --local-port 25567 --workdir <B>
pclcli link probe --target 127.0.0.1:25567
```

实测（2026-11-20）：

- 建房：`R9VGH-8ZSCK-1bc8bcfd`，房主 pid 30088，`nodeAlive=true`；
- 加入：`已加入 pclnext-R9VGH`，本地转发 `127.0.0.1:25567 → 10.114.114.114:25566`；
- peer：房主侧看到 `pclcli-client (10.114.114.1, cost=p2p)`，另有 4 个 `PublicServer_*` 中转节点；
- 探测：`连接 27 ms，往返 33 ms，回显一致`，退出码 0；
- `stop` 后 `easytier-core` / 启动脚本无残留。

### 5.2 跨机（云端 Linux 房主 + 本地 Windows 加入）—— 已实测通过

云端 `115.190.67.243` 无入站（本地 TCP 40001 直接超时），公共节点是唯一可行 rendezvous。
云端 `~/.dotnet` 已有 10.0.403 SDK（只是不在 PATH），因此无需安装运行时，补 `DOTNET_ROOT` 即可：

```bash
export DOTNET_ROOT=$HOME/.dotnet
dotnet build PCL.Cli/PCL.Cli.csproj -c Release -r linux-x64     # 0 错误（2 条既有 PCL.Download 告警）
pclcli link install --workdir /home/user/pclcli-linktest        # ghfast 243 KB/s，24.3 MB
pclcli link echo --port 25566 &
pclcli link create --port 25566 --workdir /home/user/pclcli-linktest
```

2026-11-20 实测记录：

| 环节 | 结果 |
|---|---|
| 云端建房 | 邀请码 `G9ZGM-HRE76-941596da`，pid 1992，`nodeAlive=true` |
| 本地加入 | `已加入 pclnext-G9ZGM`，`hostPresent=true`，房主 `10.114.114.114` `cost=relay(2)` 530 ms |
| 云端看到的成员 | `10.114.114.1` / `pclcli-client` / `cost=relay(2)` / 60 ms |
| 云端 echo | `收到连接：127.0.0.1:47714`，`回显 17 字节` |
| 本地 probe | 连接 18 ms、往返 674 ms、回显一致，退出码 0 |
| 隧道日志 | 两条 tcp 隧道分别落在 `easytier.weiai.org.cn:11010`、`225284.xyz:11010` |

结论：`link` 模块在无 GUI 的云端 Linux 上可独立完成建房/回显，本地 Windows 用同一份代码加入并把房主端口转发到 `127.0.0.1`，链路与端口转发均成立；**不启动游戏也能判断联机是否可用**（用 probe 回显判定，而不是看进程是否活着）。

默认公共节点（2026-11-20 云端与本地双向 TCP OPEN，可作 `--peer` 覆盖）：

```
tcp://225284.xyz:11010
tcp://easytier.weiai.org.cn:11010
tcp://183.230.36.171:11010
tcp://et.sumnight.top:11010
```

## 6 踩坑记录

1. **内置公共节点已死**：`public.easytier.top` / `public2.easytier.cn` 已 NXDOMAIN，日志里全是 `connect timeout after 1.9s`。换社区节点后 20 s 内即连上。
2. **公共节点会污染「已加入」判断**：见 §4.5，必须按 hostname 过滤基础设施 peer。
3. **只给端口号会让 EasyTier 多占端口**：`--listeners` 必须显式写 `tcp://` 与 `udp://` 两条。
4. **Linux 解压后可执行位会丢**：`ZipFile.ExtractToDirectory` 不保留 mode，必须对 `easytier-core` / `easytier-cli` 调 `File.SetUnixFileMode`。
5. **父进程退出会带走子进程的输出管道**：不能用 `RedirectStandardOutput` 直接起核心，必须走启动脚本重定向到日志文件（见 §4.3）。
6. **沙箱 DNS 不可用作网络判据**：`getent` 无结果不等于节点不可达，要用 DoH 复核（本次 DoH 直接给出 NXDOMAIN 才是真死）。

## 7 追加：安全模式与 GUI 房间可达性（2026-11-20 晚，实测）

1. **安全模式必须显式对齐加密算法**：GUI 建房参数是 `--secure-mode true --encryption-algorithm aes-gcm`；CLI 只给 `--secure-mode true` 时，房主侧会以
   `conn closed during wait handshake response` 直接断开（本机直连也失败）。已在 `PclLinkSession.BaseArguments` 里补 `aes-gcm`，与 GUI 一致。
2. **GUI 房间目前没有可达中继**：`ModLinkEasyTier.PublicPeers` 仍是 `public.easytier.top` / `public2.easytier.cn`（已 NXDOMAIN），
   所以 GUI 建出的房间只在局域网可达，云端拿到邀请码也找不到房主。
3. **验证用「本地 CLI 节点做桥」**：本地节点同时 `-p` 四个社区节点与 `-p tcp://192.168.101.17:30005`（GUI 的监听端口），
   房主与云端都连到这座桥。云端 `link status` 实测可见：
   `10.114.114.1 pclcli-client relay(2) 233 ms`、`10.114.114.114 pclnext-mc-server-30003 relay(3) 238 ms`，`hostPresent=true`。
4. **桥只能用 LAN IP**：同机连 `127.0.0.1:30005` 会报 `10049 AddrNotAvailable`（EasyTier 的 TCP 绑定行为），改用本机 WLAN 地址即通。
5. 房间内没有开 MC 服务端，所以 `link probe --target 127.0.0.1:25567` 连上后无回显（25567 已在监听，转发到房主 25565 后无人应答），这是预期现象。
6. 根治办法：把 GUI 的默认节点列表换成 CLI 现在用的四个社区节点（本轮为避开并行线的未提交改动，未动 GUI 代码）。

## 8 回滚方案

删除 `PCL.Core/Link/` 与 `PCL.Cli/Commands/LinkCommand.cs`，回退 `Program.cs`、`CliExitCode.cs`；EasyTier 二进制与状态文件都在 `--workdir` 下，随目录删除即可。GUI 侧零改动，回滚不影响任何现有功能。
