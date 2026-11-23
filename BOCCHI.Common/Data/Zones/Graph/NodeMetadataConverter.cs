using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Common.Data.Zones.Graph;

public class NodeMetadataConverter : JsonConverter<INodeMetadata>
{
    public override INodeMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("MetadataType", out JsonElement typeProp))
        {
            throw new JsonException("Missing MetadataType discriminator");
        }

        string? typeName = typeProp.GetString();

        return typeName switch
        {
            nameof(BlankNodeMetadata) => JsonSerializer.Deserialize<BlankNodeMetadata>(root.GetRawText(), options)!,
            nameof(TeleportNodeMetadata) => JsonSerializer.Deserialize<TeleportNodeMetadata>(root.GetRawText(), options)!,
            nameof(ActivityNodeMetadata) => JsonSerializer.Deserialize<ActivityNodeMetadata>(root.GetRawText(), options)!,
            var _ => throw new JsonException($"Unknown metadata type: {typeName}")
        };
    }

    public override void Write(Utf8JsonWriter writer, INodeMetadata value, JsonSerializerOptions options)
    {
        string typeName = value.GetType().Name;

        JsonElement json = JsonSerializer.SerializeToElement(value, value.GetType(), options);

        writer.WriteStartObject();
        writer.WriteString("MetadataType", typeName);

        foreach(JsonProperty prop in json.EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
