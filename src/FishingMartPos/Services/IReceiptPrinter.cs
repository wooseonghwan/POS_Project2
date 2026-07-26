using FishingMartPos.Models;

namespace FishingMartPos.Services;

public interface IReceiptPrinter
{
    Task<bool> PrintAsync(ReceiptDocument document);
}
