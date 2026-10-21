namespace Shared.Models;

/// <summary>
/// Standard API response wrapper.
/// </summary>
/// <typeparam name="T">Response data type.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the request succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Response payload.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Validation or business errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = [];

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static ApiResponse<T> Ok(T? data, string message = "") =>
        new() { Success = true, Message = message, Data = data };

    /// <summary>
    /// Creates a failed response.
    /// </summary>
    public static ApiResponse<T> Fail(string message, IReadOnlyList<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}
