using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.TelegramClient;

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
builder.Services.AddHttpClient<TelegramHttpClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var botToken = cfg["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(botToken))
    {
        throw new InvalidOperationException("Telegram:BotToken is not configured.");
    }
    client.BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/");
});

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
