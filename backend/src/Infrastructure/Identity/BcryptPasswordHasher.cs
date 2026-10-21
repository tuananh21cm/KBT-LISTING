using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

/// <summary>
/// BCrypt password hasher for Identity.
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher<ApplicationUser>
{
    /// <inheritdoc />
    public string HashPassword(ApplicationUser user, string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    /// <inheritdoc />
    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        var isValid = BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword);
        return isValid ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
    }
}
