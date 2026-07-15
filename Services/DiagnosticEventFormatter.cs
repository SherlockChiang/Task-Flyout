using System;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class DiagnosticEventFormatter
    {
        private const int MaximumExceptionDepth = 4;

        public static string FormatException(
            string operation,
            Exception exception,
            DateTimeOffset? timestamp = null,
            string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(exception);

            var builder = new StringBuilder();
            builder.AppendLine($"timestamp={timestamp ?? DateTimeOffset.Now:O}");
            builder.AppendLine($"operation={NormalizeOperation(operation)}");
            builder.AppendLine($"correlation_id={NormalizeCorrelationId(correlationId)}");

            int depth = 0;
            for (Exception? current = exception; current != null && depth < MaximumExceptionDepth; current = current.InnerException)
            {
                builder.AppendLine($"exception_{depth}_type={current.GetType().FullName ?? current.GetType().Name}");
                builder.AppendLine($"exception_{depth}_hresult=0x{current.HResult:X8}");
                depth++;
            }

            builder.AppendLine($"exception_depth={depth}");
            return builder.ToString();
        }

        private static string NormalizeOperation(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation)) return "unknown";

            var result = new StringBuilder(Math.Min(operation.Length, 64));
            foreach (char character in operation)
            {
                if (result.Length >= 64) break;
                if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                    result.Append(character);
            }
            return result.Length == 0 ? "unknown" : result.ToString();
        }

        private static string NormalizeCorrelationId(string? correlationId)
        {
            if (Guid.TryParse(correlationId, out var parsed))
                return parsed.ToString("N");
            return Guid.NewGuid().ToString("N");
        }
    }
}
