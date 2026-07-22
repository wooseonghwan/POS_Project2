using Microsoft.Win32;

namespace FishingMartPos.Services;

public sealed class WpfPhotoPicker : IPhotoPicker
{
    public string? PickPhoto()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "이미지 파일 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
