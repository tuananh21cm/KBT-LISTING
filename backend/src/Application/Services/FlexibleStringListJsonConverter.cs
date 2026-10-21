using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Services;

/// <summary>
/// OpenAI sometimes emits a string instead of a string array. Accept both so one field
/// cannot fail the whole «Shop này bán gì» payload.
/// </summary>
internal sealed class FlexibleStringListJsonConverter : JsonConverter<List<string>?>
{
    /// <inheritdoc />
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return [];
        }

        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    list.Add(value.Trim());
                }

                continue;
            }

            reader.Skip();
        }

        return list;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (value is not null)
        {
            foreach (var item in value)
            {
                writer.WriteStringValue(item);
            }
        }

        writer.WriteEndArray();
    }
}
