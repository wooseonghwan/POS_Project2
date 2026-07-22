namespace FishingMartPos.Services;

public interface IProductPhotoStorage
{
    string SavePhoto(string barcode, string sourceFilePath);
}
