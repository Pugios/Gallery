using Gallery2.ViewModels.Pages;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace Gallery2.Views.Pages;

public partial class SearchPage : INavigableView<SearchViewModel>
{
    public SearchViewModel ViewModel { get; }
    public SearchPage(SearchViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
