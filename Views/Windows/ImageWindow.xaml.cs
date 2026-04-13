using Gallery2.ViewModels.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace Gallery2.Views.Windows;

public partial class ImageWindow : FluentWindow
{
    public ImageWindowViewModel ViewModel { get; }

    public ImageWindow(ImageWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e) => DrawOverlay();

    private void DrawOverlay()
    {
        FaceOverlay.Children.Clear();
        foreach (var rect in ViewModel.FaceRects)
        {
            var r = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(r, rect.X);
            Canvas.SetTop(r, rect.Y);
            FaceOverlay.Children.Add(r);
        }
    }
}
