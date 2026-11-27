# Next 协议规范 v1

> 状态：现行（2026-11-27）。本文描述 pcl-next 联机大厅的线格式与行为，是第三方复刻与互通的**唯一判定依据**。
> 许可：允许阅读、实现、复刻并与 pcl-next 互通，义务见《联机大厅使用协议.md》第 3、4 节。
> 实现参考：`pcl-next/Modules/Link/ModLinkScaffold.cs`（控制面）、`PCL.Core/Link/PclLinkGate.cs`（准入面）。

约定用语：**必须**（MUST）、**应当**（SHOULD）、**可以**（MAY）。

---

## 1. 角色与端口

| 角色 | overlay 地址 | 监听 | 说明 |
|---|---|---|---|
| 房主 | `10.114.114.114`（固定） | `127.0.0.1:<scaffoldPort>` 控制面 | 端口随机空闲，通过主机名对外公布 |
| 房主 | 同上 | `127.0.0.1:<gatePort>` 准入面 | 仅在启用网关时监听 |
| 房主 | 同上 | `0.0.0.0:25566` 联机包直传 | 可选，只读 |
| 房主 | 同上 | 本机 MC 端口 | **不**进 overlay 白名单（启用网关时） |
| 加入者 | 由 EasyTier 分配 | `127.0.0.1:<local*>` 本地转发 | 由 EasyTier `--port-forward` 建立 |

所有 overlay 端口都必须显式写进 EasyTier 的 `--tcp-whitelist` / `--udp-whitelist`，否则流量被丢。

---

## 2. 承载层参数（EasyTier）

双方启动方式必须一致到「对等端能握手成功」的程度：

| 参数 | 房主 | 加入者 | 说明 |
|---|---|---|---|
| `--no-tun` | 是 | 是 | 只做端口转发，不建虚拟网卡 |
| `--network-name` | `pclnext-` + 网名（5 字符） | 同 | 网名来自邀请码 |
| `--network-secret` | 密钥 5 + 前缀 8 = 13 字符 | 同 | 邀请码第 2、3 段拼接 |
| `--machine-id` | 本机稳定标识 | 同 | 与协议里的 `machine_id` 同源 |
| `--relay-network-whitelist` | 本网络名 | 同 | 只允许本网络中继 |
| `--default-protocol` | `tcp` 或 `udp` | 同 | 双方偏好不同也可连，但建议一致 |
| `--listeners` | `tcp://0.0.0.0:<p>`、`udp://0.0.0.0:<p>` | 同 | 端口随机，避免 +1/+2 冲突 |
| `--rpc-portal` | `127.0.0.1:<rpc>` | 同 | 本地控制端口 |
| `--private-mode` | `true` | `true` | 不加入公共发现 |
| `--encryption-algorithm` | `aes-gcm` | 同 | 不一致会握手失败 |
| `--secure-mode` | 邀请码带 `-S` 时 `true` | 同 | **必须双方一致** |
| `-i` / `-d` | `-i 10.114.114.114` | `-d` | 房主指定覆盖地址，加入者用默认 |
| `--hostname` | `pclnext-mc-server-<scaffoldPort>` | 自定义 | 房主前缀不可改（见 §3） |
| 白名单 | 协议端口（tcp+udp）、25566/tcp、网关端口；**不含** MC 端口 | `0`（全关） | 加入者不需要被连 |
| 端口转发 | — | 按需 `--port-forward tcp://127.0.0.1:<local>/<房主IP>:<目标端口>` | 控制面、网关、MC 各一条 |

---

## 3. 房主发现

1. 加入者加入网络后，遍历 EasyTier peer 列表，找到主机名前缀为 `pclnext-mc-server-` 的节点；
2. 主机名后缀解析为正整数，即控制面端口 `<scaffoldPort>`；
3. 房主 overlay IP 从 peer 条目取（通常就是 `10.114.114.114`）；
4. 加入者建立本地转发 `127.0.0.1:<localScaffold>` → `<房主IP>:<scaffoldPort>`，然后按 §5 连接。

