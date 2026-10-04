using AIChat.Api.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Api.Services;

public class UserIdentityService : IUserIdentityService
{
    private readonly Dictionary<string, string> _codeToUserId;
    private readonly HashSet<string> _adminUserIds;

    public UserIdentityService(IOptions<List<UserConfig>> configuration, ILogger<UserIdentityService> logger)
    {
        var users = configuration.Value;
        _codeToUserId = new Dictionary<string, string>(StringComparer.Ordinal);
        _adminUserIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.Id))
            {
                throw new InvalidOperationException("User config entry has empty Id");
            }

            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                _adminUserIds.Add(user.Id);

            foreach (var code in user.AuthCodes)
            {
                if (string.IsNullOrEmpty(code)) continue;
                if (_codeToUserId.TryGetValue(code, out var existingUserId))
                {
                    throw new InvalidOperationException(
                        $"Auth code is mapped to multiple users: {existingUserId} and {user.Id}");
                }
                _codeToUserId[code] = user.Id;
            }
        }

        logger.LogInformation("UserIdentityService initialized with {UserCount} users, {CodeCount} auth codes",
            users.Count, _codeToUserId.Count);
    }

    public string? ResolveUserId(string? authCode)
    {
        if (string.IsNullOrEmpty(authCode)) return null;
        return _codeToUserId.TryGetValue(authCode, out var userId) ? userId : null;
    }

    public bool IsAdmin(string? userId) => userId is not null && _adminUserIds.Contains(userId);
}
