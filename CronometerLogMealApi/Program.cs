using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Services;
using CronometerLogMealApi.Clients.OpenAIClient;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<CronometerLogMealApi.Swagger.FromHeaderComplexTypeOperationFilter>();
});

// Typed HttpClient for Cronometer API
builder.Services.AddHttpClient<CronometerHttpClient>(client =>
{
    client.BaseAddress = new Uri("https://mobile.cronometer.com/api/v2/");
});

// Typed HttpClient for Telegram Bot API
var botToken = builder.Configuration["Telegram:BotToken"];
builder.Services.AddHttpClient<TelegramHttpClient>((sp, client) =>
{
    client.BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/");
});

// Singleton service to track LastUpdateId and encapsulate Telegram logic
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddHostedService<CronometerPollingHostedService>();
builder.Services.AddTransient<CronometerService>();

// OpenAI options + typed HttpClient
builder.Services.Configure<OpenAIClientOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddHttpClient<OpenAIHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAIClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
});

// Azure Vision options + service registration
builder.Services.Configure<CronometerLogMealApi.Clients.AzureVisionClient.AzureVisionClientOptions>(
    builder.Configuration.GetSection("AzureVision"));
builder.Services.AddSingleton<CronometerLogMealApi.Clients.AzureVisionClient.AzureVisionService>();

// Firebase options + UserMemoryService (optional - only registered if ProjectId is configured)
builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection("Firebase"));
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (!string.IsNullOrWhiteSpace(firebaseProjectId))
{
    builder.Services.AddSingleton<UserMemoryService>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
