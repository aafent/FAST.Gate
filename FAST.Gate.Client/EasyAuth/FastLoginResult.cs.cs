// FastLoginResult.cs
namespace FAST.Gate.Client;

public enum FastLoginStatus
{
    Success,
    Cancelled,
    InvalidGrant,
    Error
}

public sealed class FastLoginResult
{
    public FastLoginStatus Status { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorDescription { get; init; }
}