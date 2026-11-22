using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Gateway.Contracts;

/// <summary>The serializer for the gateway's own answers, generated at build time (no reflection in the browser).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WhoAmI))]
[JsonSerializable(typeof(UploadSlot))]
public sealed partial class GatewayJson : JsonSerializerContext;
