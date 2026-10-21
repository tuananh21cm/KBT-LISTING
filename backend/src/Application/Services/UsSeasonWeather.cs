using System.Globalization;

namespace Application.Services;

/// <summary>
/// Typical contiguous-US climate for a calendar month so the model never guesses weather.
/// Boost / penalize tokens are matched against TikTok search keywords.
/// </summary>
public sealed record UsSeasonWeatherInfo(
    string Note,
    IReadOnlyList<string> BoostTokens,
    IReadOnlyList<string> PenalizeTokens);

/// <summary>
/// Deterministic US weather-by-month, aligned with <see cref="UsRetailCalendar"/> lead times.
/// </summary>
public static class UsSeasonWeather
{
    /// <summary>Vietnamese climate note + English keyword tokens for the given UTC date.</summary>
    public static UsSeasonWeatherInfo Get(DateTime dateUtc) => dateUtc.Month switch
    {
        1 => new(
            "Tháng 1: mùa đông lạnh phần lớn nước Mỹ — layer, sweater, coat, fleece; tránh sundress/linen mỏng.",
            ["sweater", "cardigan", "coat", "fleece", "knit", "long sleeve", "turtleneck", "thermal", "wool"],
            ["sundress", "linen", "tank top", "bikini", "swimsuit", "crop top", "shorts"]),
        2 => new(
            "Tháng 2: còn lạnh + Valentine — knit/layer; đầm cocktail kín đáo hơn bikini/hè.",
            ["sweater", "cardigan", "knit", "long sleeve", "midi", "wrap dress", "valentine"],
            ["sundress", "linen", "bikini", "swimsuit", "tank top"]),
        3 => new(
            "Tháng 3: đầu xuân — transseasonal, light jacket, midi; bỏ puffer nặng và đồ bơi.",
            ["light jacket", "cardigan", "midi", "trench", "transitional", "long sleeve"],
            ["puffer", "parka", "ugly christmas", "bikini", "swimsuit"]),
        4 or 5 => new(
            "Tháng 4–5: xuân ấm — midi, light dress, linen bắt đầu; tránh coat/fleece nặng.",
            ["midi", "floral", "linen", "light", "spring", "cardigan", "short sleeve"],
            ["puffer", "parka", "fleece", "thermal", "ugly christmas", "winter coat"]),
        6 or 7 => new(
            "Tháng 6–7: hè nóng — vải mỏng, sundress, linen, short sleeve; cấm coat/sweater dày.",
            ["linen", "sundress", "cotton", "short sleeve", "maxi", "lightweight", "summer"],
            ["puffer", "parka", "fleece", "sweater", "turtleneck", "wool coat", "thermal"]),
        8 => new(
            "Tháng 8: cuối hè Mỹ còn nóng, Back to School / Labor Day gần — vải mỏng, midi, cardigan nhẹ; tránh puffer, coat, christmas sweater.",
            ["linen", "cotton", "sundress", "midi", "lightweight", "short sleeve", "cardigan", "back to school"],
            ["puffer", "parka", "fleece", "ugly christmas", "christmas sweater", "winter coat", "thermal", "faux fur"]),
        9 => new(
            "Tháng 9: chuyển mùa thu — layer nhẹ, cardigan, midi, light jacket; hè mỏng vẫn bán được, coat nặng chưa.",
            ["cardigan", "light jacket", "midi", "long sleeve", "fall", "layer"],
            ["puffer", "parka", "ugly christmas", "bikini", "swimsuit"]),
        10 => new(
            "Tháng 10: thu + Halloween — knit, layer, midi; bắt đầu sweater; chưa phải lúc puffer cực đông.",
            ["sweater", "cardigan", "knit", "midi", "halloween", "fall", "long sleeve"],
            ["sundress", "bikini", "swimsuit", "tank top"]),
        11 => new(
            "Tháng 11: lạnh + Thanksgiving / Black Friday — sweater, coat, layer; tránh đồ hè mỏng.",
            ["sweater", "coat", "knit", "fleece", "long sleeve", "thanksgiving"],
            ["sundress", "linen", "bikini", "swimsuit", "tank top", "shorts"]),
        _ => new(
            "Tháng 12: holiday / đông — sweater, coat, party midi kín; cấm sundress/bikini.",
            ["sweater", "coat", "knit", "holiday", "christmas", "velvet", "long sleeve"],
            ["sundress", "linen", "bikini", "swimsuit", "tank top", "shorts"])
    };

    /// <summary>One-line English summary for the model (month + note).</summary>
    public static string ToContextLine(DateTime dateUtc)
    {
        var info = Get(dateUtc);
        var month = dateUtc.ToString("MMMM", CultureInfo.InvariantCulture);
        return $"{month}: {info.Note}";
    }
}
