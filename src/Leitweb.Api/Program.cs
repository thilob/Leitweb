using Leitweb.Api.Data;
using Leitweb.Api.Realtime;
using Leitweb.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new Leitweb.Api.Serialization.DateOnlyJsonConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<KeycloakUserService>();
builder.Services.AddSingleton<LiveUpdateHub>();
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("Development:UseInMemoryDatabase");
builder.Services.AddDbContext<LeitwebDbContext>(options =>
{
    if (useInMemoryDatabase) options.UseInMemoryDatabase("leitweb-development");
    else options.UseNpgsql(builder.Configuration.GetConnectionString("Database"));
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
app.MapGet("/app-config.json", (IConfiguration configuration) => Results.Ok(new
{
    authority = configuration["Authentication:PublicAuthority"] ?? configuration["Authentication:Authority"],
    clientId = configuration["Authentication:ClientId"] ?? configuration["Authentication:Audience"]
}));
app.UseDefaultFiles();
app.UseStaticFiles();
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
app.Run();

public partial class Program { }
