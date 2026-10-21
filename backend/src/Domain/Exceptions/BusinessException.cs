namespace Domain.Exceptions;

/// <summary>
/// Thrown when business rules are violated.
/// </summary>
public class BusinessException : DomainException
{
    /// <summary>
    /// Optional field-level or diagnostic errors for API clients.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a business exception.
    /// </summary>
    public BusinessException(string message) : this(message, [])
    {
    }

    /// <summary>
    /// Initializes a business exception with structured errors.
    /// </summary>
    public BusinessException(string message, IReadOnlyList<string> errors) : base(message)
    {
        Errors = errors ?? [];
    }
}
