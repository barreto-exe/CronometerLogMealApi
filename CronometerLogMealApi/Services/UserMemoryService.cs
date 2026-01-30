using CronometerLogMealApi.Models.UserMemory;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CronometerLogMealApi.Services;

/// <summary>
/// Service for managing user memory (aliases and preferences) using Firebase Firestore.
/// </summary>
public class UserMemoryService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<UserMemoryService> _logger;

    private const string AliasesCollection = "foodAliases";
    private const string PreferencesCollection = "measurePreferences";
    private const string SessionsCollection = "cronometerSessions";

    public UserMemoryService(IOptions<FirebaseOptions> options, ILogger<UserMemoryService> logger)
    {
        _logger = logger;

        var firebaseOptions = options.Value;

        if (string.IsNullOrWhiteSpace(firebaseOptions.ProjectId))
        {
            throw new ArgumentException("Firebase ProjectId is required.");
        }

        // Build Firestore client based on available credentials
        if (firebaseOptions.HasInlineCredentials)
        {
            // Use inline credentials from config
            _db = CreateDbFromInlineCredentials(firebaseOptions);
            _logger.LogInformation("UserMemoryService initialized with inline credentials for project: {ProjectId}", firebaseOptions.ProjectId);
        }
        else if (!string.IsNullOrWhiteSpace(firebaseOptions.CredentialsPath))
        {
            // Use explicit credentials file
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", firebaseOptions.CredentialsPath);
            _db = FirestoreDb.Create(firebaseOptions.ProjectId);
            _logger.LogInformation("UserMemoryService initialized with credentials file for project: {ProjectId}", firebaseOptions.ProjectId);
        }
        else
        {
            // Try Application Default Credentials
            _db = FirestoreDb.Create(firebaseOptions.ProjectId);
            _logger.LogInformation("UserMemoryService initialized with ADC for project: {ProjectId}", firebaseOptions.ProjectId);
        }
    }

    private static FirestoreDb CreateDbFromInlineCredentials(FirebaseOptions options)
    {
        // Normalize the private key (replace literal \n with actual newlines)
        var privateKey = options.PrivateKey.Replace("\\n", "\n");

        // Create credential from service account info
        var credential = GoogleCredential.FromServiceAccountCredential(
            new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(options.ClientEmail)
                {
                    ProjectId = options.ProjectId
                }.FromPrivateKey(privateKey)));

        // Create Firestore client with the credential
        var clientBuilder = new FirestoreClientBuilder
        {
            ChannelCredentials = credential.ToChannelCredentials()
        };

        return FirestoreDb.Create(options.ProjectId, clientBuilder.Build());
    }

    #region Food Aliases

    /// <summary>
    /// Finds an active alias for the given input term and user.
    /// </summary>
    public async Task<FoodAlias?> FindAliasAsync(string userId, string inputTerm, CancellationToken ct = default)
    {
        var normalizedTerm = NormalizeTerm(inputTerm);

        var query = _db.Collection(AliasesCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("inputTerm", normalizedTerm)
            .WhereEqualTo("isActive", true)
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var doc = snapshot.Documents.FirstOrDefault();

        if (doc == null)
        {
            _logger.LogDebug("No alias found for user {UserId}, term '{Term}'", userId, normalizedTerm);
            return null;
        }

        var alias = doc.ConvertTo<FoodAlias>();
        alias.Id = doc.Id;

        _logger.LogInformation("Found alias for '{Term}' -> '{ResolvedName}' (used {Count} times)",
            normalizedTerm, alias.ResolvedFoodName, alias.UseCount);

        return alias;
    }

    /// <summary>
    /// Gets all active aliases for a user.
    /// </summary>
    public async Task<List<FoodAlias>> GetUserAliasesAsync(string userId, CancellationToken ct = default)
    {
        var query = _db.Collection(AliasesCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("isActive", true)
            .OrderByDescending("useCount");

        var snapshot = await query.GetSnapshotAsync(ct);

        return snapshot.Documents
            .Select(doc =>
            {
                var alias = doc.ConvertTo<FoodAlias>();
                alias.Id = doc.Id;
                return alias;
            })
            .ToList();
    }

    /// <summary>
    /// Creates or updates an alias for a user.
    /// If an alias already exists for the same inputTerm, it will be updated (alias competition).
    /// </summary>
    public async Task<FoodAlias> SaveAliasAsync(
        string userId,
        string inputTerm,
        string resolvedFoodName,
        long resolvedFoodId,
        string sourceTab,
        bool isManual = false,
        CancellationToken ct = default)
    {
        var normalizedTerm = NormalizeTerm(inputTerm);

        // Check for existing alias
        var existingAlias = await FindAliasAsync(userId, normalizedTerm, ct);

        if (existingAlias != null)
        {
            // Update existing alias (alias competition)
            var docRef = _db.Collection(AliasesCollection).Document(existingAlias.Id);

            // If resolved food is different, this is a "habit change"
            if (existingAlias.ResolvedFoodId != resolvedFoodId)
            {
                _logger.LogInformation(
                    "Alias competition: '{Term}' changing from '{OldFood}' to '{NewFood}'",
                    normalizedTerm, existingAlias.ResolvedFoodName, resolvedFoodName);

                existingAlias.ResolvedFoodName = resolvedFoodName;
                existingAlias.ResolvedFoodId = resolvedFoodId;
                existingAlias.SourceTab = sourceTab;
                existingAlias.UseCount = 1; // Reset count for new mapping
                existingAlias.IsManual = isManual;
            }
            else
            {
                // Same food, just increment usage
                existingAlias.UseCount++;
            }

            existingAlias.LastUsedAt = DateTime.UtcNow;

            await docRef.SetAsync(existingAlias, SetOptions.Overwrite, ct);
            _logger.LogInformation("Updated alias: '{Term}' -> '{Food}' (count: {Count})",
                normalizedTerm, existingAlias.ResolvedFoodName, existingAlias.UseCount);

            return existingAlias;
        }

        // Create new alias
        var newAlias = new FoodAlias
        {
            UserId = userId,
            InputTerm = normalizedTerm,
            ResolvedFoodName = resolvedFoodName,
            ResolvedFoodId = resolvedFoodId,
            SourceTab = sourceTab,
            UseCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true,
            IsManual = isManual
        };

        var newDocRef = await _db.Collection(AliasesCollection).AddAsync(newAlias, ct);
        newAlias.Id = newDocRef.Id;

        _logger.LogInformation("Created new alias: '{Term}' -> '{Food}'",
            normalizedTerm, resolvedFoodName);

        return newAlias;
    }

    /// <summary>
    /// Increments the use count for an existing alias.
    /// </summary>
    public async Task IncrementAliasUsageAsync(string aliasId, CancellationToken ct = default)
    {
        var docRef = _db.Collection(AliasesCollection).Document(aliasId);

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "useCount", FieldValue.Increment(1) },
            { "lastUsedAt", DateTime.UtcNow }
        }, cancellationToken: ct);

        _logger.LogDebug("Incremented usage for alias {AliasId}", aliasId);
    }

    /// <summary>
    /// Deactivates (soft deletes) an alias.
    /// </summary>
    public async Task DeleteAliasAsync(string aliasId, CancellationToken ct = default)
    {
        var docRef = _db.Collection(AliasesCollection).Document(aliasId);

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "isActive", false }
        }, cancellationToken: ct);

        _logger.LogInformation("Deactivated alias {AliasId}", aliasId);
    }

    #endregion

    #region Measure Preferences

    /// <summary>
    /// Finds a measure preference for a food name pattern.
    /// </summary>
    public async Task<MeasurePreference?> FindMeasurePreferenceAsync(
        string userId,
        string foodName,
        CancellationToken ct = default)
    {
        var normalizedFood = NormalizeTerm(foodName);

        // First try exact match
        var query = _db.Collection(PreferencesCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("isActive", true);

        var snapshot = await query.GetSnapshotAsync(ct);

        // Find best matching preference
        MeasurePreference? bestMatch = null;
        int bestMatchLength = 0;

        foreach (var doc in snapshot.Documents)
        {
            var pref = doc.ConvertTo<MeasurePreference>();
            pref.Id = doc.Id;

            var pattern = NormalizeTerm(pref.FoodNamePattern);

            // Check if food name contains the pattern or vice versa
            if (normalizedFood.Contains(pattern) || pattern.Contains(normalizedFood))
            {
                if (pattern.Length > bestMatchLength)
                {
                    bestMatch = pref;
                    bestMatchLength = pattern.Length;
                }
            }
        }

        if (bestMatch != null)
        {
            _logger.LogInformation("Found measure preference for '{Food}': {Unit}",
                foodName, bestMatch.PreferredUnit);
        }

        return bestMatch;
    }

    /// <summary>
    /// Gets all active measure preferences for a user.
    /// </summary>
    public async Task<List<MeasurePreference>> GetUserPreferencesAsync(string userId, CancellationToken ct = default)
    {
        var query = _db.Collection(PreferencesCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("isActive", true)
            .OrderByDescending("useCount");

        var snapshot = await query.GetSnapshotAsync(ct);

        return snapshot.Documents
            .Select(doc =>
            {
                var pref = doc.ConvertTo<MeasurePreference>();
                pref.Id = doc.Id;
                return pref;
            })
            .ToList();
    }

    /// <summary>
    /// Saves a measure preference.
    /// </summary>
    public async Task<MeasurePreference> SaveMeasurePreferenceAsync(
        string userId,
        string foodNamePattern,
        string preferredUnit,
        double? preferredQuantity = null,
        CancellationToken ct = default)
    {
        var normalizedPattern = NormalizeTerm(foodNamePattern);

        // Check for existing preference with same pattern
        var query = _db.Collection(PreferencesCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("foodNamePattern", normalizedPattern)
            .WhereEqualTo("isActive", true)
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var existingDoc = snapshot.Documents.FirstOrDefault();

        if (existingDoc != null)
        {
            var existing = existingDoc.ConvertTo<MeasurePreference>();
            existing.Id = existingDoc.Id;
            existing.PreferredUnit = preferredUnit;
            existing.PreferredQuantity = preferredQuantity;
            existing.LastUsedAt = DateTime.UtcNow;
            existing.UseCount++;

            await existingDoc.Reference.SetAsync(existing, SetOptions.Overwrite, ct);

            _logger.LogInformation("Updated measure preference: '{Pattern}' -> {Unit}",
                normalizedPattern, preferredUnit);

            return existing;
        }

        // Create new preference
        var newPref = new MeasurePreference
        {
            UserId = userId,
            FoodNamePattern = normalizedPattern,
            PreferredUnit = preferredUnit,
            PreferredQuantity = preferredQuantity,
            UseCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true
        };

        var newDocRef = await _db.Collection(PreferencesCollection).AddAsync(newPref, ct);
        newPref.Id = newDocRef.Id;

        _logger.LogInformation("Created new measure preference: '{Pattern}' -> {Unit}",
            normalizedPattern, preferredUnit);

        return newPref;
    }

    /// <summary>
    /// Deactivates a measure preference.
    /// </summary>
    public async Task DeleteMeasurePreferenceAsync(string preferenceId, CancellationToken ct = default)
    {
        var docRef = _db.Collection(PreferencesCollection).Document(preferenceId);

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "isActive", false }
        }, cancellationToken: ct);

        _logger.LogInformation("Deactivated measure preference {PreferenceId}", preferenceId);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Normalizes a term for consistent matching.
    /// </summary>
    private static string NormalizeTerm(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().ToLowerInvariant();
    }

    #endregion

    #region Cronometer Sessions

    /// <summary>
    /// Saves or updates a Cronometer session for a Telegram user.
    /// </summary>
    public async Task SaveSessionAsync(
        string telegramChatId,
        long cronometerUserId,
        string sessionKey,
        string email,
        CancellationToken ct = default)
    {
        // Check for existing session
        var existingSession = await GetSessionAsync(telegramChatId, ct);

        if (existingSession != null)
        {
            // Update existing session
            var docRef = _db.Collection(SessionsCollection).Document(existingSession.Id);

            existingSession.CronometerUserId = cronometerUserId;
            existingSession.SessionKey = sessionKey;
            existingSession.Email = email;
            existingSession.LastUpdatedAt = DateTime.UtcNow;
            existingSession.IsActive = true;

            await docRef.SetAsync(existingSession, SetOptions.Overwrite, ct);
            _logger.LogInformation("Updated Cronometer session for Telegram user {TelegramChatId}", telegramChatId);
        }
        else
        {
            // Create new session
            var newSession = new CronometerSession
            {
                TelegramChatId = telegramChatId,
                CronometerUserId = cronometerUserId,
                SessionKey = sessionKey,
                Email = email,
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var newDocRef = await _db.Collection(SessionsCollection).AddAsync(newSession, ct);
            newSession.Id = newDocRef.Id;

            _logger.LogInformation("Created new Cronometer session for Telegram user {TelegramChatId}", telegramChatId);
        }
    }

    /// <summary>
    /// Gets a Cronometer session for a Telegram user.
    /// </summary>
    public async Task<CronometerSession?> GetSessionAsync(string telegramChatId, CancellationToken ct = default)
    {
        var query = _db.Collection(SessionsCollection)
            .WhereEqualTo("telegramChatId", telegramChatId)
            .WhereEqualTo("isActive", true)
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var doc = snapshot.Documents.FirstOrDefault();

        if (doc == null)
        {
            _logger.LogDebug("No session found for Telegram user {TelegramChatId}", telegramChatId);
            return null;
        }

        var session = doc.ConvertTo<CronometerSession>();
        session.Id = doc.Id;

        _logger.LogDebug("Found session for Telegram user {TelegramChatId}, Cronometer user {CronometerUserId}",
            telegramChatId, session.CronometerUserId);

        return session;
    }

    /// <summary>
    /// Gets all active Cronometer sessions.
    /// Used to restore sessions on server startup.
    /// </summary>
    public async Task<List<CronometerSession>> GetAllActiveSessionsAsync(CancellationToken ct = default)
    {
        var query = _db.Collection(SessionsCollection)
            .WhereEqualTo("isActive", true);

        var snapshot = await query.GetSnapshotAsync(ct);

        var sessions = snapshot.Documents
            .Select(doc =>
            {
                var session = doc.ConvertTo<CronometerSession>();
                session.Id = doc.Id;
                return session;
            })
            .ToList();

        _logger.LogInformation("Loaded {Count} active Cronometer sessions from Firestore", sessions.Count);

        return sessions;
    }

    /// <summary>
    /// Deactivates a Cronometer session (logout).
    /// </summary>
    public async Task DeactivateSessionAsync(string telegramChatId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(telegramChatId, ct);

        if (session == null)
        {
            _logger.LogDebug("No session to deactivate for Telegram user {TelegramChatId}", telegramChatId);
            return;
        }

        var docRef = _db.Collection(SessionsCollection).Document(session.Id);

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "isActive", false },
            { "lastUpdatedAt", DateTime.UtcNow }
        }, cancellationToken: ct);

        _logger.LogInformation("Deactivated Cronometer session for Telegram user {TelegramChatId}", telegramChatId);
    }

    #endregion

    #region Alias Detection

    /// <summary>
    /// Detects all user aliases that appear in the input text.
    /// This should be called BEFORE sending the text to the LLM.
    /// </summary>
    public async Task<List<DetectedAlias>> DetectAliasesInTextAsync(
        string userId, 
        string inputText, 
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputText))
            return new List<DetectedAlias>();

        // Get all user aliases
        var userAliases = await GetUserAliasesAsync(userId, ct);
        
        if (userAliases.Count == 0)
            return new List<DetectedAlias>();

        var detectedAliases = new List<DetectedAlias>();
        var normalizedInput = inputText.ToLowerInvariant();

        // Sort aliases by length descending to match longer terms first
        // This prevents "mi" from matching before "mi proteina"
        var sortedAliases = userAliases.OrderByDescending(a => a.InputTerm.Length).ToList();

        foreach (var alias in sortedAliases)
        {
            var searchTerm = alias.InputTerm.ToLowerInvariant();
            var index = 0;

            while ((index = normalizedInput.IndexOf(searchTerm, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                // Check if this is a word boundary match (not part of another word)
                var isWordStart = index == 0 || !char.IsLetterOrDigit(normalizedInput[index - 1]);
                var endIndex = index + searchTerm.Length;
                var isWordEnd = endIndex >= normalizedInput.Length || !char.IsLetterOrDigit(normalizedInput[endIndex]);

                if (isWordStart && isWordEnd)
                {
                    // Check if this position is not already covered by a longer match
                    var isOverlapping = detectedAliases.Any(d => 
                        (index >= d.StartIndex && index < d.StartIndex + d.Length) ||
                        (endIndex > d.StartIndex && endIndex <= d.StartIndex + d.Length));

                    if (!isOverlapping)
                    {
                        detectedAliases.Add(new DetectedAlias
                        {
                            MatchedTerm = inputText.Substring(index, searchTerm.Length),
                            StartIndex = index,
                            Length = searchTerm.Length,
                            Alias = alias
                        });

                        _logger.LogInformation(
                            "🎯 Detected alias in text: '{Term}' -> '{ResolvedName}' at position {Index}",
                            alias.InputTerm, alias.ResolvedFoodName, index);
                    }
                }

                index = endIndex;
            }
        }

        _logger.LogInformation("Detected {Count} aliases in user input", detectedAliases.Count);

        return detectedAliases.OrderBy(d => d.StartIndex).ToList();
    }

    /// <summary>
    /// Tries to find a pre-detected alias that matches a food name from the LLM output.
    /// Uses fuzzy matching since the LLM might have translated or modified the term.
    /// </summary>
    public FoodAlias? FindMatchingDetectedAlias(
        string foodNameFromLlm, 
        List<DetectedAlias> detectedAliases,
        string originalUserInput)
    {
        if (detectedAliases.Count == 0)
            return null;

        var normalizedFoodName = foodNameFromLlm.ToLowerInvariant();

        // Strategy 1: Direct match with alias input term
        foreach (var detected in detectedAliases)
        {
            var aliasTermLower = detected.Alias.InputTerm.ToLowerInvariant();
            
            // Check if the LLM food name contains the alias term or vice versa
            if (normalizedFoodName.Contains(aliasTermLower) || 
                aliasTermLower.Contains(normalizedFoodName))
            {
                _logger.LogInformation(
                    "✅ Matched LLM output '{LlmFood}' to alias '{AliasTerm}' -> '{ResolvedName}'",
                    foodNameFromLlm, detected.Alias.InputTerm, detected.Alias.ResolvedFoodName);
                return detected.Alias;
            }
        }

        // Strategy 2: Check if the LLM food name matches the resolved name
        foreach (var detected in detectedAliases)
        {
            var resolvedNameLower = detected.Alias.ResolvedFoodName.ToLowerInvariant();
            
            if (normalizedFoodName.Contains(resolvedNameLower) || 
                resolvedNameLower.Contains(normalizedFoodName))
            {
                _logger.LogInformation(
                    "✅ Matched LLM output '{LlmFood}' to resolved name '{ResolvedName}' from alias '{AliasTerm}'",
                    foodNameFromLlm, detected.Alias.ResolvedFoodName, detected.Alias.InputTerm);
                return detected.Alias;
            }
        }

        // Strategy 3: Check word overlap for partial matches
        var foodWords = normalizedFoodName.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var detected in detectedAliases)
        {
            var aliasWords = detected.Alias.InputTerm.ToLowerInvariant()
                .Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);

            // If any significant word matches
            var matchingWords = foodWords.Intersect(aliasWords).ToList();
            if (matchingWords.Count > 0 && matchingWords.Any(w => w.Length > 2))
            {
                _logger.LogInformation(
                    "✅ Matched LLM output '{LlmFood}' to alias '{AliasTerm}' via word overlap: [{Words}]",
                    foodNameFromLlm, detected.Alias.InputTerm, string.Join(", ", matchingWords));
                return detected.Alias;
            }
        }

        _logger.LogDebug("No alias match found for LLM output '{LlmFood}'", foodNameFromLlm);
        return null;
    }

    #endregion

    #region Clarification Preferences

    private const string ClarificationPrefsCollection = "clarificationPreferences";

    /// <summary>
    /// Looks for a saved clarification preference for the given food term and clarification type.
    /// Returns the preference only if it's confirmed (occurred 2+ times).
    /// </summary>
    public async Task<ClarificationPreference?> FindClarificationPreferenceAsync(
        string userId, 
        string foodTerm, 
        string clarificationType, 
        CancellationToken ct = default)
    {
        var normalizedTerm = NormalizeTerm(foodTerm);

        var query = _db.Collection(ClarificationPrefsCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("foodTerm", normalizedTerm)
            .WhereEqualTo("clarificationType", clarificationType)
            .WhereEqualTo("isConfirmed", true)
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var doc = snapshot.Documents.FirstOrDefault();

        if (doc == null)
        {
            _logger.LogDebug("No confirmed clarification preference for user {UserId}, term '{Term}', type '{Type}'", 
                userId, normalizedTerm, clarificationType);
            return null;
        }

        var pref = doc.ConvertTo<ClarificationPreference>();
        pref.Id = doc.Id;

        // Don't use preferences with empty answers (legacy documents)
        if (string.IsNullOrWhiteSpace(pref.DefaultAnswer))
        {
            _logger.LogWarning("Found preference with empty answer, ignoring: '{Term}' + {Type}",
                pref.FoodTerm, pref.ClarificationType);
            return null;
        }

        _logger.LogInformation("Found clarification preference: '{Term}' + {Type} -> '{Answer}'",
            pref.FoodTerm, pref.ClarificationType, pref.DefaultAnswer);

        return pref;
    }

    /// <summary>
    /// Records a clarification answer. If the same pattern occurs twice, it becomes a confirmed preference.
    /// </summary>
    public async Task<bool> RecordClarificationPatternAsync(
        string userId,
        string foodTerm,
        string clarificationType,
        string answer,
        CancellationToken ct = default)
    {
        var normalizedTerm = NormalizeTerm(foodTerm);
        var normalizedAnswer = answer.Trim().ToLowerInvariant();

        _logger.LogInformation("Recording pattern: userId={UserId}, term='{Term}', type='{Type}', answer='{Answer}'",
            userId, normalizedTerm, clarificationType, normalizedAnswer);

        // Check if we already have a pending/confirmed preference for this pattern
        var query = _db.Collection(ClarificationPrefsCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("foodTerm", normalizedTerm)
            .WhereEqualTo("clarificationType", clarificationType)
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(ct);
        var existingDoc = snapshot.Documents.FirstOrDefault();

        _logger.LogInformation("Existing document found: {Found}", existingDoc != null);

        if (existingDoc != null)
        {
            var existing = existingDoc.ConvertTo<ClarificationPreference>();
            _logger.LogInformation("Existing preference: answer='{Answer}', count={Count}, confirmed={Confirmed}",
                existing.DefaultAnswer, existing.OccurrenceCount, existing.IsConfirmed);

            // If existing answer is empty (legacy document), treat as if same answer
            var existingAnswerIsEmpty = string.IsNullOrWhiteSpace(existing.DefaultAnswer);
            
            // Check if the answer is the same (or existing is empty/legacy)
            if (existingAnswerIsEmpty || existing.DefaultAnswer.Equals(normalizedAnswer, StringComparison.OrdinalIgnoreCase))
            {
                // Same answer, increment count and potentially confirm
                var newCount = existing.OccurrenceCount + 1;
                var isNowConfirmed = newCount >= 2;

                await existingDoc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    ["defaultAnswer"] = normalizedAnswer, // Always update answer (fixes legacy docs)
                    ["occurrenceCount"] = newCount,
                    ["isConfirmed"] = isNowConfirmed,
                    ["lastUsedAt"] = DateTime.UtcNow
                }, cancellationToken: ct);

                if (isNowConfirmed && !existing.IsConfirmed)
                {
                    _logger.LogInformation(
                        "🧠 Clarification preference CONFIRMED: '{Term}' + {Type} -> '{Answer}' (count: {Count})",
                        normalizedTerm, clarificationType, normalizedAnswer, newCount);
                    return true; // Newly confirmed
                }

                _logger.LogDebug("Updated clarification pattern count: {Count}", newCount);
                return false;
            }
            else
            {
                // Different answer, reset the count with new answer
                await existingDoc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    ["defaultAnswer"] = normalizedAnswer,
                    ["occurrenceCount"] = 1,
                    ["isConfirmed"] = false,
                    ["lastUsedAt"] = DateTime.UtcNow
                }, cancellationToken: ct);

                _logger.LogDebug("Clarification pattern reset with new answer: '{Answer}'", normalizedAnswer);
                return false;
            }
        }
        else
        {
            // Create new pending preference
            var newPref = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["foodTerm"] = normalizedTerm,
                ["clarificationType"] = clarificationType,
                ["defaultAnswer"] = normalizedAnswer,
                ["occurrenceCount"] = 1,
                ["isConfirmed"] = false,
                ["createdAt"] = DateTime.UtcNow,
                ["lastUsedAt"] = DateTime.UtcNow
            };

            await _db.Collection(ClarificationPrefsCollection).AddAsync(newPref, ct);
            _logger.LogDebug("Created new clarification pattern: '{Term}' + {Type} -> '{Answer}'",
                normalizedTerm, clarificationType, normalizedAnswer);

            return false;
        }
    }

    /// <summary>
    /// Gets all confirmed clarification preferences for a user.
    /// </summary>
    public async Task<List<ClarificationPreference>> GetUserClarificationPreferencesAsync(
        string userId, 
        CancellationToken ct = default)
    {
        var query = _db.Collection(ClarificationPrefsCollection)
            .WhereEqualTo("userId", userId)
            .WhereEqualTo("isConfirmed", true);

        var snapshot = await query.GetSnapshotAsync(ct);

        return snapshot.Documents
            .Select(doc =>
            {
                var pref = doc.ConvertTo<ClarificationPreference>();
                pref.Id = doc.Id;
                return pref;
            })
            .ToList();
    }

    #endregion
}
