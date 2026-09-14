using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using War3Connect.Agent;
using War3Connect.Core;
using War3Connect.Server;
using War3Connect.Tests;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
async Task Reject(Func<Task> action, HttpStatusCode expected, string name)
{
    try { await action(); throw new Exception("Accepted forbidden request: " + name); }
    catch (PlatformException e) { Check(e.Status == expected, name); }
}
void RejectDirect(Action action, int code, string name)
{
    try { action(); throw new Exception("Accepted forbidden action: " + name); }
    catch (ApiException e) { Check(e.Status == code, name); }
}
async Task Eventually(Func<Task<bool>> predicate, string name, int seconds = 12)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        if (await predicate()) { Check(true, name); return; }
        await Task.Delay(150);
    }
    throw new Exception("Timed out: " + name);
}

Check(Convert.ToHexString(LanProtocol.Search()) == "F72F1000505833571B00000000000000", "1.27 TFT discovery golden bytes");
var packet = Fixtures.Announcement();
var game = LanProtocol.Parse(packet);
Check(game.Name == "Test Game" && game.MapPath == "Maps\\fixture.w3x" && game.HostCounter == 0x12345678 && game.EntryKey == 0x87654321 && game.Port == 6112, "parse complete LAN advertisement");
var redirected = LanProtocol.WithPort(game, 16112);
Check(redirected[..^2].SequenceEqual(packet[..^2]) && LanProtocol.Parse(redirected).Port == 16112, "preserve host identity and map bytes when rewriting port");
for (int i = 0; i < packet.Length; i++)
{
    try { LanProtocol.Parse(packet[..i]); throw new Exception("Truncated packet accepted at " + i); }
    catch (InvalidDataException) { }
}
Check(true, "all truncated packet boundaries rejected");
var wrongVersion = packet.ToArray(); wrongVersion[8] = 26;
try { LanProtocol.Parse(wrongVersion); throw new Exception("Wrong version accepted"); } catch (InvalidDataException) { checks++; }
Check(!Versions.IsSupported("1.26.0.6401") && !Versions.IsSupported("1.27") && Versions.IsSupported("1.27.0.52240"), "full 1.27 build required");
Check(GameCatalog.SupportsVersion(GameCatalog.StarCraft, "1.16.1.1") && !GameCatalog.SupportsVersion(GameCatalog.StarCraft, "1.16.1")
    && !GameCatalog.SupportsVersion(GameCatalog.StarCraft, "1.18.0.0") && !GameCatalog.SupportsVersion("unknown", "1.16.1.1"), "StarCraft exact build and known game required");
try { new PlatformClient("http://example.com"); throw new Exception("Insecure remote server accepted"); } catch (ArgumentException) { checks++; }

using (var httpClient = new PlatformClient("http://203.0.113.10:5080", allowInsecureHttp: true))
    Check(httpClient.Address.Scheme == "http" && httpClient.Address.Port == 5080, "explicit opt-in permits non-loopback HTTP with custom port");
Check(PlatformClient.ParseAddress("https://example.com", true).Scheme == "https", "HTTP opt-in preserves HTTPS addresses");
foreach (var invalid in new[] { "ftp://example.com", "http://user:pass@example.com", "http://example.com/api", "http://example.com/?token=x" })
{
    try { PlatformClient.ParseAddress(invalid, true); throw new Exception("Unsafe URL accepted with HTTP opt-in"); }
    catch (ArgumentException) { }
}
Check(true, "HTTP opt-in retains scheme, credentials and root-path validation");

// Deterministic expiry and cleanup, without waiting for real heartbeat/session timeouts.
var clock = new FakeClock();
var relay = new Relay(NullLogger<Relay>.Instance, clock);
var platform = new Platform(relay, clock);
var hostUser = new Account("host", "Host", "", "");
var guestUser = new Account("guest", "Guest", "", "");
var request = new CreateRoom("Test", "", "1.27.0.52240");
var directRoom = platform.Create(hostUser, request);
platform.Join(guestUser, directRoom.Id, new("", request.GameVersion));
platform.Publish(hostUser, directRoom.Id, new(Convert.ToBase64String(packet)));
platform.CreateTunnel(guestUser, directRoom.Id);
clock.Advance(9);
Check(platform.Get(hostUser, directRoom.Id).Game == null, "stale lobby announcement removed after match starts");
RejectDirect(() => platform.CreateTunnel(guestUser, directRoom.Id), 409, "no new connections without fresh advertisement");
clock.Advance(8); platform.Sweep();
Check(relay.Stats.ActiveTunnels == 0, "unpaired relay expires");
clock.Advance(30); platform.Get(hostUser, directRoom.Id); platform.Sweep();
Check(platform.Get(hostUser, directRoom.Id).Members.Length == 1, "stale guest evicted while active host remains");
clock.Advance(46); platform.Sweep();
Check(platform.List().Length == 0, "stale host closes room");
var session = platform.Login(hostUser);
var context = new DefaultHttpContext(); context.Request.Headers.Authorization = "Bearer " + session.Token;
Check(platform.Authorize(context).Id == hostUser.Id, "bearer authentication");
platform.Login(hostUser);
RejectDirect(() => platform.Authorize(context), 401, "new login revokes old session");

