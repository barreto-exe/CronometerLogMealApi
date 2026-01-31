using CronometerLogMealApi.Models;
using CronometerLogMealApi.Requests;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Interface for orchestrating meal validation and logging.
/// </summary>
public interface IMealValidationOrchestrator
{
    /// <summary>
    /// Attempts to validate and log a meal.
    /// </summary>
    Task AttemptMealLoggingAsync(string chatId, CronometerUserInfo userInfo, LogMealRequest request, CancellationToken ct);
}
