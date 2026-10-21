namespace Domain.Exceptions;

/// <summary>
/// Thrown when a resource is held by another actor (HTTP 409 Conflict).
/// </summary>
public class ConflictException : DomainException
{
    /// <summary>Optional structured errors for API clients.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Initializes a conflict exception.</summary>
    public ConflictException(string message) : this(message, [])
    {
    }

    /// <summary>Initializes a conflict exception with structured errors.</summary>
    public ConflictException(string message, IReadOnlyList<string> errors) : base(message)
    {
        Errors = errors ?? [];
    }
}