string root = Directory.GetCurrentDirectory();
string run = Path.Combine(root, "artifacts", "tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
string configPath = Path.Combine(run, "clientsettings.json");
Check(ClientConfiguration.LoadServerUrl(configPath) == "http://127.0.0.1:5080", "missing client config uses local development endpoint");
await File.WriteAllTextAsync(configPath, """{"serverUrl":"  https://example.com:8443/  "}""");
Check(ClientConfiguration.LoadServerUrl(configPath) == "https://example.com:8443", "client config supports custom port and normalizes whitespace");
await File.WriteAllTextAsync(configPath, """{"ServerUrl":"http://127.0.0.1:15080"}""");
Check(ClientConfiguration.LoadServerUrl(configPath) == "http://127.0.0.1:15080", "client config supports local development and SSH tunnel");
foreach (var invalid in new[] { "", "http://203.0.113.10", "https://example.com/api", "https://user:password@example.com" })
{
    await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(new { ServerUrl = invalid }));
    try { ClientConfiguration.LoadServerUrl(configPath); throw new Exception("Unsafe client config accepted"); }
    catch (ArgumentException) { }
}
Check(true, "invalid, insecure, path-prefixed and credential-bearing server configs rejected");
await File.WriteAllTextAsync(configPath, "{broken-json");
try { ClientConfiguration.LoadServerUrl(configPath); throw new Exception("Malformed configuration accepted"); }
catch (System.Text.Json.JsonException) { Check(true, "malformed client config reported"); }
await File.WriteAllTextAsync(configPath, """{"ServerUrl":" http://203.0.113.10:5080/ ","AllowInsecureHttp":true}""");
var httpConfig = ClientConfiguration.Load(configPath);
Check(httpConfig.AllowInsecureHttp && httpConfig.ServerUrl == "http://203.0.113.10:5080", "explicit HTTP preference loaded and address normalized");
await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(httpConfig));
Check(ClientConfiguration.Load(configPath) == httpConfig, "HTTP configuration survives save and reload");
await File.WriteAllTextAsync(configPath, """{"ServerUrl":"https://example.com"}""");
Check(!ClientConfiguration.Load(configPath).AllowInsecureHttp, "legacy configuration keeps HTTP opt-in disabled");

var fakeInstall = new GameInstallation(Path.Combine(run, "war3.exe"), "1.27.0.52240");
File.Copy(typeof(Fixtures).Assembly.Location, fakeInstall.Executable, true);
Check(PeVersion.Read(fakeInstall.Executable) == "1.27.0.52240", "portable PE VERSIONINFO reads full fixed file version");
var inspected = GameInstallation.Inspect(fakeInstall.Executable);
Check(inspected.Version == fakeInstall.Version && !Directory.Exists(Path.Combine(run, "Maps")), "game inspection requires no map file or Maps directory");
var starExe = Path.Combine(run, "StarCraft.exe");
var starBytes = File.ReadAllBytes(fakeInstall.Executable);
byte[] fixedVersion = [0xBD, 0x04, 0xEF, 0xFE, 0, 0, 1, 0, 27, 0, 1, 0, 0x10, 0xCC, 0, 0];
int versionOffset = -1;
using var fixtureReader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(starBytes));
var resources = fixtureReader.PEHeaders.SectionHeaders.Single(s => s.Name == ".rsrc");
for (int i = resources.PointerToRawData; i <= resources.PointerToRawData + resources.SizeOfRawData - fixedVersion.Length; i++)
    if (starBytes.AsSpan(i, fixedVersion.Length).SequenceEqual(fixedVersion)) { versionOffset = i; break; }
