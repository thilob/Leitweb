using Leitweb.Api.Data;
using Leitweb.Api.Diagnostics;
using Leitweb.Api.Realtime;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new Leitweb.Api.Serialization.DateOnlyJsonConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<KeycloakUserService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<RuntimeStatusService>();
builder.Services.AddSingleton<LiveUpdateHub>();
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("Development:UseInMemoryDatabase");
builder.Services.AddDbContext<LeitwebDbContext>(options =>
{
    if (useInMemoryDatabase) options.UseInMemoryDatabase("leitweb-development");
    else options.UseNpgsql(builder.Configuration.GetConnectionString("Database"), npgsql => npgsql.UseNetTopologySuite());
});

var useTestAuthentication = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Development:UseTestAuthentication");
var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = useTestAuthentication ? DevelopmentAuthenticationHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = useTestAuthentication ? DevelopmentAuthenticationHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
});
if (useTestAuthentication)
{
    authentication.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName, _ => { });
}
else
{
    authentication.AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        var metadataAddress = builder.Configuration["Authentication:MetadataAddress"];
        if (!string.IsNullOrWhiteSpace(metadataAddress)) options.MetadataAddress = metadataAddress;
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path == "/ws/updates")
                    context.Token = context.Request.Query["access_token"];
                return Task.CompletedTask;
            }
        };
    });
}
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
    options.AddPolicy(Permissions.UserAdminPolicy, policy =>
        policy.RequireAssertion(context => RealmRoles.HasRole(context.User, Permissions.UserAdminRole)));
    options.AddPolicy(Permissions.GisViewPolicy, policy => policy.RequireAssertion(context =>
        RealmRoles.HasAnyRole(context.User, Permissions.GisViewRole, Permissions.GisEditRole, Permissions.GisFullAccessRole)));
    options.AddPolicy(Permissions.GisEditPolicy, policy => policy.RequireAssertion(context =>
        RealmRoles.HasAnyRole(context.User, Permissions.GisEditRole, Permissions.GisFullAccessRole)));
    options.AddPolicy(Permissions.GisFullAccessPolicy, policy => policy.RequireAssertion(context =>
        RealmRoles.HasRole(context.User, Permissions.GisFullAccessRole)));
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeitwebDbContext>();
    if (useInMemoryDatabase) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    if (builder.Configuration.GetValue("InitialData:Seed", useInMemoryDatabase)) await DevelopmentData.SeedAsync(db);
    await AddressSeedImporter.ImportIfEmptyAsync(db, Path.Combine(app.Environment.ContentRootPath, "Data", "addresses.tsv"));
}
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
        app.Logger.LogInformation("HTTP {Method} {Path} completed with {StatusCode} in {ElapsedMilliseconds} ms",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "HTTP {Method} {Path} failed after {ElapsedMilliseconds} ms",
            context.Request.Method, context.Request.Path, stopwatch.ElapsedMilliseconds);
        throw;
    }
});
app.MapGet("/app-config.json", (IConfiguration configuration) => Results.Ok(new
{
    authority = configuration["Authentication:PublicAuthority"] ?? configuration["Authentication:Authority"],
    tokenEndpoint = "/auth/token",
    clientId = configuration["Authentication:ClientId"] ?? configuration["Authentication:Audience"],
    qgisPublicUrl = configuration["Gis:QgisPublicUrl"],
    useTestAuthentication
}));
app.MapPost("/auth/token", async (HttpContext context, IConfiguration configuration,
    IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var request = context.Request;
    if (!request.HasFormContentType) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
    var form = await request.ReadFormAsync(ct);
    var clientId = configuration["Authentication:ClientId"] ?? configuration["Authentication:Audience"];
    if (string.IsNullOrWhiteSpace(clientId) ||
        !string.Equals(form["client_id"].ToString(), clientId, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "invalid_client" });
    var grantType = form["grant_type"].ToString();
    if (grantType is not ("authorization_code" or "refresh_token"))
        return Results.BadRequest(new { error = "unsupported_grant_type" });

    var allowedParameters = new[] { "grant_type", "client_id", "code", "redirect_uri", "code_verifier", "refresh_token" };
    var parameters = allowedParameters.Where(form.ContainsKey)
        .ToDictionary(key => key, key => form[key].ToString(), StringComparer.Ordinal);
    var keycloakBaseUrl = (configuration["KeycloakAdmin:BaseUrl"] ?? "http://identity:8080").TrimEnd('/');
    var realm = configuration["KeycloakAdmin:Realm"] ?? "leitweb";
    try
    {
        using var response = await httpClientFactory.CreateClient().PostAsync(
            $"{keycloakBaseUrl}/realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token",
            new FormUrlEncodedContent(parameters), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json",
            statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Der Anmeldedienst ist im Docker-Netz vorübergehend nicht erreichbar.", statusCode: 503);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate"
});
app.MapGet("/status", (HttpContext context, IWebHostEnvironment environment) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    return Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
});
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/ws/updates", async (HttpContext context, LiveUpdateHub updates) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await updates.HoldAsync(socket, context.RequestAborted);
}).RequireAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (LeitwebDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/health/status", async (RuntimeStatusService status, CancellationToken ct) =>
    Results.Ok(await status.CheckAsync(ct)));
app.Run();

public partial class Program { }
