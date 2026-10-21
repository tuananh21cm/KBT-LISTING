using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Services;

/// <summary>
/// Chuẩn hoá phone / zip / họ tên thành khoá so khớp giữa đơn SHEIN và đơn sàn.
///
/// Tách khỏi <see cref="OrderSyncService"/> để công cụ chẩn đoán giải thích được vì sao một billno
/// bị bỏ với lý do <c>no_local_match</c>. Nếu chẩn đoán tự chuẩn hoá lại theo cách riêng thì nó sẽ
/// nói khác luồng sync thật và câu trả lời trở nên vô giá trị, nên hai bên phải dùng đúng một hàm.
/// </summary>
public static class SheinMatchKeys
{
    /// <summary>Họ tên chuẩn hoá thành tập token đã sắp xếp (không phụ thuộc thứ tự).</summary>
    public static string NameKey(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0) return string.Empty;
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Array.Sort(tokens, StringComparer.Ordinal);
        return string.Join(' ', tokens);
    }

    /// <summary>Chữ thường, bỏ dấu và dấu câu, gộp khoảng trắng.</summary>
    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append(' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>5 chữ số đầu của mã bưu chính.</summary>
    public static string ZipKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length <= 5 ? digits : digits[..5];
    }

    /// <summary>
    /// Số điện thoại chỉ còn chữ số để so khớp tuyệt đối. Bỏ vỏ như "(+1)" / "+1" và mã quốc gia
    /// Mỹ đứng đầu, nên "(+1)98738945", "+198738945" và "987-389-45" đều thành "98738945".
    /// </summary>
    public static string PhoneKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim();
        // "(+1)987…" / "(+84)…" → bỏ mã quốc gia trong ngoặc.
        s = Regex.Replace(s, @"^\(\s*\+\s*\d+\s*\)\s*", string.Empty);
        // "+1 987…" / "+1987…" (NANP) — bỏ +1, giữ số local.
        s = Regex.Replace(s, @"^\+\s*1\s*(?=\d)", string.Empty);
        s = Regex.Replace(s, @"^\+\s*", string.Empty);
        var digits = new string(s.Where(char.IsDigit).ToArray());
        // "19873894504" (11 số NANP, không có dấu +) → bỏ leading 1.
        if (digits.Length == 11 && digits.StartsWith('1')) digits = digits[1..];
        return digits;
    }
}
