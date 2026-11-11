namespace DyeHouseERP.Application.Users.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; }
    /// <summary>Part 2: while true the account may only change-password, logout or read itself.</summary>
    public bool MustChangePassword { get; set; }
}
