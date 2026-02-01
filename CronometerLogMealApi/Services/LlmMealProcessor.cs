using System.Diagnostics;
using System.Text.Json;
using CronometerLogMealApi.Abstractions;
using CronometerLogMealApi.Clients.GeminiClient;
using CronometerLogMealApi.Clients.OpenAIClient;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Requests;
using Microsoft.Extensions.Logging;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Processes meal descriptions using AI (LLM).
/// </summary>
public class LlmMealProcessor : IMealProcessor
{
    private readonly OpenAIHttpClient _openAIClient;
    private readonly ILogger<LlmMealProcessor> _logger;
    private readonly ISessionLogService? _sessionLogService;

    public LlmMealProcessor(
        OpenAIHttpClient openAIClient,
        ILogger<LlmMealProcessor> logger,
        ISessionLogService? sessionLogService = null)
    {
        _openAIClient = openAIClient;
        _logger = logger;
        _sessionLogService = sessionLogService;
    }

    public async Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, CancellationToken ct)
    {
        return await ProcessMealDescriptionAsync(text, null, null, ct);
    }

    public async Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, string? chatId, CancellationToken ct)
    {
        return await ProcessMealDescriptionAsync(text, chatId, null, ct);
    }

    public async Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, string? chatId, string? userPreferences, CancellationToken ct)
    {
        // Get Venezuela time
        var venezuelaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Venezuela Standard Time");
        var venezuelaNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, venezuelaTimeZone);

        var prompt = GeminiPrompts.CronometerPrompt;
        prompt = prompt.Replace("@Now", venezuelaNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        prompt = prompt.Replace("@UserInput", text);
        prompt = prompt.Replace("@UserPreferences", userPreferences ?? "No saved preferences for this user.");

        var sw = Stopwatch.StartNew();
        var openAIResponse = await _openAIClient.GenerateTextAsync(prompt, ct);
        sw.Stop();
        
        var foodInfo = openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(foodInfo))
        {
            if (chatId != null)
            {
                string logText = text + ' ' + userPreferences;
                _sessionLogService?.LogLlmCall(chatId, logText, null, sw.ElapsedMilliseconds, false);
            }
            return MealProcessingResult.Failed("No se pudo procesar el mensaje.");
        }

        _logger.LogInformation("LLM response: {Response}", foodInfo);

        var cleanedJson = CleanMarkdown(foodInfo);

        try
        {
            var response = JsonSerializer.Deserialize<LogMealRequestWithClarifications>(cleanedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (chatId != null)
            {
                string logText = text + ' ' + userPreferences;
                _sessionLogService?.LogLlmCall(chatId, logText, cleanedJson, sw.ElapsedMilliseconds, response != null);
            }

            if (response == null)
            {
                return MealProcessingResult.Failed("No se pudo interpretar la respuesta.");
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                return MealProcessingResult.Failed("No se pudo extraer información de comida del mensaje.");
            }

            var clarifications = response.Clarifications?
                .Select(c => new ClarificationItem
                {
                    Type = ParseClarificationType(c.Type),
                    ItemName = c.ItemName,
                    Question = c.Question
                })
                .ToList() ?? new List<ClarificationItem>();

            if (response.NeedsClarification && clarifications.Count > 0)
            {
                return MealProcessingResult.RequiresClarification(
                    new LogMealRequest
                    {
                        Category = response.Category,
                        Date = response.Date,
                        Items = response.Items,
                        LogTime = response.LogTime
                    },
                    clarifications);
            }

            return MealProcessingResult.Successful(new LogMealRequest
            {
                Category = response.Category,
                Date = response.Date,
                Items = response.Items,
                LogTime = response.LogTime
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response as JSON: {Response}", cleanedJson);
            return MealProcessingResult.Failed("Error al procesar la respuesta del asistente.");
        }
    }

    private static ClarificationType ParseClarificationType(string type)
    {
        var normalizedType = type?.Replace("_", "").ToUpperInvariant();
        
        return normalizedType switch
        {
            "MISSINGSIZE" => ClarificationType.MissingSize,
            "MISSINGWEIGHT" => ClarificationType.MissingWeight,
            "AMBIGUOUSUNIT" => ClarificationType.AmbiguousUnit,
            "UNCLEARFOOD" => ClarificationType.FoodNotFound,
            "FOODNOTFOUND" => ClarificationType.FoodNotFound,
            _ => ClarificationType.MissingWeight
        };
    }

    private static string CleanMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        if (text.StartsWith("```json"))
            text = text.Substring("```json".Length);
        if (text.StartsWith("```"))
            text = text.Substring("```".Length);
        if (text.EndsWith("```"))
            text = text.Substring(0, text.Length - "```".Length);

        return text.Replace("**", "")
                  .Replace("__", "")
                  .Replace("*", "")
                  .Replace("_", "")
                  .Replace("`", "")
                  .Trim();
    }
}
