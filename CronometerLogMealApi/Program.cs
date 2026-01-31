using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Handlers;
using CronometerLogMealApi.Handlers.Commands;
using CronometerLogMealApi.Handlers.StateProcessors;
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

// ===== HTTP CLIENTS =====

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

// OpenAI options + typed HttpClient
builder.Services.Configure<OpenAIClientOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddHttpClient<OpenAIHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAIClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
});

// ===== CORE SERVICES =====

// Telegram service (implements ITelegramService)
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<ITelegramService>(sp => sp.GetRequiredService<TelegramService>());

// Session management
builder.Services.AddSingleton<ISessionManager, InMemorySessionManager>();

// Cronometer service (implements ICronometerService)
builder.Services.AddTransient<CronometerService>();
builder.Services.AddTransient<ICronometerService>(sp => sp.GetRequiredService<CronometerService>());

// Azure Vision options + service registration
builder.Services.Configure<CronometerLogMealApi.Clients.AzureVisionClient.AzureVisionClientOptions>(
    builder.Configuration.GetSection("AzureVision"));
builder.Services.AddSingleton<CronometerLogMealApi.Clients.AzureVisionClient.AzureVisionService>();
builder.Services.AddSingleton<IOcrService, AzureOcrServiceAdapter>();

// LLM Meal Processor
builder.Services.AddTransient<IMealProcessor, LlmMealProcessor>();

// Meal Validation Orchestrator
builder.Services.AddTransient<MealValidationOrchestrator>();
builder.Services.AddTransient<IMealValidationOrchestrator>(sp => sp.GetRequiredService<MealValidationOrchestrator>());
builder.Services.AddTransient<IAlternativeSearchHandler>(sp => sp.GetRequiredService<MealValidationOrchestrator>());

// ===== FIREBASE / MEMORY SERVICE =====

builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection("Firebase"));
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (!string.IsNullOrWhiteSpace(firebaseProjectId))
{
    builder.Services.AddSingleton<UserMemoryService>();
    builder.Services.AddSingleton<IUserMemoryService>(sp => sp.GetRequiredService<UserMemoryService>());
}

// ===== COMMAND HANDLERS =====

builder.Services.AddTransient<ICommandHandler, StartCommandHandler>();
builder.Services.AddTransient<ICommandHandler, CancelCommandHandler>();
builder.Services.AddTransient<ICommandHandler, LoginCommandHandler>();
builder.Services.AddTransient<ICommandHandler, ContinueCommandHandler>();
builder.Services.AddTransient<ICommandHandler, SaveCommandHandler>();
builder.Services.AddTransient<ICommandHandler, SearchCommandHandler>();
builder.Services.AddTransient<ICommandHandler, PreferencesCommandHandler>();

// ===== STATE PROCESSORS =====

builder.Services.AddTransient<MealDescriptionProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<MealDescriptionProcessor>());

builder.Services.AddTransient<ClarificationProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<ClarificationProcessor>());

builder.Services.AddTransient<OcrCorrectionProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<OcrCorrectionProcessor>());

builder.Services.AddTransient<ConfirmationProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<ConfirmationProcessor>());

builder.Services.AddTransient<MemoryConfirmationProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<MemoryConfirmationProcessor>());

builder.Services.AddTransient<PreferenceActionProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<PreferenceActionProcessor>());

builder.Services.AddTransient<AliasInputProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<AliasInputProcessor>());

builder.Services.AddTransient<FoodSearchProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<FoodSearchProcessor>());

builder.Services.AddTransient<FoodSelectionProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<FoodSelectionProcessor>());

builder.Services.AddTransient<AliasDeleteConfirmProcessor>();
builder.Services.AddTransient<IStateProcessor>(sp => sp.GetRequiredService<AliasDeleteConfirmProcessor>());

// ===== HOSTED SERVICE (TELEGRAM POLLING) =====

builder.Services.AddHostedService<TelegramPollingService>();

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
