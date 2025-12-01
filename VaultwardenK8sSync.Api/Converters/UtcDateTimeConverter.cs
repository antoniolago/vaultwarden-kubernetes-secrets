using System.Text.Json;
using System.Text.Json.Serialization;

namespace VaultwardenK8sSync.Api.Converters;

/// <summary>
/// JSON converter that ensures DateTime values are always serialized as UTC with 'Z' suffix.
/// This fixes timezone display issues in the dashboard.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateTimeString = reader.GetString();
        if (string.IsNullOrEmpty(dateTimeString))
        {
            return default;
        }
        
        return DateTime.Parse(dateTimeString).ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Ensure value is treated as UTC and serialize with 'Z' suffix
        var utcValue = value.Kind == DateTimeKind.Unspecified 
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc) 
            : value.ToUniversalTime();
            
        writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}
