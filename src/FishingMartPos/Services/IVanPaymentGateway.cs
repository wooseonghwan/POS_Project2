namespace FishingMartPos.Services;

public interface IVanPaymentGateway
{
    Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request);

    Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request);
}

public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0, string? SignatureHex = null);

public sealed class VanApprovalResult
{
    public required bool IsApproved { get; init; }
    public string? ApprovalNo { get; init; }
    public string? VanCode { get; init; }
    public required string ResponseMessage { get; init; }
}

public sealed record VanCancelRequest(
    string PosCode, string PayType, decimal Amount, int InstallmentMonths,
    string OriginalApprovalNo, string OriginalApprovalDateYyMmDd);

public sealed class VanCancelResult
{
    public required bool IsCancelled { get; init; }
    public required string ResponseMessage { get; init; }
}
