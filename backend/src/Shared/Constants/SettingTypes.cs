namespace Shared.Constants;

/// <summary>
/// Supported setting value types.
/// </summary>
public static class SettingTypes
{
    /// <summary>Plain text value.</summary>
    public const string Text = "text";

    /// <summary>Numeric value.</summary>
    public const string Number = "number";

    /// <summary>Boolean toggle (true/false).</summary>
    public const string Boolean = "boolean";

    /// <summary>Sensitive value (masked in UI).</summary>
    public const string Password = "password";

    /// <summary>All valid types.</summary>
    public static readonly string[] All = [Text, Number, Boolean, Password];
}
