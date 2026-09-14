# API 0.1

所有 `/api` 接口（注册、登录除外）及 `/relay` 接口需 `Authorization: Bearer <token>`。令牌不放在查询字符串中。错误响应格式为 `{ "error": "说明" }`；限流可返回空体 429。JSON 使用 camelCase。

| 方法 | 路径 | 请求/返回 |
| --- | --- | --- |
| GET | `/health` | `{status, version}`，无鉴权 |
| POST | `/api/register` | `{username,password}` → `{token,userId,username}` |
| POST | `/api/login` | 同上；使旧会话失效并退出旧房间 |
| POST | `/api/logout` | 退出房间并撤销会话 |
| GET | `/api/rooms` | 房间列表，不包含聊天和游戏公告 |
| POST | `/api/rooms` | `{name,password,gameVersion,mapName,mapSha256,capacity}` → 房间 |
| POST | `/api/rooms/{id}/join` | `{password,gameVersion,mapSha256}` → 房间 |
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

房间地图 SHA-256 是平台预检查；原生 War3 协议中的地图校验字段原样保留。服务端无法仅凭文件名或客户端声明证明真实游戏版本/地图，也不据此实现反作弊或可信战绩。
