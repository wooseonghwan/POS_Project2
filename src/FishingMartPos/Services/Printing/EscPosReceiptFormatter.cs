using System.IO;
using System.Text;
using FishingMartPos.Models;

namespace FishingMartPos.Services.Printing;

/// <summary>
/// ReceiptDocument를 ESC/POS raw 명령 바이트로 변환한다. 한글은 코드페이지 949(CP949, EUC-KR 상위호환)로
/// 인코딩한다 — 국내 영수증 프린터가 통상 이 코드페이지를 기본으로 지원한다.
/// </summary>
public static class EscPosReceiptFormatter
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const string Divider = "--------------------------------";
    private const string StoreName = "대원낚시마트";
    private const string StoreAddress = "충남 태안군 남면 한바위길 54-10";
    private const string FooterText = "이용해 주셔서 감사합니다";

    static EscPosReceiptFormatter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static byte[] Build(ReceiptDocument document)
    {
        var enc = Encoding.GetEncoding(949);
        using var ms = new MemoryStream();

        void Cmd(params byte[] bytes) => ms.Write(bytes, 0, bytes.Length);
        void Text(string s) { var b = enc.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Line(string s = "") { Text(s); Cmd((byte)'\n'); }
        void Center(string s) { Cmd(Esc, (byte)'a', 1); Line(s); Cmd(Esc, (byte)'a', 0); }

        Cmd(Esc, (byte)'@'); // 초기화

        // 머리말은 고정 문구(상호 + 주소). 영수증설정 화면 값은 사용하지 않는다.
        Cmd(Esc, (byte)'E', 1);
        Center(StoreName);
        Cmd(Esc, (byte)'E', 0);
        Center(StoreAddress);
        Line();

        Cmd(Esc, (byte)'E', 1);
        Center("영 수 증");
        Cmd(Esc, (byte)'E', 0);

        if (document.IsCancelled)
        {
            // 취소된 거래를 재발행할 때는 정상 영수증과 절대 헷갈리지 않도록 굵게 눈에 띄게 찍는다.
            Cmd(Esc, (byte)'E', 1);
            Center("*** 취소된 거래 ***");
            Cmd(Esc, (byte)'E', 0);
        }

        if (document.SaleDateTime is DateTime saleAt)
        {
            Line($"일    시: {saleAt:yyyy-MM-dd HH:mm:ss}");
        }
        if (document.SaleNo is long saleNo)
        {
            Line($"영수번호: {saleNo}");
        }
        Line(Divider);

        foreach (var line in document.Lines)
        {
            Line(line.ProductName);
            Line($"  {line.Qty} x {line.UnitPrice:N0}원 = {line.LineAmt:N0}원");
        }

        Line(Divider);
        Cmd(Esc, (byte)'E', 1); // 굵게
        Line($"합계금액: {document.TotalAmt:N0}원");
        Cmd(Esc, (byte)'E', 0); // 굵게 해제
        Line($"결제수단: {document.PayTypeLabel}");

        if (!string.IsNullOrEmpty(document.VanApprovalNo))
        {
            Line($"승인번호: {document.VanApprovalNo}");
            Line($"할    부: {(document.InstallmentMonths <= 0 ? "일시불" : $"{document.InstallmentMonths}개월")}");
        }

        if (!string.IsNullOrEmpty(document.CashReceiptTypeLabel))
        {
            Line($"현금영수증: {document.CashReceiptTypeLabel}");
            if (!string.IsNullOrEmpty(document.CashReceiptApprovalNo))
            {
                Line($"현금영수증 승인번호: {document.CashReceiptApprovalNo}");
            }
        }

        // 하단 문구도 고정.
        Line();
        Center(FooterText);

        // 마지막 내용이 용지 끝에서 잘리지 않도록 충분히 밀어낸 뒤 절단한다.
        for (int i = 0; i < 5; i++) Line();
        Cmd(Gs, (byte)'V', 1); // 부분 절단

        return ms.ToArray();
    }
}
