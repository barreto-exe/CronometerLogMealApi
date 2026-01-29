namespace CronometerLogMealApi.Services;

/// <summary>
/// Configuration options for Firebase Firestore.
/// </summary>
public class FirebaseOptions
{
    /// <summary>
    /// The Firebase project ID.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// The service account email (client_email from the credentials JSON).
    /// </summary>
    public string ClientEmail { get; set; } = string.Empty;

    /// <summary>
    /// The private key from the service account (private_key from the credentials JSON).
    /// Can be the raw key with \n or with actual newlines.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to the service account credentials JSON file.
    /// If PrivateKey and ClientEmail are provided, this is ignored.
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Returns true if inline credentials (ClientEmail + PrivateKey) are configured.
    /// </summary>
    public bool HasInlineCredentials => 
        !string.IsNullOrWhiteSpace(ClientEmail) && 
        !string.IsNullOrWhiteSpace(PrivateKey);
}
