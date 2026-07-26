using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FishingMartPos.Views;

public partial class PosView : UserControl
{
    private readonly StringBuilder _scanBuffer = new();

    public PosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    private bool IsAnyPaymentPopupOpen =>
        DataContext is ViewModels.PosViewModel vm && (vm.IsCashConfirmVisible || vm.IsCardPaymentVisible);

    private void PosView_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsAnyPaymentPopupOpen)
        {
            return;
        }

        if (e.Text.Length == 1 && char.IsDigit(e.Text[0]))
        {
            _scanBuffer.Append(e.Text);
        }
    }

    private void PosView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsAnyPaymentPopupOpen)
        {
            _scanBuffer.Clear();
            return;
        }

        if (e.Key != Key.Enter || _scanBuffer.Length == 0)
        {
            return;
        }

        string barcode = _scanBuffer.ToString();
        _scanBuffer.Clear();

        if (DataContext is ViewModels.PosViewModel vm)
        {
            vm.ScanBarcodeCommand.Execute(barcode);
        }
    }

    private void InstallmentMonthsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void ClearSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureCanvas.Strokes.Clear();
    }

    private void CancelSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureCanvas.Strokes.Clear();
        if (DataContext is ViewModels.PosViewModel vm)
        {
            vm.CancelSignatureCommand.Execute(null);
        }
    }

    private async void ConfirmSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.PosViewModel vm) return;

        byte[]? bmpBytes = null;
        if (SignatureCanvas.Strokes.Count > 0)
        {
            var renderTarget = new RenderTargetBitmap(
                (int)SignatureCanvas.ActualWidth, (int)SignatureCanvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(SignatureCanvas);

            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bmpBytes = stream.ToArray();
        }

        SignatureCanvas.Strokes.Clear();
        await vm.ConfirmSignatureCommand.ExecuteAsync(bmpBytes);
    }
}
