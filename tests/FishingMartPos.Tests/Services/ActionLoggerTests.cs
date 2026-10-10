using System.IO;
using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class ActionLoggerTests
{
    private static (ActionLogger logger, FakeActionLogRepository repo, CurrentSession session) Create()
    {
        var repo = new FakeActionLogRepository();
        var session = new CurrentSession();
        return (new ActionLogger(repo, session), repo, session);
    }

    [Fact]
    public async Task LogAsync_FillsInCurrentStaffAndTerminalFromSession()
    {
        var (logger, repo, session) = Create();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });

        await logger.LogAsync("PRODUCT_SAVE", "상품등록: 바코드=12345");

        var entry = Assert.Single(repo.Inserted);
        Assert.Equal("PRODUCT_SAVE", entry.ActionType);
        Assert.Equal("상품등록: 바코드=12345", entry.ActionDetail);
        Assert.Equal("1", entry.PosCd);
        Assert.Equal("ADMIN1", entry.StaffCd);
        Assert.Equal("SUCCESS", entry.ResultStatus);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact]
    public async Task LogAsync_WhenNotSignedIn_LeavesPosAndStaffNull()
    {
        var (logger, repo, _) = Create();

        await logger.LogAsync("LOGIN", "로그인 실패", success: false);

        var entry = Assert.Single(repo.Inserted);
        Assert.Null(entry.PosCd);
        Assert.Null(entry.StaffCd);
        Assert.Equal("FAIL", entry.ResultStatus);
    }

    [Fact]
    public async Task LogAsync_WithFailureAndErrorMessage_StoresBoth()
    {
        var (logger, repo, _) = Create();

        await logger.LogAsync("SALE_CARD", "카드결제 승인거절", success: false, errorMessage: "한도초과");

        var entry = Assert.Single(repo.Inserted);
        Assert.Equal("FAIL", entry.ResultStatus);
        Assert.Equal("한도초과", entry.ErrorMessage);
    }

    [Fact]
    public async Task LogAsync_WhenRepositoryThrows_DoesNotThrow()
    {
        var (logger, repo, _) = Create();
        repo.ThrowOnInsert = true;

        // 로깅 자체의 DB 실패가 호출자(결제/저장 로직)로 전파되면 안 된다 — 예외 없이 끝나야 한다.
        await logger.LogAsync("PRODUCT_SAVE", "상품등록");

        Assert.Empty(repo.Inserted);
    }

    [Fact]
    public async Task LogAsync_WhenRepositoryThrows_WritesDiagnosticFileNextToExe()
    {
        // action_log_tb가 아직 없거나 DB가 끊겼을 때 "왜 안 쌓이는지"를 현장에서 바로 확인할 수
        // 있도록, 원인을 exe 옆 action-log-errors.txt에 남긴다.
        var diagnosticPath = Path.Combine(AppContext.BaseDirectory, "action-log-errors.txt");
        if (File.Exists(diagnosticPath)) File.Delete(diagnosticPath);
        var (logger, repo, _) = Create();
        repo.ThrowOnInsert = true;

        await logger.LogAsync("PRODUCT_SAVE", "상품등록");

        Assert.True(File.Exists(diagnosticPath));
        var content = await File.ReadAllTextAsync(diagnosticPath);
        Assert.Contains("PRODUCT_SAVE", content);
        Assert.Contains("DB 연결 끊김(테스트용)", content);

        File.Delete(diagnosticPath);
    }
}