**兼容约定**：PCL2-CE 的房主前缀是 `scaffolding-mc-server-`，结构与解析方式相同；实现应当按「目标大厅类型」选择前缀，不要同时匹配两个前缀（避免认错房主）。

---

## 4. 连接模型

- 传输：TCP，房主 `NoDelay` 打开。
- **长连接、单通道、请求-应答严格串行**：客户端发一帧、等一帧应答，不得流水线并发；同一时刻一条 TCP 上只有一个未完成请求。
- 连接建立后，加入者**必须**立即发送一次 `c:player_ping` 完成登记；未登记的连接不能调用 `c:gate`。
- 房主可以在一帧应答后关闭连接（例如状态 255），客户端必须处理。

---

## 5. 帧格式

### 5.1 请求（客户端 → 房主）

```
偏移  长度  内容
0     1     类型长度 N（1..128）
1     N     类型名（ASCII，如 "c:server_port"）
1+N   4     正文长度 L（大端无符号 32 位）
5+N   L     正文（可为 0 字节）
```

### 5.2 应答（房主 → 客户端）

```
偏移  长度  内容
0     1     状态码（见 §6）
1     4     正文长度 L（大端无符号 32 位）
5     L     正文（可为 0 字节）
```

约束：

- 类型长度 `N` 必须 `1..128`，正文长度 `L` 必须 `0..65536`；越界即视为畸形帧，房主应当断开该连接并记日志。
- 文本类正文一律 UTF-8；长度字段**必须**是大端（big-endian）。
- 每个请求只对应一个应答；应答的正文长度与状态码无关（错误时正文可携带错误文本）。

---

## 6. 状态码

| 值 | 名称 | 语义 | 客户端行为 |
|---|---|---|---|
| `0` | OK | 成功，正文按类型定义解析 | 正常处理 |
| `32` | BAD_REQUEST | 参数错误 / **不认识该类型** / 当前不可用（如房主还没发布端口、未启用网关） | 若是扩展类型：视为「对方不支持」，静默降级；基础类型：按错误处理 |
| `255` | ERROR | 业务错误，正文是 UTF-8 错误文本；连接通常被关闭 | 展示文本；正文以 `KICKED` 开头表示被房主移出房间，客户端**必须**退出房间 |
| 其它 | — | 保留 | 按错误处理，记录状态码 |

房主在房间已解散时，对任何类型都回 `255` + 说明文本，且**不主动断开**（让客户端下次心跳读到同一句，避免呈现为「连接错误」）。

---

## 7. 消息类型

### 7.1 基础类型（与 PCL2-CE Scaffolding 对齐，`c:protocols` 会列出这五个）

| 类型 | 请求正文 | 成功应答正文 |
|---|---|---|
| `c:ping` | 任意（通常为空） | 原样回显请求正文；客户端据此测往返延迟 |
| `c:protocols` | 空 | ASCII 类型名列表，以 `\0` 分隔；**只列基础五类型**，不列扩展 |
| `c:server_port` | 空 | 2 字节大端端口；未发布时回 `32` |
| `c:player_ping` | UTF-8 JSON 成员档案（见 7.2） | 空；被踢时回 `255` + `KICKED …` |
| `c:player_profiles_list` | 空 | UTF-8 JSON 数组（见 7.3） |

### 7.2 `c:player_ping` 请求体

```json
{"name":"小明","machine_id":"<稳定标识>","vendor":"<客户端标识>","kind":"GUEST"}
```

| 字段 | 必须 | 说明 |
|---|---|---|
| `machine_id` | 是 | 非空；同一台机器跨会话稳定；房主用它去重、踢人、发 ticket |
| `name` | 否 | 展示名；缺失时房主回退用 `machine_id` |
| `vendor` | 否 | 客户端标识，建议写成 `<程序名> <版本>` |
| `kind` | 否 | `GUEST` / `HOST`；加入者传 `GUEST` 即可 |

