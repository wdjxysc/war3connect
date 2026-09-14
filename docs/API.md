# API 0.4（protocolVersion 3）

所有 `/api` 接口（注册、登录除外）及 `/relay` 接口需 `Authorization: Bearer <token>`。令牌不放在查询字符串中。错误响应格式为 `{ "error": "说明" }`；限流可返回空体 429。JSON 使用 camelCase。

| 方法 | 路径 | 请求/返回 |
| --- | --- | --- |
| GET | `/health` | `{status, version, protocolVersion}`，无鉴权 |
| POST | `/api/register` | `{username,password}` → `{token,userId,username}` |
| POST | `/api/login` | 同上；使旧会话失效并退出旧房间 |
| POST | `/api/logout` | 退出房间并撤销会话 |
| GET | `/api/rooms` | 房间列表，不包含聊天和游戏公告 |
| POST | `/api/rooms` | `{name,password,gameVersion,capacity,gameId}` → 房间 |
| POST | `/api/rooms/{id}/join` | `{password,gameVersion,gameId}` → 房间 |
| GET | `/api/rooms/{id}` | 成员私有房间状态，同时更新该成员心跳 |
| POST | `/api/rooms/{id}/leave` | 退出；房主退出则关闭房间 |
| POST | `/api/rooms/{id}/chat` | `{text}`，最多 500 字，保留最近 100 条 |
| POST | `/api/rooms/{id}/game` | `{packet}`，base64 W3GS 公告，仅房主 |
| POST | `/api/rooms/{id}/tunnels` | 玩家创建连接 → `{id}` |
| GET | `/api/rooms/{id}/tunnels` | 房主待接入连接列表 |
| GET/Upgrade | `/relay/{id}` | WebSocket，每端只允许绑定一次，仅房主与对应玩家可接入 |
| GET | `/api/relay-status` | 已登录用户可查看 `{activeTunnels,forwardedBytes}` |

`RoomView` 字段与共享项目 `Contracts.cs` 一致。`game` 可为 null，代表没有近期有效建图公告，不能用它单独判断是否正在比赛。`members` 是平台成员，不是游戏槽位。

注册/登录按来源 IP 每分钟限制 20 次，其他受保护接口按账号每分钟限制 360 次。每次 HTTP 请求体最多 16 KiB。客户端每 2 秒刷新当前房间，房主约每 350 ms 查询待接入连接，大厅每 5 秒刷新。

创建、加入房间和 `RoomView` 已移除 `mapName` / `mapSha256`；地图路径仅作为游戏公告 `GameView.mapPath` 的展示信息，不读本地地图、不用于准入校验。原生 War3 公告中的地图校验字段原样保留；中继不解释地图下载或校验报文。

`/health` 返回 `protocolVersion: 3`，客户端在注册/登录前检查兼容性，连接旧服务端会明确提示同步升级，且不会提交账号操作。缺少 protocolVersion 的旧响应按版本 1 处理。0.4.0 需要同时更新服务端和客户端。

每条中继每方向每 1 秒窗口最多转发 2 MiB，超过窗口额度时等待并对 TCP/WebSocket 施加背压，不因正常大文件传输直接关闭连接。连接取消时同时取消限速等待。


gameId 可为 war3-tft（省略时的默认值）或 starcraft-bw，RoomView 同样返回 gameId。大厅返回所有游戏，客户端按 gameId 筛选；服务端拒绝跨游戏加入。星际仅接受 1.16.1.x 完整版本，房间最多 8 人，目前仅支持准备房间、成员和聊天，游戏公告与 tunnels 接口返回 409。详见 [星际接入状态](STARCRAFT.md)。
