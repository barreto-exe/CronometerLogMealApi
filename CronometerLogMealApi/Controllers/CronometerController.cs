using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Requests;
using CronometerLogMealApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class CronometerController : CronometerControllerBase
{
    private readonly CronometerService cronometerService;
    public CronometerController(CronometerService cronometerService)
    {
        this.cronometerService = cronometerService;
    }

    [HttpPost("Log-meal")]
    public async Task<IActionResult> LogMeal([FromHeader] AuthPayload auth, [FromBody] LogMealRequest request, CancellationToken cancellation)
    {
        var ok = await cronometerService.LogMealAsync(auth, request, cancellation);
        return ok ? Ok() : BadRequest();
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok("Healthy");
}
