using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using War3Connect.Core;
using War3Connect.Server;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16384);
builder.Services.AddSingleton<Accounts>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Relay>();
builder.Services.AddSingleton<Platform>();
builder.Services.AddHostedService<Cleanup>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Default known proxies are loopback only; do not trust arbitrary forwarding headers.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.RequestServices.GetRequiredService<Platform>().Authorize(context).Id,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 360, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiException e) when (!context.Response.HasStarted)
    { context.Response.StatusCode = e.Status; await context.Response.WriteAsJsonAsync(new ErrorResponse(e.Message)); }
    catch (BadHttpRequestException) when (!context.Response.HasStarted)
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new ErrorResponse("请求格式无效。")); }
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseForwardedHeaders();
app.UseRateLimiter();
app.MapGet("/health", () => new HealthView("ok", Versions.Release, Versions.ProtocolVersion));
app.MapPost("/api/register", async (Credentials c, Accounts a, Platform p) => p.Login(await a.Authenticate(c, true))).RequireRateLimiting("auth");
app.MapPost("/api/login", async (Credentials c, Accounts a, Platform p) => p.Login(await a.Authenticate(c, false))).RequireRateLimiting("auth");
var api = app.MapGroup("/api").RequireRateLimiting("api");
api.AddEndpointFilter(async (context, next) =>
{
    var platform = context.HttpContext.RequestServices.GetRequiredService<Platform>();
    context.HttpContext.Items["user"] = platform.Authorize(context.HttpContext);
    return await next(context);
});
static Account User(HttpContext c) => (Account)c.Items["user"]!;
api.MapPost("/logout", (HttpContext c, Platform p) => { p.Logout(User(c)); return Results.NoContent(); });
api.MapGet("/rooms", (Platform p) => p.List());
api.MapGet("/relay-status", (Relay r) => r.Stats);
api.MapPost("/rooms", (CreateRoom r, HttpContext c, Platform p) => p.Create(User(c), r));
api.MapGet("/rooms/{id}", (string id, HttpContext c, Platform p) => p.Get(User(c), id));
api.MapPost("/rooms/{id}/join", (string id, JoinRoom r, HttpContext c, Platform p) => p.Join(User(c), id, r));
api.MapPost("/rooms/{id}/leave", (string id, HttpContext c, Platform p) => { p.Leave(User(c), id); return Results.NoContent(); });
api.MapPost("/rooms/{id}/chat", (string id, SendChat r, HttpContext c, Platform p) => { p.Chat(User(c), id, r); return Results.NoContent(); });
api.MapPost("/rooms/{id}/game", (string id, PublishGame r, HttpContext c, Platform p) => { p.Publish(User(c), id, r); return Results.NoContent(); });
api.MapPost("/rooms/{id}/tunnels", (string id, HttpContext c, Platform p) => p.CreateTunnel(User(c), id));
api.MapGet("/rooms/{id}/tunnels", (string id, HttpContext c, Platform p) => p.Pending(User(c), id));
// Authorization uses the same opaque bearer token, never query-string tokens.
app.MapGet("/relay/{id}", async (string id, HttpContext c, Platform p, Relay r) => await r.Attach(c, id, p.Authorize(c).Id)).RequireRateLimiting("api");
app.Run();

public sealed class Cleanup(Platform platform) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken)) platform.Sweep();
    }
}
