using System.Text.Json;
using System.Text.Json.Serialization;

namespace PSMobileWallpaper.Domain.Serialization;

/// <summary>
/// Emits an enum member as its uppercase name. Spec §5.4 fixes the transport wire values as
/// "ADB" and "HDC", which are not valid C# member names in that casing.
/// </summary>
public sealed class UppercaseEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new JsonException($"'{value}' is not a valid {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToUpperInvariant());
}
