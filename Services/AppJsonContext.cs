using System.Collections.Generic;
using System.Text.Json.Serialization;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout
{
    [JsonSerializable(typeof(AppCache))]
    [JsonSerializable(typeof(List<ConnectedAccountInfo>))]
    [JsonSerializable(typeof(List<MailAccount>))]
    [JsonSerializable(typeof(MailPersistentCache))]
    [JsonSerializable(typeof(RssCache))]
    [JsonSerializable(typeof(WeatherCacheEnvelope))]
    [JsonSerializable(typeof(WeatherCacheStore))]
    [JsonSerializable(typeof(WeatherLocationSettings))]
    [JsonSerializable(typeof(WeatherFavoritesStore))]
    [JsonSerializable(typeof(HashSet<string>))]
    [JsonSerializable(typeof(Dictionary<string, List<AgendaItem>>))]
    [JsonSerializable(typeof(ICloudCredentialEnvelope))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}
