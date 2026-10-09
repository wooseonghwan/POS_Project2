using System.ComponentModel;
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
    private bool _isConfirmingSignature;

    public PosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
        DataContextChanged += PosView_DataContextChanged;
    }

    private void PosView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 서명 캔버스의 잉크는 IsSignatureCaptureVisible이 꺼질 때 항상 한 곳에서 정리한다
        // (지우기/취소/서명완료 각각의 개별 Clear() 호출은 방어적으로 유지 — 3중 안전장치).
        if (e.OldValue is ViewModels.PosViewModel oldVm)
        {
            oldVm.PropertyChanged -= PosViewModel_PropertyChanged;
        }
        if (e.NewValue is ViewModels.PosViewModel newVm)
        {
            newVm.PropertyChanged += PosViewModel_PropertyChanged;
        }
    }

    private void PosViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.PosViewModel.IsSignatureCaptureVisible)
            && sender is ViewModels.PosViewModel vm
            && !vm.IsSignatureCaptureVisible)
        {
            SignatureCanvas.Strokes.Clear();
        }
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
            // 이 숫자는 바코드 스캔버퍼가 이미 소비했다고 표시한다. 표시 안 하면 이벤트가 계속
            // 터널링돼서 현재 포커스를 가진 컨트롤(예: 방금 누른 숫자패드 버튼)에도 전달되는데,
            // 숫자패드 버튼이 포커스를 쥔 상태에서 바코드를 스캔하면 그 바코드 속 숫자/Enter가
            // 거기로 새어들어가 "방금 선택한 상품"의 수량을 계속 바꿔버리는 실매장 버그가 있었다
            // (예: 파워에이드 선택 후 수량 2로 설정 → 과자 바코드 스캔 → 파워에이드 수량이
            // 엉뚱하게 치솟음). 결제 팝업이 열려 있을 때는(IsAnyPaymentPopupOpen) 이 분기 자체를
            // 안 타므로, 할부 "기타개월" 입력창의 정상적인 숫자 입력은 전혀 영향받지 않는다.
            e.Handled = true;
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
        // 위와 같은 이유로, 스캔을 마무리짓는 Enter 키도 포커스를 가진 컨트롤로 새어나가지
        // 않도록 막는다(그 컨트롤이 Enter를 자기 클릭으로 해석하는 경우를 방지).
        e.Handled = true;

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

    // 벤더 문서 예시(Kicc_Bmp2SignDataN)가 128x32 규격을 기준으로 하므로, 화면상 캔버스 실제
    // 크기와 무관하게 항상 이 고정 크기로 축소해 전달한다 — 그래야 네이티브 출력 버퍼 크기를
    // 예측 가능한 범위로 유지할 수 있다 (KiccSignatureConverter의 262144바이트 버퍼와 짝을 이룸).
    private const int SignatureBitmapWidth = 128;
    private const int SignatureBitmapHeight = 32;

    private async void ConfirmSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        // 렌더링/인코딩/게이트웨이 호출이 끝나기 전에 재클릭되면 두 번째 호출의 토스트 타이머가
        // 첫 번째 승인 결과 토스트를 조기에 지워버릴 수 있어 재진입을 막는다.
        if (_isConfirmingSignature) return;
        _isConfirmingSignature = true;
        try
        {
            if (DataContext is not ViewModels.PosViewModel vm) return;

            try
            {
                byte[]? bmpBytes = SignatureCanvas.Strokes.Count > 0 ? RenderSignatureToFixedSizeBmp() : null;

                SignatureCanvas.Strokes.Clear();
                await vm.ConfirmSignatureCommand.ExecuteAsync(bmpBytes);
            }
            catch (Exception)
            {
                // 렌더링/인코딩/게이트웨이 호출 중 예기치 못한 예외가 나더라도 async void 핸들러라
                // 그대로 던지면 프로세스가 죽는다 — 서명 상태만 정리하고 조용히 종료한다.
                SignatureCanvas.Strokes.Clear();
            }
        }
        finally
        {
            _isConfirmingSignature = false;
        }
    }

    private byte[] RenderSignatureToFixedSizeBmp()
    {
        int sourceWidth = Math.Max(1, (int)SignatureCanvas.ActualWidth);
        int sourceHeight = Math.Max(1, (int)SignatureCanvas.ActualHeight);
        var renderTarget = new RenderTargetBitmap(sourceWidth, sourceHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(SignatureCanvas);

        var scaledVisual = new DrawingVisual();
        using (var context = scaledVisual.RenderOpen())
        {
            context.DrawImage(renderTarget, new Rect(0, 0, SignatureBitmapWidth, SignatureBitmapHeight));
        }
        var scaledTarget = new RenderTargetBitmap(
            SignatureBitmapWidth, SignatureBitmapHeight, 96, 96, PixelFormats.Pbgra32);
        scaledTarget.Render(scaledVisual);

        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaledTarget));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
