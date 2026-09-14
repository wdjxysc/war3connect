using System.Security.Cryptography;
using System.Text;
using War3Connect.Core;

namespace War3Connect.Server;

public sealed class Platform
{
    private sealed record Session(Account User, DateTimeOffset Expires);
    private sealed class Room
    {
        public required string Id, Name, PasswordHash, PasswordSalt, Version, MapName, MapHash;
        public required Account Host;
        public required int Capacity;
        public Dictionary<string, (Account User, DateTimeOffset Seen)> Members { get; } = [];
        public List<ChatView> Messages { get; } = [];
        public GameView? Game;
        public long MessageId;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, Room> _rooms = [];
    private readonly Relay _relay;
    private readonly TimeProvider _clock;
    private DateTimeOffset Now => _clock.GetUtcNow();
    public Platform(Relay relay, TimeProvider clock) { _relay = relay; _clock = clock; }

    public LoginResult Login(Account account)
    {
        lock (_gate)
        {
            // One session per account prevents conflicting room agents.
            foreach (var key in _sessions.Where(s => s.Value.User.Id == account.Id).Select(s => s.Key).ToArray()) _sessions.Remove(key);
            LeaveAll(account.Id);
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessions[token] = new(account, Now.AddHours(12));
            return new(token, account.Id, account.Name);
        }
    }
    public Account Authorize(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        lock (_gate)
        {
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !_sessions.TryGetValue(header[7..], out var s) || s.Expires <= Now)
                throw new ApiException(401, "登录已过期，请重新登录。");
            return s.User;
        }
    }
    public void Logout(Account user)
    {
        lock (_gate)
        {
            LeaveAll(user.Id);
            foreach (var token in _sessions.Where(s => s.Value.User.Id == user.Id).Select(s => s.Key).ToArray()) _sessions.Remove(token);
        }
    }
    public RoomView[] List()
    {
        lock (_gate) return _rooms.Values.Select(r => View(r, false)).ToArray();
    }
    public RoomView Create(Account user, CreateRoom request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 40 || request.Password is null || request.Password.Length > 64
            || !Versions.IsSupported(request.GameVersion) || !ValidHash(request.MapSha256)
            || string.IsNullOrWhiteSpace(request.MapName) || request.MapName.Length > 200 || request.Capacity is < 2 or > 12)
            throw new ApiException(400, "房间参数无效：请选择 1.27 游戏和地图，人数为 2～12。");
        lock (_gate)
        {
            EnsureNotInRoom(user.Id);
            if (_rooms.Count >= 200) throw new ApiException(503, "内测房间数量已满。");
            string salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var room = new Room { Id = Guid.NewGuid().ToString("N"), Name = request.Name.Trim(), Host = user,
                PasswordSalt = salt, PasswordHash = request.Password.Length == 0 ? "" : PasswordHash(request.Password, salt),
                Version = request.GameVersion, MapName = request.MapName, MapHash = request.MapSha256.ToUpperInvariant(), Capacity = request.Capacity };
            room.Members[user.Id] = (user, Now);
            _rooms.Add(room.Id, room);
            return View(room, true);
        }
    }
    public RoomView Join(Account user, string id, JoinRoom request)
    {
        lock (_gate)
        {
            var r = Find(id);
            if (request.Password is null || request.Password.Length > 64 || (r.PasswordHash.Length > 0
                && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(r.PasswordHash), Convert.FromHexString(PasswordHash(request.Password, r.PasswordSalt)))))
                throw new ApiException(403, "房间密码错误。");
            if (r.Version != request.GameVersion) throw new ApiException(409, $"游戏版本不一致，房间要求 {r.Version}。");
            if (!string.Equals(r.MapHash, request.MapSha256, StringComparison.OrdinalIgnoreCase)) throw new ApiException(409, "地图文件不一致，请选择与房主完全相同的地图。");
            if (!r.Members.ContainsKey(user.Id))
            {
                EnsureNotInRoom(user.Id);
                if (r.Members.Count >= r.Capacity) throw new ApiException(409, "房间已满。");
            }
            r.Members[user.Id] = (user, Now);
            return View(r, true);
        }
    }
    public RoomView Get(Account user, string id)
    {
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            r.Members[user.Id] = (user, Now);
            return View(r, true);
        }
    }
    public void Leave(Account user, string id)
    {
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            RemoveMember(r, user.Id);
        }
    }
    public void Chat(Account user, string id, SendChat request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500) throw new ApiException(400, "消息需为 1～500 字。");
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            r.Messages.Add(new(++r.MessageId, user.Name, request.Text.Trim(), Now));
            if (r.Messages.Count > 100) r.Messages.RemoveAt(0);
        }
    }
    public void Publish(Account user, string id, PublishGame request)
    {
        if (request.Packet is null || request.Packet.Length > 6000) throw new ApiException(400, "游戏公告过大。");
        LanGame game;
        try { game = LanProtocol.Parse(Convert.FromBase64String(request.Packet)); }
        catch (Exception e) when (e is FormatException or InvalidDataException) { throw new ApiException(400, e.Message); }
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            if (r.Host.Id != user.Id) throw new ApiException(403, "只有房主可发布游戏。");
            if (!game.MapPath.Replace('\\', '/').Split('/').Last().Equals(r.MapName, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(409, "游戏内地图名称与房间地图不一致。");
            r.Game = new(request.Packet, game.Name, game.MapPath, Now);
        }
    }
    public TunnelView CreateTunnel(Account user, string id)
    {
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            if (r.Host.Id == user.Id) throw new ApiException(400, "房主无需加入自己的代理。");
            if (FreshGame(r) == null) throw new ApiException(409, "房主尚未建图或游戏已开始。");
            return _relay.Create(id, r.Host.Id, user.Id);
        }
    }
    public TunnelView[] Pending(Account user, string id)
    {
        lock (_gate)
        {
            var r = MemberRoom(user.Id, id);
            if (r.Host.Id != user.Id) throw new ApiException(403, "只有房主可接受连接。");
            return _relay.Pending(id);
        }
    }
    public void Sweep()
    {
        lock (_gate)
        {
            foreach (var r in _rooms.Values.ToArray())
                foreach (var m in r.Members.Values.Where(m => Now - m.Seen > TimeSpan.FromSeconds(45)).ToArray())
                    RemoveMember(r, m.User.Id);
            foreach (var key in _sessions.Where(s => s.Value.Expires <= Now).Select(s => s.Key).ToArray())
            {
                LeaveAll(_sessions[key].User.Id);
                _sessions.Remove(key);
            }
        }
        _relay.Sweep();
    }
    private void EnsureNotInRoom(string user)
    {
        if (_rooms.Values.Any(r => r.Members.ContainsKey(user))) throw new ApiException(409, "请先退出当前房间。");
    }
    private void LeaveAll(string user)
    {
        foreach (var r in _rooms.Values.Where(r => r.Members.ContainsKey(user)).ToArray()) RemoveMember(r, user);
    }
    private void RemoveMember(Room r, string user)
    {
        r.Members.Remove(user);
        if (r.Host.Id == user) { _rooms.Remove(r.Id); _relay.CancelRoom(r.Id); }
        else _relay.CancelUser(r.Id, user);
    }
    private Room Find(string id) => _rooms.TryGetValue(id, out var r) ? r : throw new ApiException(404, "房间已关闭。");
    private Room MemberRoom(string user, string id)
    {
        var r = Find(id);
        if (!r.Members.ContainsKey(user)) throw new ApiException(403, "你不在该房间中。");
        return r;
    }
    private GameView? FreshGame(Room r) => r.Game != null && Now - r.Game.UpdatedAt < TimeSpan.FromSeconds(8) ? r.Game : null;
    private RoomView View(Room r, bool privateView) => new(r.Id, r.Name, r.Host.Id, r.Host.Name, r.Version, r.MapName,
        r.MapHash, r.Capacity, r.PasswordHash.Length > 0, r.Members.Values.Select(m => new MemberView(m.User.Id, m.User.Name, m.User.Id == r.Host.Id)).ToArray(),
        privateView ? r.Messages.ToArray() : [], privateView ? FreshGame(r) : null);
    private static bool ValidHash(string? h) => h is { Length: 64 } && h.All(Uri.IsHexDigit);
    private static string PasswordHash(string text, string salt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + text)));
}
