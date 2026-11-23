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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>
    /// Department a Departmental Manager is responsible for. Custody operations by a
    /// manager are limited to employees in this department. Administrators are not scoped.
    /// </summary>
    public string? Department { get; set; }
}

public sealed record CreateUserRequest(string Email, string DisplayName, UserRole Role, string? Department = null);
public sealed record UpdateUserRequest(UserRole Role, bool IsActive, string? Department = null);
public sealed record GoogleCredentialRequest(string Credential);
public sealed record AuthResponse(string Status, string? Token = null, string? Email = null,
    string? DisplayName = null, string? Role = null);
