namespace Shared.Constants;

/// <summary>
/// Per-user UI theme preference values stored on <c>ApplicationUser.Theme</c>.
/// </summary>
public static class UiThemeModes
{
    /// <summary>Light appearance (default).</summary>
    public const string Light = "light";

    /// <summary>Dark appearance (Cursor / OpenAI-platform style).</summary>
    public const string Dark = "dark";

    /// <summary>Allowed values for validators and normalization.</summary>
    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Light,
        Dark,
    };

    /// <summary>
    /// Returns a canonical <c>light</c>/<c>dark</c> value, or <see cref="Light"/> when invalid.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.Equals(value, Dark, StringComparison.OrdinalIgnoreCase))
        {
            return Dark;
        }

        return Light;
    }
}
