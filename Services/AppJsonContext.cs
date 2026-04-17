using System.Collections.Generic;
using System.Text.Json.Serialization;
using Task_Flyout.Models;

namespace Task_Flyout
{
    [JsonSerializable(typeof(AppCache))]
    [JsonSerializable(typeof(List<ConnectedAccountInfo>))]
    [JsonSerializable(typeof(Dictionary<string, List<AgendaItem>>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}
