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
        OpenWeather,
        ReportMountReadiness
    }

    internal enum WeatherCompanionMountState
    {
        None,
        Ready,
        Lost
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
        UnsupportedCommand,
        InvalidArguments
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
        WeatherCompanionCommand Command,
        uint ControllerNonce = 0,
        ulong MountGeneration = 0,
        WeatherCompanionMountState MountState = WeatherCompanionMountState.None);

    internal readonly record struct WeatherCompanionMountReadinessReport(
        uint ClientProcessId,
        uint ControllerNonce,
        ulong MountGeneration,
        WeatherCompanionMountState MountState);

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
            uint? controllerNonce = null;
            ulong? mountGeneration = null;
            string? mountState = null;
            bool sawVersion = false;
            bool sawCommand = false;
            bool sawControllerNonce = false;
            bool sawMountGeneration = false;
            bool sawMountState = false;
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
                    bool isControllerNonce = reader.ValueTextEquals("controllerNonce"u8);
                    bool isMountGeneration = reader.ValueTextEquals("mountGeneration"u8);
                    bool isMountState = reader.ValueTextEquals("mountState"u8);
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
                    else if (isControllerNonce)
                    {
                        if (sawControllerNonce) throw new JsonException();
                        if (reader.TokenType != JsonTokenType.Number ||
                            !reader.TryGetUInt32(out uint parsedNonce))
                            throw new JsonException();
                        sawControllerNonce = true;
                        controllerNonce = parsedNonce;
                    }
                    else if (isMountGeneration)
                    {
                        if (sawMountGeneration) throw new JsonException();
                        if (reader.TokenType != JsonTokenType.Number ||
                            !reader.TryGetUInt64(out ulong parsedGeneration))
                            throw new JsonException();
                        sawMountGeneration = true;
                        mountGeneration = parsedGeneration;
                    }
                    else if (isMountState)
                    {
                        if (sawMountState) throw new JsonException();
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException();
                        sawMountState = true;
                        mountState = reader.GetString();
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
            else if (string.Equals(command, "report-mount-readiness", StringComparison.Ordinal))
                parsedCommand = WeatherCompanionCommand.ReportMountReadiness;
            else
            {
                error = WeatherCompanionProtocolError.UnsupportedCommand;
                return false;
            }

            bool hasMountArguments = sawControllerNonce ||
                sawMountGeneration || sawMountState;
            if (parsedCommand != WeatherCompanionCommand.ReportMountReadiness)
            {
                if (hasMountArguments)
                {
                    error = WeatherCompanionProtocolError.InvalidArguments;
                    return false;
                }

                request = new WeatherCompanionRequest(version.Value, parsedCommand);
                error = WeatherCompanionProtocolError.None;
                return true;
            }

            WeatherCompanionMountState parsedMountState = mountState switch
            {
                "ready" => WeatherCompanionMountState.Ready,
                "lost" => WeatherCompanionMountState.Lost,
                _ => WeatherCompanionMountState.None
            };
            if (!controllerNonce.HasValue || controllerNonce.Value == 0 ||
                !mountGeneration.HasValue || mountGeneration.Value == 0 ||
                parsedMountState == WeatherCompanionMountState.None)
            {
                error = WeatherCompanionProtocolError.InvalidArguments;
                return false;
            }

            request = new WeatherCompanionRequest(
                version.Value,
                parsedCommand,
                controllerNonce.Value,
                mountGeneration.Value,
                parsedMountState);
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

        public static bool IsSnapshotFresh(
            WeatherCompanionSnapshot snapshot,
            DateTimeOffset nowUtc,
            TimeSpan maxAge)
        {
            if (maxAge <= TimeSpan.Zero || snapshot.UpdatedUtc == DateTimeOffset.MinValue)
                return false;

            DateTimeOffset updatedUtc = snapshot.UpdatedUtc.ToUniversalTime();
            nowUtc = nowUtc.ToUniversalTime();
            if (updatedUtc > nowUtc + TimeSpan.FromMinutes(5)) return false;
            return nowUtc - updatedUtc <= maxAge;
        }

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
