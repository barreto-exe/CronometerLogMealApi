using CronometerLogMealApi.Clients.CronometerClient;
using CronometerLogMealApi.Clients.CronometerClient.Models;
using CronometerLogMealApi.Clients.CronometerClient.Requests;
using CronometerLogMealApi.Models;
using CronometerLogMealApi.Models.UserMemory;
using CronometerLogMealApi.Requests;
using F23.StringSimilarity;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CronometerLogMealApi.Services;

public class CronometerService
{
    private readonly CronometerHttpClient cronometerHttpClient;
    private readonly ILogger<CronometerService> logger;
    private readonly UserMemoryService? _memoryService;

    public CronometerService(
        CronometerHttpClient cronometerHttpClient, 
        ILogger<CronometerService> logger,
        UserMemoryService? memoryService = null)
    {
        this.cronometerHttpClient = cronometerHttpClient;
        this.logger = logger;
        this._memoryService = memoryService;
    }

    /// <summary>
    /// Logs a meal to Cronometer based on the given request/auth. Returns detailed result.
    /// </summary>
    public async Task<LogMealResult> LogMealAsync(AuthPayload auth, LogMealRequest request, CancellationToken cancellation = default)
    {
        //log auth
        logger.LogInformation("Logging meal for userId {UserId} with sessionKey {SessionKey}", auth.UserId, auth.Token);

        //log request
        logger.LogInformation("LogMealRequest: {Request}", JsonSerializer.Serialize(request));

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

        var (servingPayload, notFoundItems) = await GetServingPayloadFromRequest(order, date, userId, type, request.Items, auth, request.LogTime, cancellation);

        // If there are items not found, return early with that information
        if (notFoundItems.Any())
        {
            logger.LogWarning("Some food items were not found: {Items}", string.Join(", ", notFoundItems));
            return LogMealResult.NotFound(notFoundItems);
        }

        var result = await cronometerHttpClient.AddMultiServingAsync(servingPayload, cancellation);

        bool hasFailed = result != null &&
            result.Raw.ValueKind == JsonValueKind.Object &&
            result.Raw.TryGetProperty("result", out var resultProp) &&
            string.Equals(resultProp.GetString(), "fail", StringComparison.OrdinalIgnoreCase);

        if (hasFailed)
        {
            var jsonPayload = JsonSerializer.Serialize(servingPayload);
            logger.LogError("Failed to log meal. Payload: {Payload}, Response: {Response}", jsonPayload, result?.Raw.ToString());
            return LogMealResult.Failed("Error al registrar la comida en Cronometer.");
        }

        logger.LogInformation("Successfully logged meal. Response: {Response}", result?.Raw.ToString());

        return LogMealResult.Successful();
    }

    private async Task<(AddMultiServingRequest request, List<string> notFoundItems)> GetServingPayloadFromRequest(
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

        var notFoundItems = new List<string>();

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
            
            // Track items that were not found
            if (foodId == 0)
            {
                notFoundItems.Add(itemRequest.Name);
                continue; // Skip this item, don't add to servings
            }

            var food = (await cronometerHttpClient.GetFoodsAsync(new()
            {
                Ids = [foodId],
                Auth = auth,
            }, cancellation))
            .Foods?
            .FirstOrDefault();

            var (measure, isRawGrams) = GetSimilarMeasureId(food?.Measures, itemRequest.Unit);

            itemToLogInCronometer.FoodId = foodId;
            itemToLogInCronometer.MeasureId = measure.Id;
            
            // If isRawGrams is true, the user specified grams but the food doesn't have a "g" measure
            // In this case, we need to calculate the correct gram amount
            // The quantity represents raw grams, so we just use it directly as Grams
            if (isRawGrams)
            {
                // The user wants X grams, so we set Grams = X (the quantity they specified)
                itemToLogInCronometer.Grams = itemRequest.Quantity;
            }
            else
            {
                // Normal case: quantity * measure value = total grams
                itemToLogInCronometer.Grams = measure.Value * itemRequest.Quantity;
            }

            result.Servings.Add(itemToLogInCronometer);
        }

