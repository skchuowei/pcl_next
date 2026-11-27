# Next 协议接入指南

> 面向：想让自己的启动器 / 工具接入 pcl-next 联机大厅、并与 pcl-next 双向互通的开发者。
> 前置阅读：《Next协议介绍.md》（为什么这么设计）、《Next协议规范.md》（字节与字段）、**《联机大厅使用协议.md》（许可与义务，务必先看第 4 节）**。

---

## 0. 先决条件

| 依赖 | 说明 |
|---|---|
| EasyTier | 与 pcl-next 内置版本（2.6.4）协议兼容的版本；参数见规范 §2 |
| 网络 | 房主与加入者都要能访问 EasyTier 公共/自建节点 |
| 语言/运行时 | 不限；只要能开 TCP、算 HMAC-SHA256、解析 JSON |
| 你的标识 | 准备一个稳定的 `machine_id`（落盘，不要每次启动重新生成）和一个 `vendor` 字符串 |

**不需要**：pcl-next 的账号、SDK、许可密钥、任何中心服务。

---

## 1. 最小可行版本：只做「加入端」

目标：能用 pcl-next 的邀请码进 pcl-next 的房主房间，并进服。

### 步骤

1. **解析邀请码**（新码 `网名5-密钥5-前缀8`，可带 `-S`；旧码 `网名9-密钥5-端口4hex` 也要能认）。
   - 新码 → `network-name = "pclnext-" + 网名`，`network-secret = 密钥 + 前缀`，`secure-mode = 有 -S`；
   - 旧码 → `network-secret = 密钥`，端口直接用码里的值。
2. **拉起 EasyTier** 加入网络：`-d`、`--no-tun`、`--network-name`、`--network-secret`、`--machine-id`、`--private-mode true`、`--encryption-algorithm aes-gcm`、`--listeners tcp/udp`、`--rpc-portal 127.0.0.1:<rpc>`；邀请码带 `-S` 时再加 `--secure-mode true`。
3. **等 peer 出现**，在 EasyTier peer 列表里找主机名前缀 `pclnext-mc-server-`，后缀即控制面端口；房主 IP 从 peer 条目取（固定为 `10.114.114.114`）。
4. **建本地转发**：`--port-forward tcp://127.0.0.1:<localScaffold>/<房主IP>:<scaffoldPort>`，然后连 `127.0.0.1:<localScaffold>`。
5. **握手与取端口**（TCP 一帧一问）：
   1. 立即发 `c:player_ping`（JSON：`machine_id` 必填，`name`、`vendor`、`kind:"GUEST"`）；
   2. `c:server_port` → 2 字节大端端口；
   3. `c:gate` → 有返回就按 §9 起网关隧道，回 `32` 就用普通 `--port-forward` 转发 MC 端口；
   4. 之后每 5 秒心跳：`c:player_ping` → `c:member_state` → `c:ping` → `c:player_profiles_list`。
6. **游戏侧**：把本地端口交给用户（`127.0.0.1:<LocalJoinPort>`），或按你的启动器方式直接写进服务器地址。

### 参考伪代码（帧读写）

```python
import struct, json, socket

def send_req(sock, type_name, body=b""):
    t = type_name.encode("ascii")
    sock.sendall(bytes([len(t)]) + t + struct.pack(">I", len(body)) + body)
    status = sock.recv(1)
    if not status: raise ConnectionError("房主关闭了连接")
    n = struct.unpack(">I", read_exact(sock, 4))[0]
    return status[0], read_exact(sock, n)

def read_exact(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk: raise ConnectionError("连接中断")
        buf += chunk
    return buf

# 登记 + 取端口
st, body = send_req(s, "c:player_ping", json.dumps({
    "name": nick, "machine_id": mid, "vendor": "MyLauncher 1.0 (next v1)", "kind": "GUEST"
}, ensure_ascii=False).encode("utf-8"))
mc_port = struct.unpack(">H", send_req(s, "c:server_port")[1])[0]
```

### 参考伪代码（网关 proof）

```python
import hashlib, hmac

key   = hashlib.sha256(("pclnext-gate|" + network_secret).encode("utf-8")).digest()
proof = hmac.new(key, (nonce + "|" + ticket).encode("utf-8"), hashlib.sha256).hexdigest()
```

---

## 2. 完整版本：再做「房主端」

目标：你的启动器能建房，pcl-next 的加入端能进你的房间。

1. 生成邀请码：网名 5 位、密钥 5 位（31 字符表）、前缀 8 位十六进制，全部用密码学安全随机；想开安全模式就追加 `-S`。
2. 起 EasyTier：`-i 10.114.114.114`、`--hostname pclnext-mc-server-<scaffoldPort>`、白名单放行「协议端口 tcp+udp」「25566/tcp」「网关端口」；**不要**放行 MC 端口。
3. 起控制面服务（帧格式见规范 §5），至少实现：`c:ping`、`c:protocols`、`c:server_port`、`c:player_ping`、`c:player_profiles_list`；建议补 `c:gate`、`c:game_info`。
4. 起网关（强烈建议）：实现 `PCLGATE1` 握手、ticket 发放/校验/撤销；网关端口放行到 overlay。
5. 游戏开好局域网后，把真实 MC 端口发布到 `c:server_port` / `c:game_info`；端口变化只更新发布，不用重启 EasyTier。
6. 踢人：控制面对该 `machine_id` 的请求回 `255` + `KICKED …`，同时撤销 ticket 并断开在途隧道。

