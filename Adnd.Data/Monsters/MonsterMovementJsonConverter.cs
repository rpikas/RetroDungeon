using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adnd.Data.Monsters;

public sealed class MonsterMovementJsonConverter : JsonConverter<MonsterMovementJson>
{
    public override MonsterMovementJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var walk = reader.GetInt32();
            return new MonsterMovementJson
            {
                Walk = walk,
                Fly = 0,
                Swim = 0,
                Burrow = 0,
                Climb = 0
            };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            return new MonsterMovementJson
            {
                Walk = TryGetInt(root, "Walk"),
                Fly = TryGetInt(root, "Fly"),
                Swim = TryGetInt(root, "Swim"),
                Burrow = TryGetInt(root, "Burrow"),
                Climb = TryGetInt(root, "Climb")
            };
        }

        throw new JsonException($"Unsupported Movement token type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, MonsterMovementJson value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Walk);
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }
}
