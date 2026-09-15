using System.Text.Json.Serialization;
using DbUp;
using Game.Shared.Config;
using Game.Shared.Jwt;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);


// =================== 【迁移入口：--migrate 或 RUN_MIGRATION_ONLY=true】 ===================
var runMigrationOnly = args.Contains("--migrate")
    || string.Equals(Environment.GetEnvironmentVariable("RUN_MIGRATION_ONLY"), "true", StringComparison.OrdinalIgnoreCase);

if (runMigrationOnly)
{
    var migrateConn = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__Postgres");
    Console.WriteLine("Executing database migrations...");
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(migrateConn)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
        .WithTransaction()
        .LogToConsole()
        .Build();
    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
    {
        Console.Error.WriteLine($"DB migration failed: {result.Error}");
        Environment.Exit(1);
    }
    Console.WriteLine("Migration completed successfully.");
    return; // 只跑迁移，不启动 Web
}
// ========================================================================================

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("缺少 Jwt__Secret（必须与 MP 一致）");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "battle-net-mp";
var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("缺少 ConnectionStrings__Postgres");

builder.Services.AddSingleton(new SimpleJwt(jwtSecret, jwtIssuer));
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connStr).Build());

var configRoot = GameConfigStore.ResolveConfigRoot(
    builder.Configuration["Config:Root"] ?? builder.Configuration["Config__Root"]);
var gameConfig = GameConfigStore.Load(configRoot);
builder.Services.AddSingleton(gameConfig);

var app = builder.Build();

// 正常启动路径不再执行迁移。由 *-migrate Job 或 --migrate 完成。

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", "game-user")));

// 获取或自动创建当前玩家在某个游戏的资料
app.MapGet("/api/v1/user/profile", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, string game_id) =>
{
    if (!TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        SELECT id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        FROM player_profiles
        WHERE mp_account_id = @mp AND game_id = @gid
        """, conn);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));
    cmd.Parameters.AddWithValue("gid", game_id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
        return Results.Ok(ReadProfile(reader));

    await reader.CloseAsync();
    await using var insert = new NpgsqlCommand("""
        INSERT INTO player_profiles (mp_account_id, game_id, nickname)
        VALUES (@mp, @gid, @nick)
        RETURNING id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        """, conn);
    insert.Parameters.AddWithValue("mp", Guid.Parse(claims.Sub));
    insert.Parameters.AddWithValue("gid", game_id);
    insert.Parameters.AddWithValue("nick", "Player_" + claims.Sub[..Math.Min(8, claims.Sub.Length)]);

    await using var r2 = await insert.ExecuteReaderAsync();
    await r2.ReadAsync();
    return Results.Ok(ReadProfile(r2));
});

// 更新昵称 / 等级 / 扩展字段
app.MapPut("/api/v1/user/profile", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, UpdateProfileRequest body) =>
{
    if (!TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        INSERT INTO player_profiles (mp_account_id, game_id, nickname, level, extra_json, updated_at)
        VALUES (@mp, @gid, @nick, COALESCE(@lv, 1), COALESCE(@extra::jsonb, '{}'::jsonb), NOW())
        ON CONFLICT (mp_account_id, game_id) DO UPDATE SET
            nickname = COALESCE(NULLIF(@nick, ''), player_profiles.nickname),
            level = COALESCE(@lv, player_profiles.level),
            extra_json = COALESCE(@extra::jsonb, player_profiles.extra_json),
            updated_at = NOW()
        RETURNING id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        """, conn);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));
    cmd.Parameters.AddWithValue("gid", body.GameId);
    cmd.Parameters.AddWithValue("nick", body.Nickname ?? "");
    cmd.Parameters.AddWithValue("lv", (object?)body.Level ?? DBNull.Value);
    cmd.Parameters.AddWithValue("extra", body.ExtraJson ?? "{}");

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(ReadProfile(reader));
});

app.Run("http://0.0.0.0:8080");

// ---------- helpers ----------
static bool TryGetClaims(HttpContext ctx, SimpleJwt jwt, out JwtClaims? claims)
{
    claims = null;
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    return jwt.TryValidate(auth["Bearer ".Length..].Trim(), out claims) && claims is not null;
}

static ProfileResponse ReadProfile(NpgsqlDataReader r) => new(
    r.GetGuid(0),
    r.GetGuid(1).ToString(),
    r.GetString(2),
    r.GetString(3),
    r.GetInt32(4),
    r.GetString(5),
    r.GetDateTime(6),
    r.GetDateTime(7)
);

public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error);

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("service")] string Service);

public sealed record ProfileResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("mp_account_id")] string MpAccountId,
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("extra_json")] string ExtraJson,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt);

public sealed record UpdateProfileRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("nickname")] string? Nickname,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("extra_json")] string? ExtraJson);

[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(UpdateProfileRequest))]
internal partial class AppJsonContext : JsonSerializerContext;
