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
}
