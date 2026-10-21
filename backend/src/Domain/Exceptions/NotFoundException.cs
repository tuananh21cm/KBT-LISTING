namespace Domain.Exceptions;

/// <summary>
/// Thrown when a requested entity is not found.
/// </summary>
public class NotFoundException : DomainException
{
    /// <summary>
    /// Initializes a not-found exception.
    /// </summary>
    public NotFoundException(string message) : base(message)
    {
    }
}
