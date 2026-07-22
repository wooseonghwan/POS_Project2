using System.IO;

namespace FishingMartPos.Services;

public sealed class FileSystemProductPhotoStorage : IProductPhotoStorage
{
    private readonly string _baseDirectory;

    public FileSystemProductPhotoStorage(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public string SavePhoto(string barcode, string sourceFilePath)
    {
        var extension = Path.GetExtension(sourceFilePath);
        var relativePath = Path.Combine("ProductPhotos", barcode + extension);
        var destinationPath = Path.Combine(_baseDirectory, relativePath);

        Directory.CreateDirectory(Path.Combine(_baseDirectory, "ProductPhotos"));
        File.Copy(sourceFilePath, destinationPath, overwrite: true);

        return relativePath.Replace('\\', '/');
    }
}
