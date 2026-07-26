using FishingMartPos.Models;

namespace FishingMartPos.Services;

public sealed class StubReceiptPrinter : IReceiptPrinter
{
    public Task<bool> PrintAsync(ReceiptDocument document) => Task.FromResult(false);
}
