namespace FishingMartPos.Services;

public interface IVanPaymentGateway
{
    Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request);
}

public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0);

public sealed class VanApprovalResult
{
    public required bool IsApproved { get; init; }
    public string? ApprovalNo { get; init; }
    public string? VanCode { get; init; }
    public required string ResponseMessage { get; init; }
}
