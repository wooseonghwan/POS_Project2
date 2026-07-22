using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductPhotoStorage : IProductPhotoStorage
{
    public List<(string Barcode, string SourceFilePath)> SaveCalls { get; } = new();

    public string SavePhoto(string barcode, string sourceFilePath)
    {
        SaveCalls.Add((barcode, sourceFilePath));
        return $"ProductPhotos/{barcode}.jpg";
    }
}