首次发送即完成登记，之后每 5 秒重发维持在线（房主按最后在线时间清理超时成员）。

### 7.3 `c:player_profiles_list` 应答体

JSON 数组，元素是成员档案对象：

```json
[{"name":"小明","machine_id":"…","vendor":"…","kind":"GUEST",
  "game_running":true,"instance":"1.20.1-Forge","version":"1.20.1",
  "game_port":25565,"has_instance":true,"instance_name":"…",
  "pack_state":"done","pack_percent":100,"pack_size":123456,"state_at":"2026-11-27T20:00:00+08:00"}]
```

- `kind` 为 `HOST` 的元素带房主自己的状态；`GUEST` 元素的状态来自该成员最近一次 `c:member_state` 上报，超过 20 秒未上报即视为不新鲜。
- 状态字段全部可选；不认识时忽略。

### 7.4 扩展类型（pcl-next 私有，全部可选）

| 类型 | 请求正文 | 成功应答正文 | 未实现时 |
|---|---|---|---|
| `c:gate` | `machine_id`（纯文本，UTF-8） | `{"port":<网关端口>,"ticket":"<32 hex>","expiresAt":"<ISO 8601>"}` | `32` |
| `c:game_info` | 空 | `{"port":<MC 端口>,"running":<bool>,"instance":"…","version":"…"}` | `32` |
| `c:package` | 空 | `{"available":<bool>,"name":"…","size":<long>,"sha256":"…","port":<int>}` | `32` |
| `c:join_report` | `{"machine_id","name","state","reason","category","suggestion","evidence"}` | 空 | `32` |
| `c:member_state` | 见 7.5 | 空 | `32` |

`c:gate` 只在「房主已登记该 `machine_id` 且未被踢、且启用了网关」时返回成功；否则一律 `32`。

### 7.5 `c:member_state` 请求体

```json
{"machine_id":"…","name":"…","game_running":true,"instance":"…","version":"1.20.1",
 "game_port":25565,"has_instance":true,"instance_name":"…",
 "pack_state":"syncing","pack_percent":42,"pack_size":123456,"state_at":"<ISO 8601>"}
```

房主按 `machine_id` 覆盖保存，并在成员列表中原样带出。加入者每 5 秒随心跳发送一次。

---

## 8. 心跳与成员管理

加入者每 **5 秒**执行一轮：

1. `c:player_ping`（重发档案，维持在线）；
2. `c:member_state`（上报本机状态，可省）；
3. `c:ping`（空正文，测到房主的往返延迟）；
4. `c:player_profiles_list`（拉成员列表）。

规则：

- 任何一步收到 `32` → 视为对方不支持该扩展，忽略并继续；
- 收到 `255` + `KICKED` → 客户端**必须**结束心跳、退出房间并提示；
- 其它 IO 异常 → 视为房主已关闭房间，停止心跳并把连接状态置为断开；
- 房主对超过阈值未心跳的成员应清理出列表。

---

## 9. 准入网关（Gate）

行协议：TCP，UTF-8 单行文本，以 `\n` 结尾（读取端应当容忍 `\r\n`），单行最长 256 字节。

### 9.1 握手

```
房主 →  PCLGATE1 <nonce>            nonce = 16 字节随机数的小写 hex（32 字符）
加入 →  <ticket> <proof>            ticket 为 32 字符小写 hex；proof 见 9.2
房主 →  OK                          成功，随后该 TCP 会话被双向转发到房主本机 MC 端口
房主 →  DENY <reason>               失败，立即断开
```

### 9.2 proof 计算

```
key   = SHA256( UTF8("pclnext-gate|" + networkSecret) )        // networkSecret 为 13 字符
proof = lower_hex( HMAC_SHA256(key, UTF8(nonce + "|" + ticket)) )
```

### 9.3 ticket 与限制