---

## 3. 十个最容易踩的坑

| # | 坑 | 正确做法 |
|---|---|---|
| 1 | 邀请码里塞端口 | 新格式不带端口，端口由房主实时发布 |
| 2 | 加完网络直接连 MC 端口 | 先找到房主主机名 → 连控制面 → 问端口 |
| 3 | 自定义房主主机名 | `pclnext-mc-server-<端口>` 是互通标识，不能改 |
| 4 | 白名单只放一个方向 | 协议端口要 tcp **和** udp 都放行 |
| 5 | 以为 `c:protocols` 是「能力全集」 | 它只列基础五类型；扩展要主动试，收到 `32` 再降级 |
| 6 | 未知类型回 `255` 或直接断连 | **必须回 `32`**，否则新扩展会打断旧实现 |
| 7 | 帧长度用小端 / 文本长度算成字符数 | 长度字段大端；正文长度按**字节** |
| 8 | 心跳只发 `c:ping` | 必须用 `c:player_ping` 维持登记，否则成员列表里没有你 |
| 9 | 被踢后继续重连 | `255` + `KICKED` 是终态：停止心跳、退出房间 |
| 10 | 网关握手不带 proof 或复用 ticket | 每次连接重算 proof；ticket 12 小时过期，踢人即撤销 |

---

## 4. 自测清单

先在「自己房主 + 自己加入端」跑通，再跨启动器验收。

| # | 场景 | 期望 |
|---|---|---|
| 1 | 两个实例互连（同一实现） | 加入者拿到端口并进服 |
| 2 | 邀请码带 `-S` | 双方都开 `--secure-mode` 才连上；一方没开则连不上（这是正确行为） |
| 3 | 房主只实现基础五类型，加入端请求 `c:gate` | 房主回 `32`，加入端回退普通转发，仍能进服 |
| 4 | 缺失的扩展类型 | 状态码正好是 `32`，连接仍然可用 |
| 5 | 心跳停 20 秒 | 房主成员列表里该项状态变旧/被清理 |
| 6 | 房主踢人 | 加入端收到 `255` + `KICKED`，退出房间 |
| 7 | 直接连 overlay 的 MC 端口（绕过协议） | 被 EasyTier 白名单挡住（房主启用网关时） |
| 8 | 网关误导 proof / 过期 ticket | 收到 `DENY bad_proof` / `DENY unknown_ticket` |
| 9 | 房主换 MC 端口（重启游戏） | 加入端重新问端口后仍能进服，无需重建房间 |
| 10 | 帧长度 129 字节的类型名 / 70 KB 正文 | 房主断开并记日志（不崩溃） |

---

## 5. 跨启动器验收（互通定义）

以下四条都成立，才算「与 next 互通」：

1. 你的**加入端**能进 pcl-next 房主的房间（含启用网关的房间）；
2. pcl-next 的**加入端**能进你房主的房间（你的房主端完整实现控制面）；
3. 双方成员列表能互相看到（至少 `name` + `vendor` 正确）；
4. 被踢、房间关闭、端口变化这三类边界行为不出现「假在线」或「卡 99%」。

建议在 PR / 发布说明里贴出：协议版本（v1）、EasyTier 版本、上述 4 条的自测结果。

---

## 6. 常见问题

**Q：必须用 EasyTier 吗？**
A：承载层约定就是 EasyTier 的参数组合（网络名、密钥、主机名、白名单、转发）。换别的组网工具，pcl-next 的加入端找不到你的房主，就不算互通。

**Q：可以只做控制面、不做网关吗？**
A：可以。房主不启用网关时，加入端走普通端口转发，功能等价于旧版。但你的房间会少一道「只有走过协议才能进」的门禁，建议至少在自己的房间里提示用户。

**Q：能用 `pclnext-` 网络前缀建一个「自有协议」房间吗？**
A：不行。用这个前缀就意味着这个房间是 next 协议房间，pcl-next 加入端会按 next 协议去问端口和领 ticket（《联机大厅使用协议.md》§4.3）。

**Q：商业软件可以用吗？**
A：可以，免费、无需授权。但必须满足四条义务（不冒充官方 / 不绕过准入 / 不冒用标识 / 不采集与再分发），并在关于页声明「第三方实现，与 pcl-next 无隶属关系」。

**Q：我改了协议字段，还能叫「兼容 next」吗？**
A：不能。修改既有类型语义属于破坏性变更；新增私有类型请用非 `c:` 前缀，并在文档里注明。

---

## 7. 源码与求助

| 想看什么 | 位置 |
|---|---|
| 控制面（帧、类型、成员） | `pcl-next/Modules/Link/ModLinkScaffold.cs` |
| 邀请码与大厅参数 | `pcl-next/Modules/Link/ModLinkCodec.cs`、`ModLinkInterop.cs` |
| EasyTier 参数与主机名发现 | `pcl-next/Modules/Link/ModLinkEasyTier.cs`、`ModLinkSession.cs` |
| 准入网关（无 GUI 共用实现） | `PCL.Core/Link/PclLinkGate.cs` |
| 联机包直传 | `pcl-next/Modules/Link/ModLinkTransfer.cs`、`ModLinkSync.cs` |

提 issue 请标注 `link-protocol`，并附：你的实现名称、协议版本、EasyTier 版本、失败帧的十六进制片段（请先脱敏 `network-secret`、ticket、`proof`）。
