namespace Domain.Exceptions;

/// <summary>
/// Thrown when authentication fails.
/// </summary>
public class UnauthorizedException : DomainException
{
    /// <summary>
    /// Initializes an unauthorized exception.
    /// </summary>
    public UnauthorizedException(string message) : base(message)
    {
    }
}
