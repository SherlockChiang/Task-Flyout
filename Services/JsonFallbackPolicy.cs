using System;

namespace Task_Flyout.Services
{
    internal static class JsonFallbackPolicy
    {
        public static T DeserializeOrDefault<T>(string? json, Func<string, T?> deserialize, Func<T> createDefault)
        {
            if (string.IsNullOrWhiteSpace(json))
                return createDefault();

            try
            {
                return deserialize(json) ?? createDefault();
            }
            catch
            {
                return createDefault();
            }
        }
    }
}
