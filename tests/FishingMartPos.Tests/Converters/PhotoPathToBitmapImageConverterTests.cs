using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FishingMartPos.Converters;
using Xunit;

namespace FishingMartPos.Tests.Converters;

public class PhotoPathToBitmapImageConverterTests
{
    [Fact]
    public void Convert_NullValue_ReturnsNull()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var converter = new PhotoPathToBitmapImageConverter();

            var result = converter.Convert(null, typeof(object), null!, CultureInfo.InvariantCulture);

            Assert.Null(result);
        });
    }

    [Fact]
    public void Convert_NonExistentFile_ReturnsNull()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var converter = new PhotoPathToBitmapImageConverter();

            var result = converter.Convert(@"C:\does\not\exist.jpg", typeof(object), null!, CultureInfo.InvariantCulture);

            Assert.Null(result);
        });
    }

    [Fact]
    public void Convert_ValidFile_ReturnsBitmapImageWithoutLockingSourceFile()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
            CreateMinimalPng(tempPath);
            try
            {
                var converter = new PhotoPathToBitmapImageConverter();

                var result = converter.Convert(tempPath, typeof(object), null!, CultureInfo.InvariantCulture);

                Assert.IsType<BitmapImage>(result);

                // Regression guard: this is the exact scenario that crashed the app —
                // File.Copy(overwrite: true) onto a file still displayed by an Image control.
                var ex = Record.Exception(() =>
                {
                    using var exclusive = File.Open(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                });
                Assert.Null(ex);
            }
            finally
            {
                File.Delete(tempPath);
            }
        });
    }

    private static void CreateMinimalPng(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 1, 1));
        }

        var target = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
