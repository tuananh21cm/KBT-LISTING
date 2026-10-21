using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Database-backed system settings provider.
/// </summary>
public class SettingProvider : ISettingProvider
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes the setting provider.
    /// </summary>
    public SettingProvider(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        return setting?.Value;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetValuesAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await _context.Settings
            .AsNoTracking()
            .Where(s => keyList.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
        {
            return;
        }

        setting.Value = value;
        setting.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
