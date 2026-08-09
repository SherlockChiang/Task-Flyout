using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Task_Flyout.Services
{
    internal enum WeatherCompanionCommand
    {
        Ping,
        GetSnapshot,
        OpenWeather
    }

    internal enum WeatherCompanionProtocolError
    {
        None,
        EmptyRequest,
        RequestTooLarge,
        InvalidJson,
        MissingVersion,
        UnsupportedVersion,
        MissingCommand,
        UnsupportedCommand
    }

    internal enum WeatherCompanionResponseStatus
    {
        Ok,
        Unavailable,
        InvalidRequest,
        VersionMismatch,
        InternalError
    }

    internal readonly record struct WeatherCompanionRequest(
        int Version,
        WeatherCompanionCommand Command);

    internal readonly record struct WeatherCompanionSnapshot(
        string Icon,
        string Temperature,
        string Description,
        string Location,
        string Alert,
        DateTimeOffset UpdatedUtc);

    internal static class WeatherCompanionProtocol
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public const int Version = 1;
        public const int MaxRequestBytes = 4 * 1024;
        public const int MaxResponseBytes = 16 * 1024;

        private const int MaxIconScalars = 16;
        private const int MaxTemperatureScalars = 32;
        private const int MaxDescriptionScalars = 96;
        private const int MaxLocationScalars = 96;
        private const int MaxAlertScalars = 128;
        private const int MaxDetailScalars = 128;

        public static bool TryParseRequest(
            ReadOnlySpan<byte> utf8,
            out WeatherCompanionRequest request,
            out WeatherCompanionProtocolError error)
        {
            request = default;
            if (utf8.IsEmpty)
            {
                error = WeatherCompanionProtocolError.EmptyRequest;
                return false;
            }

            if (utf8.Length > MaxRequestBytes)
            {
                error = WeatherCompanionProtocolError.RequestTooLarge;
                return false;
            }

            int? version = null;
            string? command = null;
            bool sawVersion = false;
            bool sawCommand = false;
            try
            {
                _ = StrictUtf8.GetCharCount(utf8);
                var reader = new Utf8JsonReader(
                    utf8,
                    new JsonReaderOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 8
                    });

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        throw new JsonException();

                    bool isVersion = reader.ValueTextEquals("version"u8);
                    bool isCommand = reader.ValueTextEquals("command"u8);
                    if (!reader.Read()) throw new JsonException();

                    if (isVersion)
                    {
                        if (sawVersion) throw new JsonException();
                        if (reader.TokenType != JsonTokenType.Number ||
                            !reader.TryGetInt32(out int parsedVersion))
                            throw new JsonException();
                        sawVersion = true;
                        version = parsedVersion;
                    }
                    else if (isCommand)
                    {
                        if (sawCommand) throw new JsonException();
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException();
                        sawCommand = true;
                        command = reader.GetString();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
                    throw new JsonException();
            }
            catch (JsonException)
            {
                error = WeatherCompanionProtocolError.InvalidJson;
                return false;
            }
            catch (DecoderFallbackException)
            {
                error = WeatherCompanionProtocolError.InvalidJson;
                return false;
            }

            if (!version.HasValue)
            {
                error = WeatherCompanionProtocolError.MissingVersion;
                return false;
            }

            if (version.Value != Version)
            {
                error = WeatherCompanionProtocolError.UnsupportedVersion;
                return false;
            }

            if (string.IsNullOrEmpty(command))
            {
                error = WeatherCompanionProtocolError.MissingCommand;
                return false;
            }

            WeatherCompanionCommand parsedCommand;
            if (string.Equals(command, "ping", StringComparison.Ordinal))
                parsedCommand = WeatherCompanionCommand.Ping;
            else if (string.Equals(command, "get-snapshot", StringComparison.Ordinal))
                parsedCommand = WeatherCompanionCommand.GetSnapshot;
            else if (string.Equals(command, "open-weather", StringComparison.Ordinal))
                parsedCommand = WeatherCompanionCommand.OpenWeather;
            else
            {
                error = WeatherCompanionProtocolError.UnsupportedCommand;
                return false;
            }

            request = new WeatherCompanionRequest(version.Value, parsedCommand);
            error = WeatherCompanionProtocolError.None;
            return true;
        }

        public static WeatherCompanionSnapshot CreateSnapshot(
            string? icon,
            string? temperature,
            string? description,
            string? location,
            string? alert,
            DateTimeOffset updatedUtc)
            => new(
                NormalizeText(icon, MaxIconScalars),
                NormalizeText(temperature, MaxTemperatureScalars),
                NormalizeText(description, MaxDescriptionScalars),
                NormalizeText(location, MaxLocationScalars),
                NormalizeText(alert, MaxAlertScalars),
                updatedUtc.ToUniversalTime());

        public static byte[] SerializeResponse(
            WeatherCompanionResponseStatus status,
            WeatherCompanionSnapshot? snapshot = null,
            string? detail = null)
        {
            var buffer = new ArrayBufferWriter<byte>(512);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", Version);
                writer.WriteString("status", GetStatusName(status));

                if (!string.IsNullOrWhiteSpace(detail))
                    writer.WriteString("detail", NormalizeText(detail, MaxDetailScalars));

                if (snapshot is WeatherCompanionSnapshot value)
                {
                    writer.WriteStartObject("snapshot");
                    writer.WriteString("icon", NormalizeText(value.Icon, MaxIconScalars));
                    writer.WriteString("temperature", NormalizeText(value.Temperature, MaxTemperatureScalars));
                    writer.WriteString("description", NormalizeText(value.Description, MaxDescriptionScalars));
                    writer.WriteString("location", NormalizeText(value.Location, MaxLocationScalars));
                    writer.WriteString("alert", NormalizeText(value.Alert, MaxAlertScalars));
                    writer.WriteString("updatedUtc", value.UpdatedUtc.ToUniversalTime());
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            if (buffer.WrittenCount > MaxResponseBytes)
                throw new InvalidOperationException("Weather companion response exceeded the protocol limit.");

            return buffer.WrittenSpan.ToArray();
        }

        private static string GetStatusName(WeatherCompanionResponseStatus status)
            => status switch
            {
                WeatherCompanionResponseStatus.Ok => "ok",
                WeatherCompanionResponseStatus.Unavailable => "unavailable",
                WeatherCompanionResponseStatus.InvalidRequest => "invalid-request",
                WeatherCompanionResponseStatus.VersionMismatch => "version-mismatch",
                _ => "internal-error"
            };

        private static string NormalizeText(string? value, int maxScalars)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var result = new StringBuilder(Math.Min(value.Length, maxScalars));
            int scalarCount = 0;
            foreach (Rune rune in value.Trim().EnumerateRunes())
            {
                int codePoint = rune.Value;
                if (IsBidirectionalControl(codePoint)) continue;
                if (scalarCount++ >= maxScalars) break;

                result.Append(codePoint < 0x20 ||
                              (codePoint >= 0x7F && codePoint <= 0x9F) ||
                              codePoint is 0x2028 or 0x2029
                    ? ' '
                    : rune.ToString());
            }
            return result.ToString().Trim();
        }

        private static bool IsBidirectionalControl(int codePoint)
            => codePoint is 0x061C or 0x200E or 0x200F ||
               (codePoint >= 0x202A && codePoint <= 0x202E) ||
               (codePoint >= 0x2066 && codePoint <= 0x2069);
    }
}
