using Gallery2.Models;
using Gallery2.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Abstractions.Controls;

namespace Gallery2.Views.Pages;

/// <summary>
/// Interaction logic for GalleryPage.xaml
/// </summary>
public partial class GalleryPage : INavigableView<GalleryViewModel>
{
    public GalleryViewModel ViewModel { get; }
    public GalleryPage(GalleryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}