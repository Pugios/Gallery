using System.Windows.Input;

namespace Gallery2.ViewModels.Header;
public partial class SearchHeaderViewModel(ICommand reindexFacesCommand) : ObservableObject
{
    public ICommand ReindexFacesCommand { get; } = reindexFacesCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotIndexing))]
    private bool _isIndexing;

    public bool IsNotIndexing => !IsIndexing;
}