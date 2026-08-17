using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adnd.Core.Config;

public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            return ParseColorName(name);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                return ParseColorName(nameProp.GetString());

            var r = TryReadByte(root, "R");
            var g = TryReadByte(root, "G");
            var b = TryReadByte(root, "B");
            var a = TryReadByte(root, "A") ?? (byte)255;

            if (r.HasValue && g.HasValue && b.HasValue)
                return Color.FromArgb(a, r.Value, g.Value, b.Value);
        }

        return Color.Green;
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        var name = value.IsNamedColor ? value.Name : $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        writer.WriteStringValue(name);
    }

    private static Color ParseColorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Color.Green;

        var color = Color.FromName(name);
        if (!color.IsKnownColor && !color.IsNamedColor)
            return Color.Green;

        return color;
    }

    private static byte? TryReadByte(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind != JsonValueKind.Number)
            return null;

        return prop.TryGetByte(out var value) ? value : null;
    }
}
