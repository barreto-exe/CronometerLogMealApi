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
        var result = await cronometerService.LogMealAsync(auth, request, cancellation);
        
        if (result.Success)
        {
            return Ok(new { success = true });
        }

        if (result.NotFoundItems.Count > 0)
        {
            return BadRequest(new { 
                success = false, 
                notFoundItems = result.NotFoundItems,
                message = result.ErrorMessage 
            });
        }

        return BadRequest(new { 
            success = false, 
            message = result.ErrorMessage 
        });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok("Healthy");
}