        return (result, notFoundItems);
    }

    private async Task<long> GetFoodId(string query, AuthPayload auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;

        logger.LogInformation("🔍 Starting food search for: '{Query}'", query);

        // Normalize the query for better matching
        var normalizedQuery = NormalizeSearchQuery(query);
        
        // Define search tabs with their priority weights (higher = better)
        // CUSTOM and FAVOURITES get much higher priority since they're user-specific
        var tabsWithPriority = new (string Tab, double PriorityWeight)[]
        {
            ("CUSTOM", 3.0),       // User's custom foods - HIGHEST priority
            ("FAVOURITES", 2.5),   // User's favorites - very high priority  
            ("COMMON_FOODS", 1.0), // Common foods - normal priority
            ("SUPPLEMENTS", 0.5),  // Supplements - lower priority
            ("ALL", 0.4)           // Fallback - lowest priority
        };

        // Execute all searches in parallel for better performance
        var searchTasks = tabsWithPriority.Select(async t =>
        {
            var request = new FindFoodRequest
            {
                Query = query,
                Tab = t.Tab,
                Auth = auth
            };

            try
            {
                var response = await cronometerHttpClient.FindFoodAsync(request, cancellationToken);
                // Take top 5 results from each tab for better matching
                var foods = response.Foods?.Take(5).ToList() ?? new List<Food>();
                
                logger.LogInformation("  📂 Tab {Tab}: Found {Count} results", t.Tab, foods.Count);
                foreach (var food in foods.Take(3))
                {
                    logger.LogInformation("    - '{FoodName}' (ID: {FoodId})", food.Name, food.Id);
                }
                
                return (Tab: t.Tab, Priority: t.PriorityWeight, Foods: foods);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to search in tab {Tab}", t.Tab);
                return (Tab: t.Tab, Priority: t.PriorityWeight, Foods: new List<Food>());
            }
        });

        var searchResults = await Task.WhenAll(searchTasks);

        // Flatten all results with their source priority
        var allCandidates = searchResults
            .SelectMany(r => r.Foods.Select(food => new
            {
                Food = food,
                SourceTab = r.Tab,
                SourcePriority = r.Priority
            }))
            .ToList();

        if (!allCandidates.Any())
        {
            logger.LogWarning("❌ No food found for query: {Query}", query);
            return 0;
        }

        // Calculate composite score for each candidate
        var dice = new SorensenDice();
        var scoredCandidates = allCandidates
            .Select(c =>
            {
                var normalizedFoodName = NormalizeSearchQuery(c.Food.Name);
                
                // Calculate similarity score (0-1)
                var similarityScore = dice.Similarity(normalizedFoodName, normalizedQuery);
                
                // Also calculate similarity with original (non-normalized) names for exact matches
                var originalSimilarity = dice.Similarity(c.Food.Name.ToLowerInvariant(), query.ToLowerInvariant());
                
                // Use the better of the two similarity scores
                var bestSimilarity = Math.Max(similarityScore, originalSimilarity);
                
                // Bonus for exact match (case-insensitive) - HUGE bonus
                var exactMatchBonus = string.Equals(c.Food.Name, query, StringComparison.OrdinalIgnoreCase) 
                    ? 10.0 : 0.0;
                
                // Bonus for exact match after normalization
                var normalizedExactBonus = string.Equals(normalizedFoodName, normalizedQuery, StringComparison.OrdinalIgnoreCase) 
                    ? 5.0 : 0.0;
                
                // Bonus for starts-with match (original query)
                var startsWithBonus = c.Food.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) 
                    ? 2.0 : 0.0;
                
                // Bonus for contains match (original query)
                var containsBonus = c.Food.Name.Contains(query, StringComparison.OrdinalIgnoreCase) 
                    ? 1.0 : 0.0;

                // Composite score: (similarity * priority) + all bonuses
                var compositeScore = (bestSimilarity * c.SourcePriority) + exactMatchBonus + normalizedExactBonus + startsWithBonus + containsBonus;

                return new
                {
                    c.Food,
                    c.SourceTab,
                    c.SourcePriority,
                    SimilarityScore = bestSimilarity,
                    CompositeScore = compositeScore,
                    ExactMatchBonus = exactMatchBonus,
                    NormalizedExactBonus = normalizedExactBonus,
                    StartsWithBonus = startsWithBonus,
                    ContainsBonus = containsBonus
                };
            })
            .OrderByDescending(x => x.CompositeScore)
            .ThenByDescending(x => x.SimilarityScore)
            .ToList();

        // Log top 5 candidates for debugging
        logger.LogInformation("📊 Top candidates for '{Query}':", query);
        foreach (var candidate in scoredCandidates.Take(5))
        {
            logger.LogInformation(
                "  [{Tab}] '{Name}' | Score: {Score:F2} = (sim:{Sim:F2} × pri:{Pri:F1}) + exact:{Exact:F1} + normExact:{NormExact:F1} + starts:{Starts:F1} + contains:{Contains:F1}",
                candidate.SourceTab,
                candidate.Food.Name,
                candidate.CompositeScore,
                candidate.SimilarityScore,
                candidate.SourcePriority,
                candidate.ExactMatchBonus,
                candidate.NormalizedExactBonus,
                candidate.StartsWithBonus,
                candidate.ContainsBonus);
        }

        var bestMatch = scoredCandidates.FirstOrDefault();

        if (bestMatch == null)
        {
            return 0;
        }

        // Log the selection
        logger.LogInformation(
            "✅ Selected: '{FoodName}' (ID: {FoodId}) from {Tab} with score {Score:F3}",
            bestMatch.Food.Name, bestMatch.Food.Id, bestMatch.SourceTab, bestMatch.CompositeScore);

        // Return best match if score is acceptable
        if (bestMatch.CompositeScore < 0.2)
        {
            logger.LogWarning("⚠️ Best match for '{Query}' has too low score ({Score:F3}), returning no match", 
                query, bestMatch.CompositeScore);
            return 0;
        }

        return bestMatch.Food.Id;
    }

    /// <summary>
    /// Normalizes a search query or food name for better matching.
    /// Removes common noise words, extra spaces, and standardizes format.
    /// </summary>
    private static string NormalizeSearchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Convert to lowercase and trim
        var normalized = input.Trim().ToLowerInvariant();

        // Remove punctuation that might interfere with matching
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[,\.\-_]", " ");

        // Remove common noise words that don't help with matching
        var noiseWords = new[] { "de", "con", "the", "a", "an", "and", "y", "or", "o", "en", "in" };
        foreach (var word in noiseWords)
        {
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                $@"\b{word}\b", 
                " ", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Remove extra whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Validates a list of meal items against Cronometer's database.
    /// Returns a list of validated items with exact DB info, and a list of not found items.
    /// </summary>
    public async Task<(List<ValidatedMealItem> ValidatedItems, List<string> NotFoundItems)> ValidateMealItemsAsync(
        IEnumerable<MealItem> items,
        AuthPayload auth,
        string? userId = null,
        List<DetectedAlias>? detectedAliases = null,
        string? originalUserInput = null,
        CancellationToken cancellationToken = default)
    {
        var validatedItems = new List<ValidatedMealItem>();
        var notFoundItems = new List<string>();

        foreach (var itemRequest in items)
        {
            // FIRST: Check if any pre-detected alias matches this item
            FoodSearchResult? searchResult = null;
            
            if (detectedAliases != null && detectedAliases.Count > 0 && _memoryService != null && !string.IsNullOrEmpty(originalUserInput))
            {
                var matchingAlias = _memoryService.FindMatchingDetectedAlias(
                    itemRequest.Name, 
                    detectedAliases,
                    originalUserInput);
                
                if (matchingAlias != null)
                {
                    logger.LogInformation(
                        "✨ Pre-detected alias matched! LLM output '{LlmName}' -> alias '{AliasTerm}' -> '{ResolvedName}' (ID: {Id})",
                        itemRequest.Name,
                        matchingAlias.InputTerm,
                        matchingAlias.ResolvedFoodName,
                        matchingAlias.ResolvedFoodId);

                    // Increment usage
                    await _memoryService.IncrementAliasUsageAsync(matchingAlias.Id, cancellationToken);

                    searchResult = new FoodSearchResult
                    {
                        FoodId = matchingAlias.ResolvedFoodId,
                        FoodName = matchingAlias.ResolvedFoodName,
                        SourceTab = matchingAlias.SourceTab,
                        WasFromAlias = true,
                        AliasId = matchingAlias.Id
                    };
                }
            }

            // If no pre-detected alias matched, use the regular search (which also checks aliases)
            searchResult ??= await GetFoodWithMemoryAsync(itemRequest.Name, auth, userId, cancellationToken);
            
            if (searchResult.FoodId == 0)
            {
                notFoundItems.Add(itemRequest.Name);
                continue;
            }

            var food = (await cronometerHttpClient.GetFoodsAsync(new()
            {
                Ids = [searchResult.FoodId],
                Auth = auth,
            }, cancellationToken))
            .Foods?
            .FirstOrDefault();

            if (food == null)
            {
                notFoundItems.Add(itemRequest.Name);
                continue;
            }

            var (measure, isRawGrams) = GetSimilarMeasureId(food.Measures, itemRequest.Unit);

            validatedItems.Add(new ValidatedMealItem
            {
                OriginalName = itemRequest.Name,
                FoodName = food.Name,
                FoodId = food.Id,
                Quantity = itemRequest.Quantity,
                MeasureName = isRawGrams ? "g" : measure.Name,
                MeasureId = measure.Id,
                MeasureGrams = measure.Value,
                IsRawGrams = isRawGrams,
                SourceTab = searchResult.SourceTab,
                WasResolvedFromAlias = searchResult.WasFromAlias,
                AliasId = searchResult.AliasId
            });
        }

        return (validatedItems, notFoundItems);
    }

    /// <summary>
    /// Result of a food search operation.
    /// </summary>
    public class FoodSearchResult
    {
        public long FoodId { get; set; }
        public string FoodName { get; set; } = string.Empty;
        public string SourceTab { get; set; } = string.Empty;
        public bool WasFromAlias { get; set; }
        public string? AliasId { get; set; }
        public List<SearchCandidate> AllCandidates { get; set; } = new();
    }

    /// <summary>
    /// Searches for a food with memory (alias) support.
    /// First checks user aliases, then falls back to API search.
    /// </summary>
    public async Task<FoodSearchResult> GetFoodWithMemoryAsync(
        string query,
        AuthPayload auth,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new FoodSearchResult();

        logger.LogInformation("🔍 Starting food search for: '{Query}' (user: {UserId})", query, userId ?? "anonymous");

        // 1. Check user memory first if available
        if (!string.IsNullOrWhiteSpace(userId) && _memoryService != null)
        {
            try
            {
                var alias = await _memoryService.FindAliasAsync(userId, query, cancellationToken);
                if (alias != null)
                {
                    logger.LogInformation("✨ Found alias! '{Query}' -> '{ResolvedName}' (ID: {Id})",
                        query, alias.ResolvedFoodName, alias.ResolvedFoodId);

                    // Increment usage
                    await _memoryService.IncrementAliasUsageAsync(alias.Id, cancellationToken);

                    return new FoodSearchResult
                    {
                        FoodId = alias.ResolvedFoodId,
                        FoodName = alias.ResolvedFoodName,
                        SourceTab = alias.SourceTab,
                        WasFromAlias = true,
                        AliasId = alias.Id
                    };
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error checking user memory, falling back to API search");
            }
        }

        // 2. Fall back to API search
        var (foodId, foodName, sourceTab, candidates) = await SearchFoodWithCandidatesAsync(query, auth, cancellationToken);

        return new FoodSearchResult
        {
            FoodId = foodId,
            FoodName = foodName,
            SourceTab = sourceTab,
            WasFromAlias = false,
            AllCandidates = candidates
        };
    }

    /// <summary>
    /// Searches for food and returns top candidates for potential selection.
    /// </summary>
    public async Task<(long FoodId, string FoodName, string SourceTab, List<SearchCandidate> Candidates)> 
        SearchFoodWithCandidatesAsync(string query, AuthPayload auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (0, string.Empty, string.Empty, new List<SearchCandidate>());

        var normalizedQuery = NormalizeSearchQuery(query);
        
        // Define search tabs with their priority weights
        var tabsWithPriority = new (string Tab, double PriorityWeight)[]
        {
            ("CUSTOM", 3.0),
            ("FAVOURITES", 2.5),
            ("COMMON_FOODS", 1.0),
            ("SUPPLEMENTS", 0.5),
            ("ALL", 0.4)
        };

        // Execute all searches in parallel
        var searchTasks = tabsWithPriority.Select(async t =>
        {
            var request = new FindFoodRequest
            {
                Query = query,
                Tab = t.Tab,
                Auth = auth
            };

            try
            {
                var response = await cronometerHttpClient.FindFoodAsync(request, cancellationToken);
                var foods = response.Foods?.Take(5).ToList() ?? new List<Food>();
                
                logger.LogInformation("  📂 Tab {Tab}: Found {Count} results", t.Tab, foods.Count);
                
                return (Tab: t.Tab, Priority: t.PriorityWeight, Foods: foods);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to search in tab {Tab}", t.Tab);
                return (Tab: t.Tab, Priority: t.PriorityWeight, Foods: new List<Food>());
            }
        });

        var searchResults = await Task.WhenAll(searchTasks);

        // Score all candidates
        var dice = new SorensenDice();
        var scoredCandidates = searchResults
            .SelectMany(r => r.Foods.Select(food => new
            {
                Food = food,
                SourceTab = r.Tab,
                SourcePriority = r.Priority
            }))
            .Select(c =>
            {
                var normalizedFoodName = NormalizeSearchQuery(c.Food.Name);
                var similarityScore = dice.Similarity(normalizedFoodName, normalizedQuery);
                var originalSimilarity = dice.Similarity(c.Food.Name.ToLowerInvariant(), query.ToLowerInvariant());
                var bestSimilarity = Math.Max(similarityScore, originalSimilarity);
                
                var exactMatchBonus = string.Equals(c.Food.Name, query, StringComparison.OrdinalIgnoreCase) ? 10.0 : 0.0;
                var normalizedExactBonus = string.Equals(normalizedFoodName, normalizedQuery, StringComparison.OrdinalIgnoreCase) ? 5.0 : 0.0;
                var startsWithBonus = c.Food.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 2.0 : 0.0;
                var containsBonus = c.Food.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

                var compositeScore = (bestSimilarity * c.SourcePriority) + exactMatchBonus + normalizedExactBonus + startsWithBonus + containsBonus;

                return new SearchCandidate
                {
                    Food = c.Food,
                    SourceTab = c.SourceTab,
                    Score = compositeScore,
                    SimilarityScore = bestSimilarity
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.SimilarityScore)
            .ToList();

        // Log top candidates
        logger.LogInformation("📊 Top candidates for '{Query}':", query);
        foreach (var candidate in scoredCandidates.Take(5))
        {
            logger.LogInformation("  [{Tab}] '{Name}' | Score: {Score:F2}",
                candidate.SourceTab, candidate.Food.Name, candidate.Score);
        }

        var bestMatch = scoredCandidates.FirstOrDefault();

        if (bestMatch == null || bestMatch.Score < 0.2)
        {
            logger.LogWarning("❌ No good match found for '{Query}'", query);
            return (0, string.Empty, string.Empty, scoredCandidates.Take(10).ToList());
        }

        logger.LogInformation("✅ Selected: '{FoodName}' (ID: {FoodId}) from {Tab}",
            bestMatch.Food.Name, bestMatch.Food.Id, bestMatch.SourceTab);

        return (bestMatch.Food.Id, bestMatch.Food.Name, bestMatch.SourceTab, scoredCandidates.Take(10).ToList());
    }

    /// <summary>
    /// Finds the best matching measure for the given unit name.
    /// Returns a tuple with the measure and whether the quantity needs to be treated as raw grams.
    /// </summary>
    /// <param name="measures">Available measures for the food</param>
    /// <param name="measureName">The unit name from user input (e.g., "grams", "g", "cup", "large")</param>
    /// <returns>
    /// A tuple containing:
    /// - Measure: The matched measure (or a gram-based fallback)
    /// - IsRawGrams: If true, the quantity should be divided by MeasureGrams to get the correct serving count
    /// </returns>
    private static (Measure Measure, bool IsRawGrams) GetSimilarMeasureId(IEnumerable<Measure>? measures, string measureName)
    {
        var defaultMeasure = new Measure()
        {
            Id = 1074000,
            Name = "g",
            Value = 1
        };

        if (measures == null || !measures.Any() || string.IsNullOrWhiteSpace(measureName))
        {
            return (defaultMeasure, false);
        }

        // Normalize the measure name for comparison
        var normalizedName = measureName.Trim().ToLowerInvariant();
        var isGramRequest = normalizedName == "grams" || normalizedName == "gram" || 
                           normalizedName == "gms" || normalizedName == "gm" || normalizedName == "g";

        // 1. Try exact match first
        var measure = measures.FirstOrDefault(m => string.Equals(m.Name, measureName, StringComparison.OrdinalIgnoreCase));
        if (measure != null)
        {
            return (measure, false);
        }

        // 2. For gram requests, prioritize finding a pure "g" measure
        if (isGramRequest)
        {
            measure = measures.FirstOrDefault(m => m.Name.Equals("g", StringComparison.OrdinalIgnoreCase));
            if (measure != null)
            {
                return (measure, false);
            }

            // 3. If no pure "g" measure, find any gram-based measure and flag for raw gram conversion
            // This handles cases like "100g", "50g", etc.
            measure = measures.FirstOrDefault(m => 
                System.Text.RegularExpressions.Regex.IsMatch(m.Name, @"^\d+\s*g$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            if (measure != null)
            {
                // Return this measure but flag that we need to convert the quantity
                // The caller should divide the requested grams by the measure's gram value
                return (measure, true);
            }

            // 4. Look for any measure containing "g" that represents grams
            measure = measures.FirstOrDefault(m => 
                m.Name.EndsWith("g", StringComparison.OrdinalIgnoreCase) && 
                !m.Name.Contains("serving", StringComparison.OrdinalIgnoreCase));
            if (measure != null)
            {
                return (measure, true);
            }

            // 5. If still no gram measure found, use the first available measure and convert
            // This is a last resort - we'll use whatever measure exists and calculate based on its gram value
            var firstMeasure = measures.FirstOrDefault();
            if (firstMeasure != null && firstMeasure.Value > 0)
            {
                return (firstMeasure, true);
            }
        }

        // 3. Try contains match for non-gram requests
        measure = measures.FirstOrDefault(m => m.Name.Contains(measureName, StringComparison.OrdinalIgnoreCase));
        if (measure != null)
        {
            return (measure, false);
        }

        // 4. Try if the measure name contains the requested name
        measure = measures.FirstOrDefault(m => measureName.Contains(m.Name, StringComparison.OrdinalIgnoreCase));
        if (measure != null)
        {
            return (measure, false);
        }

        return (defaultMeasure, false);
    }

    /// <summary>
    /// Static version of GetSimilarMeasureId for use in other services.
    /// </summary>
    public static (Measure Measure, bool IsRawGrams) GetSimilarMeasureIdStatic(IEnumerable<Measure>? measures, string measureName)
    {
        return GetSimilarMeasureId(measures, measureName);
    }
}
