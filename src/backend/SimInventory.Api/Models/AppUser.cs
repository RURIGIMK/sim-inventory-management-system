namespace SimInventory.Api.Models;

public enum UserRole
{
    Administrator,
    DepartmentalManager,
    Viewer
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public bool TotpEnabled { get; set; }
    public string? ProtectedTotpSecret { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed record CreateUserRequest(string Email, string DisplayName, UserRole Role);
public sealed record UpdateUserRequest(UserRole Role, bool IsActive);
public sealed record GoogleCredentialRequest(string Credential);
public sealed record TotpRequest(string Code);
public sealed record AuthResponse(string Status, string? Token = null, string? Email = null,
    string? DisplayName = null, string? Role = null, string? TotpSetupUri = null, string? ManualSecret = null);
