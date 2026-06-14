using System.Text.Json.Serialization;
using Serilog;
using AIChat.Api.Extensions;
using AIChat.Api.Hubs;
using AIChat.Api.Services;
using AIChat.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Additional config files. Env vars are re-registered after so they continue to win.
builder.Configuration
    .AddJsonFile("config/users.json", optional: false, reloadOnChange: true)
    .AddJsonFile("config/azure-openai.json", optional: true, reloadOnChange: true)
    .AddJsonFile("config/models.json", optional: false, reloadOnChange: true)
    .AddJsonFile("config/memory.json", optional: false, reloadOnChange: true)
    .AddJsonFile("config/prompt-profiles.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Serilog: console + hourly-rolling file. Config lives in appsettings.json under "Serilog".
builder.Host.UseSerilog((ctx, _, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Add services to the container
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Serialize enums as camelCase strings ("fact", "preference", "summary")
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR(options =>
{
    // The client sends the full conversation history on each SendMessage, which easily
    // exceeds SignalR's 32 KB default for long, code-heavy chats. Raising the cap avoids
    // the server closing the WebSocket mid-turn ("Connection closed with an error").
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

// Register application services
builder.Services.AddSingleton<IUserIdentityService, UserIdentityService>();
builder.Services.AddAzureOpenAI(builder.Configuration);
builder.Services.AddPromptProfiles(builder.Configuration);
builder.Services.AddMemoryServices(builder.Configuration);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");
app.UseAuthCode();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");

app.Run();