if (versionOffset < 0) throw new Exception("PE fixture version resource missing");
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(starBytes.AsSpan(versionOffset + 8), 0x00010010);
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(starBytes.AsSpan(versionOffset + 12), 0x00010001);
File.WriteAllBytes(starExe, starBytes);
var starInstall = GameInstallation.Inspect(starExe, GameCatalog.StarCraft);
Check(starInstall.Version == "1.16.1.1" && starInstall.GameId == GameCatalog.StarCraft, "StarCraft PE version inspection");
Check(!starInstall.CreateLaunchInfo().ArgumentList.Contains("-window"), "StarCraft does not inherit War3 launch arguments");
try { GameInstallation.Inspect(starExe); throw new Exception("StarCraft accepted as War3"); }
catch (InvalidOperationException) { Check(true, "wrong executable rejected for selected game"); }
var oldHealth = System.Text.Json.JsonSerializer.Deserialize<HealthView>("""{"Status":"ok","Version":"0.1.0"}""")!;
try { PlatformClient.RequireCompatibleServer(oldHealth); throw new Exception("Old map-checking server accepted"); }
catch (InvalidOperationException e) { Check(e.Message.Contains("同步更新"), "old map-checking server rejected with upgrade guidance"); }
var launch = fakeInstall.CreateLaunchInfo("/custom wine/bin/wine", Path.Combine(run, "wine prefix"));
Check(!launch.UseShellExecute && launch.ArgumentList.Last() == "-window", "launch uses separate arguments without a shell");
if (OperatingSystem.IsLinux())
{
    Check(launch.FileName == "/custom wine/bin/wine" && launch.ArgumentList[0] == fakeInstall.Executable && launch.Environment["WINEPREFIX"] == Path.Combine(run, "wine prefix"), "Linux Wine program, game path and prefix preserved");

}
else Check(launch.FileName == fakeInstall.Executable && launch.ArgumentList.Count == 1, "Windows launches game directly");
var invalidPe = Path.Combine(run, "invalid.exe");
File.WriteAllBytes(invalidPe, [0x4d, 0x5a]);
try { PeVersion.Read(invalidPe); throw new Exception("Invalid PE accepted"); }
catch (BadImageFormatException) { Check(true, "truncated PE rejected"); }
var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
string address = $"http://127.0.0.1:{port}";
var serverInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
serverInfo.ArgumentList.Add(Path.Combine(root, "src", "War3Connect.Server", "bin", "Release", "net8.0", "War3Connect.Server.dll"));
serverInfo.ArgumentList.Add("--urls"); serverInfo.ArgumentList.Add(address);
serverInfo.ArgumentList.Add("--DataDirectory"); serverInfo.ArgumentList.Add(Path.Combine(run, "data"));
using var server = Process.Start(serverInfo) ?? throw new Exception("Cannot start server");
var stdout = server.StandardOutput.ReadToEndAsync();
var stderr = server.StandardError.ReadToEndAsync();
try
{
    using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
    await Eventually(async () => { try { return (await http.GetAsync("health")).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } }, "real server starts");
    using (var unauthorized = await http.GetAsync("api/rooms")) Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized, "anonymous API denied");
    using var host = new PlatformClient(address);
    using var guest = new PlatformClient(address, allowInsecureHttp: true);
    using var guest2 = new PlatformClient(address);
    using var outsider = new PlatformClient(address);
    await host.LoginAsync("Host", "test-password-123", true);
    await guest.LoginAsync("Guest", "test-password-123", true);
    await guest2.LoginAsync("Guest2", "test-password-123", true);
    await outsider.LoginAsync("Outsider", "test-password-123", true);
    Check(host.Session != null && guest.Session != null, "register and login over HTTP");
    using var duplicate = new PlatformClient(address);
    await Reject(() => duplicate.LoginAsync("host", "test-password-123", true), HttpStatusCode.Conflict, "case-insensitive duplicate account rejected");
    await Reject(() => duplicate.LoginAsync("Host", "wrong-password", false), HttpStatusCode.Unauthorized, "wrong password rejected");
    string accountsFile = await File.ReadAllTextAsync(Path.Combine(run, "data", "accounts.json"));
    Check(!accountsFile.Contains("test-password-123") && accountsFile.Contains("Salt"), "accounts persist only salted password hashes");
    await Reject(() => host.Post<RoomView>("api/rooms", new CreateRoom("SC", "", "1.16.1.1", 9, GameCatalog.StarCraft)), HttpStatusCode.BadRequest, "StarCraft eight-player limit enforced");
    var scRoom = await host.Post<RoomView>("api/rooms", new CreateRoom("SC preparation", "", "1.16.1.1", GameId: GameCatalog.StarCraft));
    var scGuest = await guest.Post<RoomView>($"api/rooms/{scRoom.Id}/join", new JoinRoom("", "1.16.1.1", GameCatalog.StarCraft));
    Check(scGuest.GameId == GameCatalog.StarCraft && scGuest.Members.Length == 2, "StarCraft game ID survives HTTP create and join");
    await Reject(() => guest2.Post<RoomView>($"api/rooms/{scRoom.Id}/join", new JoinRoom("", "1.16.1.2", GameCatalog.StarCraft)), HttpStatusCode.Conflict, "StarCraft different builds isolated");
    await Reject(() => host.Post($"api/rooms/{scRoom.Id}/game", new PublishGame(Convert.ToBase64String(packet))), HttpStatusCode.Conflict, "StarCraft cannot publish War3 advertisements");
    await Reject(() => guest.Post<TunnelView>($"api/rooms/{scRoom.Id}/tunnels", new { }), HttpStatusCode.Conflict, "StarCraft cannot create War3 tunnel");
    await Reject(() => host.Get<TunnelView[]>($"api/rooms/{scRoom.Id}/tunnels"), HttpStatusCode.Conflict, "StarCraft cannot poll War3 tunnels");
    var scUpdate = new TaskCompletionSource<RoomView>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var scAgent = new RoomAgent(guest, scGuest))
    {
        scAgent.Updated += r => { if (r.Messages.Any(m => m.Text == "SC preparation chat")) scUpdate.TrySetResult(r); };
        scAgent.Start();
        await host.Post($"api/rooms/{scRoom.Id}/chat", new SendChat("SC preparation chat"));
        var state = await scUpdate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(state.GameId == GameCatalog.StarCraft && scAgent.LocalPort == 0 && scAgent.ActiveConnections == 0, "StarCraft room heartbeat and chat work without fake game proxy");
    }
    await host.Post($"api/rooms/{scRoom.Id}/leave");
    await Reject(() => guest.Get<RoomView>($"api/rooms/{scRoom.Id}"), HttpStatusCode.NotFound, "StarCraft preparation room cleanup");
    var room = await host.Post<RoomView>("api/rooms", new CreateRoom("Integration", "room-key", fakeInstall.Version, 3));
    await Reject(() => outsider.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("room-key", fakeInstall.Version, GameCatalog.StarCraft)), HttpStatusCode.Conflict, "cross-game room join rejected even with matching version");
    await Reject(() => guest.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("bad", fakeInstall.Version)), HttpStatusCode.Forbidden, "room password enforced");
    await Reject(() => guest.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("room-key", "1.27.1.7085")), HttpStatusCode.Conflict, "1.27a and 1.27b isolated");
    var joined = await guest.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("room-key", fakeInstall.Version));
    var joined2 = await guest2.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("room-key", fakeInstall.Version));
    await Reject(() => outsider.Post<RoomView>($"api/rooms/{room.Id}/join", new JoinRoom("room-key", fakeInstall.Version)), HttpStatusCode.Conflict, "room capacity enforced");
    await Reject(() => outsider.Get<RoomView>($"api/rooms/{room.Id}"), HttpStatusCode.Forbidden, "outsider cannot read private room state");
    await Reject(() => guest.Post($"api/rooms/{room.Id}/game", new PublishGame(Convert.ToBase64String(packet))), HttpStatusCode.Forbidden, "only host can publish game");
    await host.Post($"api/rooms/{room.Id}/chat", new SendChat("准备开始"));
    Check((await guest.Get<RoomView>($"api/rooms/{room.Id}")).Messages.Single().Text == "准备开始", "room chat delivered");

    Check(joined.Members.Length == 2 && joined2.Members.Length == 3, "create and join without map metadata");
    await using var fakeGame = new FakeGame();
    await using var hostAgent = new RoomAgent(host, room);
    await using var guestAgent = new RoomAgent(guest, joined);
    await using var guestAgent2 = new RoomAgent(guest2, joined2);
    hostAgent.Log += line => Console.WriteLine("HOST: " + line);
    guestAgent.Log += line => Console.WriteLine("GUEST: " + line);
    hostAgent.Start(); guestAgent.Start(); guestAgent2.Start();
    await Eventually(async () => (await guest.Get<RoomView>($"api/rooms/{room.Id}")).Game != null, "host UDP discovery publishes room game");
    await Eventually(() => Task.FromResult(fakeGame.LastProxyAnnouncement != null), "guest republishes game on local UDP");
    Check(LanProtocol.Parse(fakeGame.LastProxyAnnouncement!).Port != fakeGame.Port, "guest advertises local proxy port");

    Check(!Directory.Exists(Path.Combine(run, "Maps")), "native LAN listing advertised even with no local maps");
    var originalAnnouncement = Fixtures.Announcement(fakeGame.Port);
    Check(fakeGame.LastProxyAnnouncement![..^2].SequenceEqual(originalAnnouncement[..^2]), "native map metadata and host identity pass through unchanged");
    fakeGame.MapPath = "Maps\\Download\\changed-map.w3x";
    await Eventually(async () => (await guest.Get<RoomView>($"api/rooms/{room.Id}")).Game?.MapPath == fakeGame.MapPath, "host can switch map within same platform room");
    await Eventually(() => Task.FromResult(LanProtocol.Parse(fakeGame.LastProxyAnnouncement!).MapPath == fakeGame.MapPath), "new map appears in native LAN advertisement without file checks");

    using var player = new TcpClient();
    using var player2 = new TcpClient();
    await player.ConnectAsync(IPAddress.Loopback, guestAgent.LocalPort);
    await player2.ConnectAsync(IPAddress.Loopback, guestAgent2.LocalPort);
    async Task RoundTrip(TcpClient client, byte seed, int size)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var data = Enumerable.Range(0, size).Select(i => (byte)(i * 31 + seed)).ToArray();
        var received = new byte[size];
        var stream = client.GetStream();
        var read = stream.ReadExactlyAsync(received, timeout.Token).AsTask();
        for (int offset = 0; offset < data.Length; offset += 997)
            await stream.WriteAsync(data.AsMemory(offset, Math.Min(997, data.Length - offset)), timeout.Token);
        await read;
        Check(data.SequenceEqual(received), $"end-to-end TCP/WS relay payload preserved ({seed}, {size} bytes)");
    }
    await Task.WhenAll(RoundTrip(player, 7, 150000), RoundTrip(player2, 211, 120000));
    Check((await host.Get<RelayStats>("api/relay-status")).ActiveTunnels == 2, "independent simultaneous player tunnels");
    var bulkTimer = Stopwatch.StartNew();
    await RoundTrip(player, 93, 6 * 1024 * 1024);
    Check(bulkTimer.Elapsed >= TimeSpan.FromSeconds(1), "bulk traffic is paced by relay bandwidth limit");
    Check((await host.Get<RelayStats>("api/relay-status")).ActiveTunnels == 2, "multi-megabyte transfer preserves both player connections");
    await RoundTrip(player, 17, 256);
    // Free guest2's connection for an explicit credential hijack test.
    player2.Close();
    await Eventually(async () => (await host.Get<RelayStats>("api/relay-status")).ActiveTunnels == 1, "guest TCP close cleans relay connection");
    var reserved = await guest2.Post<TunnelView>($"api/rooms/{room.Id}/tunnels", new { });
    try { using var badSocket = await outsider.ConnectTunnel(reserved.Id, CancellationToken.None); throw new Exception("Outsider attached relay"); }
    catch (WebSocketException) { Check(true, "outsider cannot hijack known tunnel ID"); }

    fakeGame.Advertising = false;
    await Eventually(async () => (await guest.Get<RoomView>($"api/rooms/{room.Id}")).Game == null, "in-game discovery expiry removes lobby advertisement", 15);
    await RoundTrip(player, 42, 5000);
    await Reject(() => guest2.Post<TunnelView>($"api/rooms/{room.Id}/tunnels", new { }), HttpStatusCode.Conflict, "late game join rejected after advertisement expiry");
    await host.Post($"api/rooms/{room.Id}/leave");
    await Eventually(async () => (await guest.Get<RelayStats>("api/relay-status")).ActiveTunnels == 0, "host leave cancels all room tunnels");
    await Reject(() => guest.Get<RoomView>($"api/rooms/{room.Id}"), HttpStatusCode.NotFound, "host leave closes room");
    Check((await outsider.Get<RoomView[]>("api/rooms")).Length == 0, "closed room disappears from lobby");
    using var loginAgain = new PlatformClient(address);
    await loginAgain.LoginAsync("Host", "test-password-123", false);
    Check(loginAgain.Session?.UserId == host.Session!.UserId, "persisted account can log in again");
}
finally
{
    if (!server.HasExited) server.Kill(entireProcessTree: true);
    await server.WaitForExitAsync();
    await File.WriteAllTextAsync(Path.Combine(run, "server.log"), await stdout + await stderr);
}
Console.WriteLine($"SUCCESS: {checks} checks. Logs: {run}");
