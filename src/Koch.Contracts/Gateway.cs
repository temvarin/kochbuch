using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koch.Contracts;

/// <summary>
/// How the gateway tells the backend who is asking. The backend is internal-only, so it trusts this header;
/// the gateway strips any copy a caller sent before setting the real one.
/// </summary>
public static class ForwardedUser
{
    public const string Header = "X-Koch-User";
}

/// <summary>The answer of <c>/api/whoami</c>.</summary>
public sealed record WhoAmI(string Id, string Name, bool Member);

/// <summary>
/// The answer of <c>POST /api/photos/upload-slot</c>: a short-lived valet-key SAS URL for uploading straight to Blob Storage.
/// </summary>
public sealed record UploadSlot(Guid Photo, Uri UploadUrl, DateTimeOffset Expires, long MaxBytes);

/// <summary>Source-generated JSON serializer for gateway responses (no reflection in Blazor WASM).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WhoAmI))]
[JsonSerializable(typeof(UploadSlot))]
public sealed partial class GatewayJson : JsonSerializerContext;
