namespace Application.Interfaces;

/// <summary>
/// Reads system settings from the database.
/// </summary>
public interface ISettingProvider
{
    /// <summary>Gets a setting value by key.</summary>
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets multiple setting values by keys.</summary>
    Task<Dictionary<string, string>> GetValuesAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing setting value by key (no-op if key missing).</summary>
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
}
