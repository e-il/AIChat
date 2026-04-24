using System.Text.Json.Serialization;
using AIChat.Api.Hubs;
using AIChat.Api.Services;
using AIChat.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Additional config files. Env vars are re-registered after so they continue to win.
builder.Configuration
    .AddJsonFile("config/users.json", optional: false, reloadOnChange: true)
    .AddJsonFile("config/models.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Serialize enums as camelCase strings ("fact", "preference", "summary")
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

// Register application services
builder.Services.AddSingleton<IUserIdentityService, UserIdentityService>();
builder.Services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
builder.Services.AddSingleton<IMemoryService, MemoryService>();

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
