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

    public LlmMealProcessor(
        OpenAIHttpClient openAIClient,
        ILogger<LlmMealProcessor> logger)
    {
        _openAIClient = openAIClient;
        _logger = logger;
    }

    public async Task<MealProcessingResult> ProcessMealDescriptionAsync(string text, CancellationToken ct)
    {
        // Get Venezuela time
        var venezuelaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Venezuela Standard Time");
        var venezuelaNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, venezuelaTimeZone);

        var prompt = GeminiPrompts.CronometerPrompt;
        prompt = prompt.Replace("@Now", venezuelaNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        prompt = prompt.Replace("@UserInput", text);

        var openAIResponse = await _openAIClient.GenerateTextAsync(prompt, ct);
        var foodInfo = openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(foodInfo))
        {
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
