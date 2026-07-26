using FishingMartPos.Models;
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeReceiptPrinter : IReceiptPrinter
{
    private readonly bool _result;
    public List<ReceiptDocument> PrintedDocuments { get; } = new();

    public FakeReceiptPrinter(bool result)
    {
        _result = result;
    }

    public Task<bool> PrintAsync(ReceiptDocument document)
    {
        PrintedDocuments.Add(document);
        return Task.FromResult(_result);
    }
}
