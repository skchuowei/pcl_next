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

### 5.2 云端 Linux（无 GUI）

```bash
export PATH=$HOME/.dotnet:$PATH
dotnet build PCL.Cli/PCL.Cli.csproj -c Release -r linux-x64
# 房主：link create + link echo；本地：link join + link probe（--peer 用社区节点）
```

公共节点改用社区监控页（info.qtet.cn/uptime/easytier）在册且双向可达的四个：

```
tcp://225284.xyz:11010
tcp://easytier.weiai.org.cn:11010
tcp://183.230.36.171:11010
tcp://et.sumnight.top:11010
```

（Windows 本地与云端 Linux 对这四个节点均实测 TCP OPEN。）

## 6 踩坑记录

1. **内置公共节点已死**：`public.easytier.top` / `public2.easytier.cn` 已 NXDOMAIN，日志里全是 `connect timeout after 1.9s`。换社区节点后 20 s 内即连上。
2. **公共节点会污染「已加入」判断**：见 §4.5，必须按 hostname 过滤基础设施 peer。
3. **只给端口号会让 EasyTier 多占端口**：`--listeners` 必须显式写 `tcp://` 与 `udp://` 两条。
4. **Linux 解压后可执行位会丢**：`ZipFile.ExtractToDirectory` 不保留 mode，必须对 `easytier-core` / `easytier-cli` 调 `File.SetUnixFileMode`。
5. **父进程退出会带走子进程的输出管道**：不能用 `RedirectStandardOutput` 直接起核心，必须走启动脚本重定向到日志文件（见 §4.3）。
6. **沙箱 DNS 不可用作网络判据**：`getent` 无结果不等于节点不可达，要用 DoH 复核（本次 DoH 直接给出 NXDOMAIN 才是真死）。

## 7 回滚方案

删除 `PCL.Core/Link/` 与 `PCL.Cli/Commands/LinkCommand.cs`，回退 `Program.cs`、`CliExitCode.cs`；EasyTier 二进制与状态文件都在 `--workdir` 下，随目录删除即可。GUI 侧零改动，回滚不影响任何现有功能。
