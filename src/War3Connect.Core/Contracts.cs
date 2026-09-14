namespace War3Connect.Core;

public sealed record Credentials(string Username, string Password);
public sealed record LoginResult(string Token, string UserId, string Username);
public sealed record CreateRoom(string Name, string Password, string GameVersion, int Capacity = 8);
public sealed record JoinRoom(string Password, string GameVersion);
public sealed record PublishGame(string Packet);
public sealed record SendChat(string Text);
public sealed record ErrorResponse(string Error);
public sealed record MemberView(string Id, string Name, bool IsHost);
public sealed record ChatView(long Id, string Name, string Text, DateTimeOffset Time);
public sealed record GameView(string Packet, string Name, string MapPath, DateTimeOffset UpdatedAt);
public sealed record RoomView(string Id, string Name, string HostId, string HostName, string GameVersion,
    int Capacity, bool HasPassword, MemberView[] Members, ChatView[] Messages, GameView? Game);
public sealed record TunnelView(string Id);
public sealed record HealthView(string Status, string Version, int ProtocolVersion = 1);
public sealed record RelayStats(int ActiveTunnels, long ForwardedBytes);

public static class Versions
{
    public const int ProtocolVersion = 2;
    public const string Release = "0.3.1";
    public static bool IsSupported(string? version) => Version.TryParse(version, out var v)
        && v.Major == 1 && v.Minor == 27 && v.Build >= 0 && v.Revision >= 0;
}
