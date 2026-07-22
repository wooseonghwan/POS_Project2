using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace FishingMartPos.Views;

public partial class PosView : UserControl
{
    private readonly StringBuilder _scanBuffer = new();

    public PosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    private void PosView_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 1 && char.IsDigit(e.Text[0]))
        {
            _scanBuffer.Append(e.Text);
        }
    }

    private void PosView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
}
