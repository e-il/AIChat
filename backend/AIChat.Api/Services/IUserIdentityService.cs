namespace AIChat.Api.Services;

public interface IUserIdentityService
{
    /// <summary>
    /// Resolves an auth code to a stable user identifier.
    /// Returns null if the code is not recognized.
    /// </summary>
    string? ResolveUserId(string? authCode);
}
