using System.Collections.Generic;
using System.Text.Json.Serialization;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout
{
    [JsonSerializable(typeof(AppCache))]
    [JsonSerializable(typeof(List<ConnectedAccountInfo>))]
    [JsonSerializable(typeof(List<MailAccount>))]
    [JsonSerializable(typeof(Dictionary<string, List<AgendaItem>>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}
