using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TrackSwap.Runtime;

internal static class RuntimeJson
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        MissingMemberHandling = MissingMemberHandling.Error
    };
}
