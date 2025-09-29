using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Models;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Requests;
using F23.StringSimilarity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CronometerLogMealApi.Controllers;

public class CronometerController : CronometerControllerBase
{
    private readonly CronometerHttpClient cronometerHttpClient;
    private readonly ILogger<CronometerController> logger;
    public CronometerController(CronometerHttpClient cronometerHttpClient, ILogger<CronometerController> logger)
    {
        this.cronometerHttpClient = cronometerHttpClient;
        this.logger = logger;
    }

    [HttpPost("Log-meal")]
    public async Task<IActionResult> LogMeal([FromHeader] AuthPayload auth, [FromBody] LogMealRequest request, CancellationToken cancellation)
    {
        int order = request.Category.ToLower() switch
        {
            "breakfast" => 65537,
            "lunch" => 131073,
            "dinner" => 196609,
            "snacks" => 262145,
            _ => 1
        };

        var date = request.Date;
        var userId = auth.UserId;
        var type = "Serving";

        var servingPayload = await GetServingPayloadFromRequest(order, date, userId, type, request.Items, auth, request.LogTime, cancellation);

        var result = await cronometerHttpClient.AddMultiServingAsync(servingPayload, cancellation);

        bool hasFailed = result != null &&
            result.Raw.ValueKind == JsonValueKind.Object &&
            result.Raw.TryGetProperty("result", out var resultProp) &&
            string.Equals(resultProp.GetString(), "fail", StringComparison.OrdinalIgnoreCase);

        if (hasFailed)
        {
            var jsonPayload = JsonSerializer.Serialize(servingPayload);

            logger.LogError("Failed to log meal. Payload: {Payload}, Response: {Response}", jsonPayload, result?.Raw.ToString());

            return BadRequest();
        }

        return Ok();
    }

    private async Task<AddMultiServingRequest> GetServingPayloadFromRequest(
        int order, 
        DateTime date, 
        long userId, 
        string type, 
        IEnumerable<MealItem> whatsappRequest, 
        AuthPayload auth, 
        bool? logTime = false,
        CancellationToken cancellation = default)
    {
        var result = new AddMultiServingRequest()
        {
            Servings = [],
            Auth = auth,
        };

        foreach (var itemRequest in whatsappRequest)
        {
            var itemToLogInCronometer = new ServingPayload()
            {
                Order = order,
                Day = date.ToString("yyyy-MM-dd"),
                Time = logTime == true ? date.ToString("HH:m:s") : string.Empty,
                UserId = userId,
                Type = type,
            };

            var foodId = await GetFoodId(itemRequest.Name, auth, cancellation);
            var food = (await cronometerHttpClient.GetFoodsAsync(new()
            {
                Ids = [foodId],
                Auth = auth,
            }, cancellation))
            .Foods?
            .FirstOrDefault();

            var measure = GetSimilarMeasureId(food?.Measures, itemRequest.Unit);

            itemToLogInCronometer.FoodId = foodId;
            itemToLogInCronometer.MeasureId = measure.Id;
            itemToLogInCronometer.Grams = measure.Value * itemRequest.Quantity;

            result.Servings.Add(itemToLogInCronometer);
        }

        return result;
    }

    private async Task<long> GetFoodId(string query, AuthPayload auth, CancellationToken cancellationToken)
    {
        var allResults = new List<Food>();

        var httpRequest = new FindFoodRequest()
        {
            Query = query,
            Tab = "CUSTOM", //CUSTOM | COMMON_FOODS | SUPPLEMENTS | FAVOURITES | ALL
            Auth = auth,
        };
        var customResult = (await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken)).Foods;
        if (customResult != null)
        {
            allResults.AddRange(customResult);
        }

        httpRequest.Tab = "FAVOURITES";
        var favouriteResult = (await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken)).Foods;
        if (favouriteResult != null)
        {
            allResults.AddRange(favouriteResult);
        }

        httpRequest.Tab = "COMMON_FOODS";
        var commonResult = (await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken)).Foods;
        if (commonResult != null)
        {
            allResults.AddRange(commonResult);
        }

        httpRequest.Tab = "SUPPLEMENTS";
        var supplementResult = (await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken)).Foods;
        if (supplementResult != null)
        {
            allResults.AddRange(supplementResult);
        }

        //httpRequest.Tab = "ALL";
        //var allResult = (await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken)).Foods?.FirstOrDefault();

        var dice = new SorensenDice();

        var bestMatch = allResults
            .Select(food => new
            {
                Food = food,
                Similarity = dice.Similarity(food.Name, query)
            })
            .OrderByDescending(x => x.Similarity)
            .FirstOrDefault();

        if (bestMatch != null && bestMatch.Similarity > 0.3) // threshold to avoid bad matches
        {
            return bestMatch.Food.Id;
        }

        return 0;
    }

    private static Measure GetSimilarMeasureId(IEnumerable<Measure>? measures, string measureName)
    {
        var defaultMeasure = new Measure()
        {
            Id = 1074000,
            Name = "g",
            Value = 1
        };

        if (measures == null || !measures.Any() || string.IsNullOrWhiteSpace(measureName))
        {
            return defaultMeasure;
        }

        var measure = measures.FirstOrDefault(m => string.Equals(m.Name, measureName, StringComparison.OrdinalIgnoreCase));
        if (measure != null)
        {
            return measure;
        }
        measure = measures.FirstOrDefault(m => m.Name.Contains(measureName, StringComparison.OrdinalIgnoreCase));
        if (measure != null)
        {
            return measure;
        }

        if(measureName == "grams" || measureName == "gram" || measureName == "gms" || measureName == "gm")
        {
            measure = measures.FirstOrDefault(m => m.Name.Equals("g", StringComparison.OrdinalIgnoreCase));
            if (measure != null)
            {
                return measure;
            }
        }

        return defaultMeasure; 
    }
}
