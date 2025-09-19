using CronometerLogMealApi.CronometerClient;
using CronometerLogMealApi.CronometerClient.Requests;
using CronometerLogMealApi.Requests;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CronometerLogMealApi.Controllers;

public class CronometerController : CronometerControllerBase
{
    private readonly CronometerHttpClient cronometerHttpClient;
    public CronometerController(CronometerHttpClient cronometerHttpClient)
    {
        this.cronometerHttpClient = cronometerHttpClient;
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

        var date = DateTime.Now;
        var userId = auth.UserId;
        var type = "Serving";

        var servingPayload = GetServingPayloadFromRequest(order, date, userId, type, request.Items.FirstOrDefault(), auth, cancellation);

        return Ok();
    }

    private async Task<ServingPayload> GetServingPayloadFromRequest(int order, DateTime date, long userId, string type, MealItem request, AuthPayload auth, CancellationToken cancellation = default)
    {
        var result = new ServingPayload()
        {
            Order = order,
            Day = date.ToString("yyyy-MM-dd"),
            UserId = userId,
            Type = type,
        };

        var foodId = GetFoodId(request.Name, auth, cancellation);

        return result;
    }

    private async Task<string> GetFoodId(string query, AuthPayload auth, CancellationToken cancellationToken)
    {
        var httpRequest = new FindFoodRequest()
        {
            Query = query,
            Tab = "CUSTOM",
            Auth = auth,
        };

        var customResult = await cronometerHttpClient.FindFoodAsync(httpRequest, cancellationToken);

        return string.Empty;
    }
}