| 项 | 值 |
|---|---|
| 发放 | 只能经控制面 `c:gate` 发放，绑定 `machine_id`，随机 32 字符 hex |
| 有效期 | 12 小时；过期、被撤销、不存在都视为 `unknown_ticket` |
| 撤销 | 房主踢人时撤销该 `machine_id` 的全部 ticket，并断开在途会话 |
| 握手超时 | 5 秒（读/写都被限制），超时断开 |
| 并发握手 | 房主同时最多 2 个未完成握手 |
| 会话上限 | 房主最多 64 条；同一 `machine_id` 最多 2 条 |

### 9.4 拒绝原因（`DENY <reason>`）

| reason | 含义 |
|---|---|
| `bad_format` | 行格式不是「两个以空格分隔的字段」 |
| `unknown_ticket` | ticket 不存在 / 过期 / 已被撤销 |
| `bad_proof` | HMAC 校验失败 |
| `too_many_sessions` | 该 `machine_id` 会话数已达上限 |

失败计数超过阈值的来源，房主可以在 60 秒内拒绝新的握手（实现可选，但建议记录审计日志：时间、`machine_id`、原因）。

### 9.5 加入者侧

加入者在本机监听 `127.0.0.1:<LocalJoinPort>`；Minecraft 每来一条 TCP 连接，就向 `<房主IP>:<gatePort>` 建一条隧道并完成 9.1 的握手，成功后双向转发。**不使用** `--port-forward` 直接把 MC 端口暴露到 overlay。

---

## 10. 联机包直传（可选数据面）

- 房主在 `0.0.0.0:25566` 提供一个**只读**最小 HTTP 服务：只支持 `GET` / `HEAD`，支持 `Range`，仅提供清单内的文件；其它方法回 `405`，未登记路径回 `404`。
- 元信息通过控制面 `c:package` 获取：`{available,name,size,sha256,port}`；成员下载后必须用 `sha256` 校验。
- 该端口只放行给 overlay（`--tcp-whitelist 25566`），不做逐文件授权，也不提供写入。

---

## 11. 兼容规则（复刻务必遵守）

1. **未知类型必须回 `32`**，不得回 `255`、不得断开连接（否则旧实现会被新扩展打断）。
2. `c:protocols` 只列基础五类型；实现方**不得**因为列表里没有 `c:gate` 就断言对方一定没网关 —— 应当直接尝试 `c:gate`，收到 `32` 再降级。
3. 基础类型的语义与正文格式**不可改动**；要扩展只能新增类型。
4. 私有扩展**必须**使用非 `c:` 的前缀（例如 `z:vendor_feature`）；`c:` 命名空间由 next 协议保留。
5. 加入端遇到「码内带端口」的旧格式房间时，可以不做控制面交互，直接按端口转发；但这样就失去成员列表与踢人能力。
6. 房主端若同时兼容 PCL2-CE，请注意：PCL2-CE 房主用 `scaffolding-mc-server-` 前缀、不带 `c:gate`，网关必须对这类房间关闭。

---

## 12. 安全与实现要求

1. `network-secret`、ticket、`proof` **不得**写入面向用户的日志或崩溃报告；日志脱敏。
2. 邀请码解析必须使用密码学安全随机生成新码（禁止可预测的伪随机）。
3. 握手与请求处理必须有超时；畸形帧必须断开并限速，防止单连接耗尽房主资源。
4. 不要因为「内网」假设而省略 proof 校验或跳过 `machine_id` 登记。
5. 第三方实现应当把 `vendor` 写成可辨识的自家标识（如 `XLq-Launcher 1.2 (next v1)`）。

---

## 13. 版本与演进

- 协议族名：`next`；本文为 **v1**。线格式不带全局版本号字段，能力靠 `c:protocols` 与类型探测确定。
- 新增类型不影响旧实现（对方回 `32`）；修改既有类型的语义属于破坏性变更，必须发布新版本文档并在此登记。
- 变更记录：

| 日期 | 变更 |
|---|---|
| 2026-11-27 | v1 首次成文：基础五类型 + `c:gate` / `c:game_info` / `c:package` / `c:join_report` / `c:member_state`；控制面帧格式、状态码、网关握手、25566 直传 |
