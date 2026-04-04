using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsurperRemake.Utils
{
    /// <summary>
    /// JSON converter factory that produces tolerant enum converters for any enum type.
    /// Unknown enum values log a warning and return the default instead of throwing.
    /// </summary>
    public class TolerantEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum ||
                   (Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var enumType = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            var converterType = typeof(TolerantEnumConverter<>).MakeGenericType(enumType);
            var converter = (JsonConverter)Activator.CreateInstance(converterType)!;

            if (Nullable.GetUnderlyingType(typeToConvert) != null)
            {
                var nullableType = typeof(NullableTolerantEnumConverter<>).MakeGenericType(enumType);
                return (JsonConverter)Activator.CreateInstance(nullableType)!;
            }

            return converter;
        }
    }

    public class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(value))
                    return default;
                if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
                    return result;

                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("GAMEDATA",
                    $"Unknown {typeof(T).Name} value: '{value}', using default");
                return default;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                var intVal = reader.GetInt32();
                if (Enum.IsDefined(typeof(T), intVal))
                    return (T)Enum.ToObject(typeof(T), intVal);

                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("GAMEDATA",
                    $"Unknown {typeof(T).Name} numeric value: {intVal}, using default");
                return default;
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    public class NullableTolerantEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
    {
        private readonly TolerantEnumConverter<T> _inner = new();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            return _inner.Read(ref reader, typeof(T), options);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                _inner.Write(writer, value.Value, options);
        }
    }
}
