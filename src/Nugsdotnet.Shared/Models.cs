namespace Nugsdotnet.Shared;

/// <summary>
/// Shape returned by GET /api/session. The client uses this to decide
/// whether to render the login screen.
/// </summary>
public sealed record SessionInfo(
    bool LoggedIn,
    string? UserId = null,
    string? Plan = null,
    bool Accessible = false);

/// <summary>
/// Body of POST /api/login. If both fields are null, the server falls back
/// to NUGS_EMAIL / NUGS_PASSWORD from configuration.
/// </summary>
public sealed record LoginRequest(string? Email = null, string? Password = null);

/// <summary>
/// Returned by POST /api/login on failure. On success the response is empty
/// with a 200 status.
/// </summary>
public sealed record ErrorResponse(string Error);
