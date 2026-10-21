namespace Domain.Exceptions;

/// <summary>
/// Base domain exception.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Initializes a new domain exception.
    /// </summary>
    protected DomainException(string message) : base(message)
    {
    }
}
