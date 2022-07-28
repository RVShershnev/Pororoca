using System.Text.Json;
using System.Text.Json.Serialization;
using Pororoca.Domain.Features.Entities.Pororoca;
using static Pororoca.Domain.Features.Common.JsonConfiguration;

namespace Pororoca.Domain.Features.ImportCollection;

// Based on:
// https://blog.maartenballiauw.be/post/2020/01/29/deserializing-json-into-polymorphic-classes-with-systemtextjson.html
public sealed class PororocaRequestJsonConverter : JsonConverter<PororocaRequest>
{
    public override bool CanConvert(Type typeToConvert) =>
        typeof(PororocaRequest).IsAssignableFrom(typeToConvert);

    public override PororocaRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Check for null values
        if (reader.TokenType == JsonTokenType.Null)
            return null!; // ugly hehehe
        
        // Copy the current state from reader (it's a struct)
        var readerAtStart = reader;

        using var jsonDocument = JsonDocument.ParseValue(ref reader);
        var jsonObject = jsonDocument.RootElement;
        bool requestTypeJsonElementFound = jsonObject.TryGetProperty("requestType", out var requestTypeJsonElement);
        
        if (!requestTypeJsonElementFound)
        {
            throw new JsonException("\"requestType\" not found in request object!");
        }

        string? requestTypeStr = requestTypeJsonElement.GetString();
        bool requestTypeValid = Enum.TryParse<PororocaRequestType>(requestTypeStr, true, out var requestType);
        if (!requestTypeValid)
        {
            throw new JsonException($"'{requestTypeStr}' is not a valid value for a \"requestType\"!");
        }

        // The custom converters cannot be used below, otherwise, a recursive call will happen
        // and a StackOverflowExcpetion will arise
        return requestType switch
        {
            _ => JsonSerializer.Deserialize<PororocaHttpRequest>(ref readerAtStart, ExporterImporterWithoutCustomConvertersJsonOptions)!
        };
    }

    public override void Write(Utf8JsonWriter writer, PororocaRequest req, JsonSerializerOptions options) =>
        // No need for this one in our use case, but to just dump the object into JSON
        // (without having the requestType property!), we can do this:
        JsonSerializer.Serialize(writer, req, req.GetType(), ExporterImporterWithoutCustomConvertersJsonOptions);
}