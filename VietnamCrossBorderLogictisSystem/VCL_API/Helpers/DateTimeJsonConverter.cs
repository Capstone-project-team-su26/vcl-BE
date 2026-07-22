using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VCL_API.Helpers
{
    public class DateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DateTime.Parse(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // If the DateTime kind is Unspecified, treat it as UTC so it gets serialized with 'Z' suffix
            if (value.Kind == DateTimeKind.Unspecified)
            {
                writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
            }
            else
            {
                writer.WriteStringValue(value.ToUniversalTime());
            }
        }
    }
}
