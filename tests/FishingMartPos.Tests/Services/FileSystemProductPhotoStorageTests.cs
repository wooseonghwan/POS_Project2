using System.IO;
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Services;

public class FileSystemProductPhotoStorageTests
{
    [Fact]
    public void SavePhoto_CopiesFileIntoProductPhotosFolder_ReturnsRelativePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "FishingMartPosTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceFile = Path.Combine(tempRoot, "source.jpg");
            File.WriteAllText(sourceFile, "fake image bytes");

            var storage = new FileSystemProductPhotoStorage(tempRoot);
            var relativePath = storage.SavePhoto("8800000020051", sourceFile);

            Assert.Equal(Path.Combine("ProductPhotos", "8800000020051.jpg"), relativePath);
            Assert.True(File.Exists(Path.Combine(tempRoot, relativePath)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
