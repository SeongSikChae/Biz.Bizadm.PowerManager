using System.Text.Json.Serialization;

namespace Biz.Bizadm.PowerManager.Core.Devices
{
    // 장치 본문은 snake_case이고 값이 모두 문자열("verify":"true" 포함).
    // 이름을 고정하지 않으면 ASP.NET Core 기본 camelCase로 매핑돼 값이 조용히 null이 된다.
    // [ApiController]가 non-nullable 참조 속성을 암묵적 [Required]로 보지 않도록 모두 nullable로 둔다.

    /// <summary>
    /// <c>POST /api/v1/devices/register/create</c> 요청 본문.
    /// </summary>
    public sealed record DeviceRegisterRequest(
        [property: JsonPropertyName("account")] DeviceAccount? Account,
        [property: JsonPropertyName("device")] DeviceInfo? Device);

    public sealed record DeviceAccount(
        [property: JsonPropertyName("user_id")] string? UserId);

    public sealed record DeviceInfo(
        [property: JsonPropertyName("mac")] string? Mac,
        [property: JsonPropertyName("model_id")] string? ModelId,
        [property: JsonPropertyName("company")] string? Company,
        [property: JsonPropertyName("lati")] string? Latitude,
        [property: JsonPropertyName("long")] string? Longitude,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("verify")] string? Verify);
}
