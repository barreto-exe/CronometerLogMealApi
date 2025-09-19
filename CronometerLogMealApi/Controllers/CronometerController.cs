using CronometerLogMealApi.CronometerClient;
using CronometerLogMealApi.CronometerClient.Requests;
using CronometerLogMealApi.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class CronometerController : CronometerControllerBase
{
    private readonly CronometerHttpClient cronometerHttpClient;
    public CronometerController(CronometerHttpClient cronometerHttpClient)
    {
        this.cronometerHttpClient = cronometerHttpClient;
    }

    [HttpPost("Log-meal")]
    public async Task<IActionResult> LogMeal([FromHeader] AuthPayload auth, [FromBody] LogMealRequest request)
    {
        //1 Uncategorized | 65537 Breakfast | 131073 Lunch | 196609 Dinner | 262145 Snacks
        int order = request.Category.ToLower() switch
        {
            "breakfast" => 65537,
            "lunch" => 131073,
            "dinner" => 196609,
            "snacks" => 262145,
            _ => 1
        };

        var day = DateTime.Now;
        var userId = auth.UserId;
        var type = "Serving";



        return Ok();
    }
}
