using System.Net.Http;
using System.Text.RegularExpressions;

namespace FishingMartPos.Services.Kicc;

/// <summary>
/// EasyCard2(로컬 HTTP/JSONP 서버, 기본 포트 8080)를 통해 카드결제를 처리한다.
/// 이 매장은 "단말기승인(부착형)" 방식이 아니라 "PC결제(IP기반)" 방식으로 설정되어 있어
/// (EasyCard2 환경설정의 "단말기연결: 사용안함" + 신용 IP/PORT 확인됨), KiccPos.dll을 직접
/// P/Invoke로 호출하는 대신 이미 떠 있는 EasyCard2 프로세스에 로컬 HTTP 요청을 보낸다.
/// 요청/응답 필드 형식은 공식 문서가 없어, 실제 운영 로그(EasyCard2\Log\Slog*.txt)에서
/// 역으로 확인한 값이다. 카드 읽기/PIN입력/서명 UI는 EasyCard2가 자체적으로 처리한다.
/// </summary>
public sealed class KiccHttpVanPaymentGateway : IVanPaymentGateway
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly int _port;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType;

    public KiccHttpVanPaymentGateway(int port, IReadOnlyDictionary<string, KiccMerchantConfig> merchantsByPayType)
    {
        _port = port;
        _merchantsByPayType = merchantsByPayType;
    }

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var transNo = BuildTransNo();
        string installmentCode = request.InstallmentMonths.ToString("00");
        string req = $"D1^^{(int)request.Amount}^{installmentCode}^^^^{transNo}^WEB{transNo}^^{merchant.Tid}^40^A^^";

        var fields = await SendAsync(req);
        if (fields is null)
        {
            return new VanApprovalResult { IsApproved = false, ResponseMessage = "카드단말 통신 오류" };
        }

        if (fields.GetValueOrDefault("SUC") != "00")
        {
            return new VanApprovalResult { IsApproved = false, ResponseMessage = fields.GetValueOrDefault("MSG") ?? "카드 승인 거절" };
        }

        if (fields.GetValueOrDefault("RS18") == "Y")
        {
            return new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = fields.GetValueOrDefault("RS09"),
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            };
        }

        string? failMessage = fields.GetValueOrDefault("RS16")?.Trim();
        return new VanApprovalResult
        {
            IsApproved = false,
            ResponseMessage = string.IsNullOrEmpty(failMessage) ? "카드 승인 거절" : failMessage,
        };
    }

    public async Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var transNo = BuildTransNo();
        string installmentCode = request.InstallmentMonths.ToString("00");
        string req = $"D4^^{(int)request.Amount}^{installmentCode}^{request.OriginalApprovalDateYyMmDd}^{request.OriginalApprovalNo}^^{transNo}^^^{merchant.Tid}^30";

        var fields = await SendAsync(req);
        if (fields is null)
        {
            return new VanCancelResult { IsCancelled = false, ResponseMessage = "카드단말 통신 오류" };
        }

        if (fields.GetValueOrDefault("SUC") == "00" && fields.GetValueOrDefault("RS18") == "Y")
        {
            return new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
        }

        return new VanCancelResult { IsCancelled = false, ResponseMessage = "카드 취소 거절" };
    }

    private async Task<Dictionary<string, string>?> SendAsync(string req)
    {
        try
        {
            string callback = $"pos{Guid.NewGuid():N}";
            // EasyCard2의 실제 운영 로그에 남은 요청들이 '^'를 퍼센트 인코딩 없이 그대로 보내고 있어
            // (단순한 임베디드 HTTP 서버로 보임), 동일하게 원문 그대로 보낸다.
            string url = $"http://127.0.0.1:{_port}/?callback={callback}&REQ={req}";
            string body = await Http.GetStringAsync(url);
            return ParseJsonpFields(body);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseJsonpFields(string body)
    {
        var result = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(body, @"'([A-Za-z0-9]+)'\s*:\s*'([^']*)'"))
        {
            result[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return result;
    }

    private static string BuildTransNo() => DateTime.Now.ToString("MMddHHmmss");
}
