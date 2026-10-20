using System.Net;
using System.Text.Json;
using GuerraDasNacoes;
using GuerraDasNacoes.Server;

if(args.Length==3 && args[0]=="--backup-db") { MonsterCatalogMigrationSelfTests.CopyDatabase(args[1],args[2]); Console.WriteLine("Backup SQLite consistente criado."); return; }

if(args.Contains("--monster-catalog-self-test")) { MonsterCatalogMigrationSelfTests.Run(); MonsterBehaviorSelfTests.Run(); return; }
if(args.Contains("--catacombs-self-test")) { CatacombsSelfTests.Run(); return; }
if(args.Length==4 && args[0]=="--verify-catalog-copy") { MonsterCatalogMigrationSelfTests.VerifyExistingCopy(args[1],args[2],args[3]); return; }

if(args.Contains("--frontier-self-test")) { WorldExpansionSelfTests.Run(); return; }

if(args.Contains("--economy-self-test")) { EconomySelfTests.Run(); return; }
if(args.Contains("--menu-self-test")) { MenuSelfTests.Run(); return; }
if(args.Contains("--nickname-self-test")) { NicknameSelfTests.Run(); return; }

if (args.Contains("--self-test"))
{
    SelfTests.Run();
    EconomySelfTests.Run();
    EconomyRegressionChecks.Run();
    MenuSelfTests.Run();
    NicknameSelfTests.Run();
    WorldExpansionSelfTests.Run();
    MonsterCatalogMigrationSelfTests.Run();
    MonsterBehaviorSelfTests.Run();
    CatacombsSelfTests.Run();
    return;
}

string serverRoot = FindServerRoot();
string host = Environment.GetEnvironmentVariable("HOST") ?? "127.0.0.1";
if (!int.TryParse(Environment.GetEnvironmentVariable("PORT") ?? "8088", out int port) || port is < 1 or > 65535)
    throw new InvalidOperationException("PORT inválida.");
bool loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
if (!loopback && Environment.GetEnvironmentVariable("GAME_ALLOW_INSECURE_LAN") != "1")
    throw new InvalidOperationException("HTTP fora de loopback requer GAME_ALLOW_INSECURE_LAN=1, somente para teste em rede confiável. Produção precisa de TLS e autenticação de conta.");
string collisionPath = Environment.GetEnvironmentVariable("COLLISION_PATH") ?? Path.Combine(serverRoot, "world-collision.json");
string databasePath = Environment.GetEnvironmentVariable("DB_PATH") ?? Path.Combine(serverRoot, "data", "characters.db");
databasePath = Path.GetFullPath(databasePath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
FileStream databaseLease;
try
{
    databaseLease = new FileStream(databasePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    Console.Error.WriteLine("Este banco já está sendo usado por outro processo do servidor, ou seu arquivo de exclusão não pode ser aberto. Encerre a outra instância ou escolha outro DB_PATH. Nenhum personagem foi alterado.");
    Environment.ExitCode = 1;
    return;
}
using var exclusiveDatabaseLease = databaseLease;
var adminSecurity = new AdminSecurity(databasePath);
string catacombsCollisionPath = Environment.GetEnvironmentVariable("CATACOMBS_COLLISION_PATH") ?? Path.Combine(serverRoot,"catacombs-collision.json");
var world = new GameWorld(new SaveStore(databasePath), CollisionGrid.Load(collisionPath), catacombsGrid:File.Exists(catacombsCollisionPath)?CollisionGrid.Load(catacombsCollisionPath):null);
if (world.LegacyNicknameCollisionCount > 0)
    Console.Error.WriteLine($"Atenção: {world.LegacyNicknameCollisionCount} nick(s) legado(s) têm mais de um titular. O progresso e a reentrada foram preservados; esses nomes estão bloqueados para novos personagens até reconciliação administrativa.");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://{host}:{port}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1048576);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.IncludeFields = true;
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.MaxDepth = 32;
});
builder.Services.AddSingleton(world);
builder.Services.AddSingleton<WorldHealth>();
builder.Services.AddHostedService<WorldTicker>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["X-Frame-Options"]="DENY";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
    try { await next(context); }
    catch (GameApiException error)
    {
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(new OnlineError { code = error.Code, message = error.Message }, WireJson.Options);
    }
    catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
    {
        app.Logger.LogError("Falha de armazenamento ou estado. Novas escritas exigem recuperação do banco. Tipo: {Type}", error.GetType().Name);
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new OnlineError { code = "storage_unavailable", message = "Não foi possível confirmar a gravação. Reenvie os mesmos comandos para consultar ou concluir a operação." }, WireJson.Options);
    }
});
app.MapGet("/health", (WorldHealth health) => health.StorageHealthy
    ? Results.Ok(new { status = "ok", protocolVersion = 2, authority = "server", storage = "sqlite" })
    : Results.Json(new { status = "storage_unavailable" }, statusCode: 503));
app.MapPost("/v1/connect", (OnlineConnectRequest request) => Results.Json(world.Connect(request), WireJson.Options));
app.MapPost("/v1/characters/preview", (OnlineConnectRequest request) => Results.Json(world.CharacterPreview(request), WireJson.Options));
app.MapPost("/v1/characters/nickname-check", (OnlineNicknameCheckRequest request) => Results.Json(world.NicknameAvailability(request), WireJson.Options));
app.MapPost("/v1/disconnect", (HttpContext context) =>
{
    string authorization = context.Request.Headers.Authorization.ToString();
    string token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
    return Results.Json(world.Disconnect(token), WireJson.Options);
});
app.MapPost("/v1/commands", (HttpContext context, OnlineCommandBatch request) =>
{
    string authorization = context.Request.Headers.Authorization.ToString();
    string token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
    return Results.Json(world.Commands(token, request), WireJson.Options);
});
app.MapGet("/admin", (HttpContext context) =>
{
    adminSecurity.ValidateOrigin(context,false);
    return Results.Content(File.ReadAllText(Path.Combine(serverRoot,"Admin","index.html")),"text/html; charset=utf-8");
});
app.MapPost("/admin/api/login",(HttpContext context,AdminLoginRequest request)=>Results.Json(adminSecurity.Login(context,request),WireJson.Options));
app.MapPost("/admin/api/logout",(HttpContext context)=>Results.Json(adminSecurity.Logout(context),WireJson.Options));
app.MapGet("/admin/api/state",(HttpContext context)=>{adminSecurity.Authenticate(context,false);return Results.Json(world.AdminState(),WireJson.Options);});
app.MapPut("/admin/api/draft",(HttpContext context,AdminDraftRequest request)=>{adminSecurity.Authenticate(context,true);return Results.Json(world.AdminDraft(request),WireJson.Options);});
app.MapPost("/admin/api/validate",(HttpContext context,AdminValidateRequest request)=>{adminSecurity.Authenticate(context,true);return Results.Json(world.AdminValidate(request.config),WireJson.Options);});
app.MapPost("/admin/api/simulate",(HttpContext context,AdminSimulateRequest request)=>{adminSecurity.Authenticate(context,true);return Results.Json(world.AdminSimulate(request.config,request.kills,request.killRate),WireJson.Options);});
app.MapPost("/admin/api/publish",(HttpContext context,AdminPublishRequest request)=>{adminSecurity.Authenticate(context,true);return Results.Json(world.AdminPublish(request),WireJson.Options);});
app.MapPost("/admin/api/rollback",(HttpContext context,AdminRollbackRequest request)=>{adminSecurity.Authenticate(context,true);return Results.Json(world.AdminRollback(request),WireJson.Options);});
Console.WriteLine($"Guerra das Nações — servidor de protótipo em http://{host}:{port}");
Console.WriteLine("Persistência autoritativa ativa. Identidades e sessões nunca são registradas no console.");
await app.RunAsync();

static string FindServerRoot()
{
    string current = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(current, "Server.csproj"))) return current;
    if (File.Exists(Path.Combine(current, "Server", "Server.csproj"))) return Path.Combine(current, "Server");
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Server.csproj"))) return directory.FullName;
        directory = directory.Parent;
    }
    return AppContext.BaseDirectory;
}

namespace GuerraDasNacoes.Server
{
    public sealed class WorldHealth { public volatile bool StorageHealthy = true; }
    public sealed class WorldTicker(GameWorld world, WorldHealth health, ILogger<WorldTicker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { world.Tick(); health.StorageHealthy = true; }
                catch (Exception error)
                {
                    if (health.StorageHealthy) logger.LogError("Tick sem confirmação por falha de persistência. Tipo: {Type}", error.GetType().Name);
                    health.StorageHealthy = false;
                }
            }
        }
    }
}
