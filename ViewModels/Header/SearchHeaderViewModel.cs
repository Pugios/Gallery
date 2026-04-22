using System.Windows.Input;

namespace Gallery2.ViewModels.Header;
public partial class SearchHeaderViewModel(ICommand reindexFacesCommand, ICommand returnToGridCommand, ICommand dissolvePersonCommand) : ObservableObject
{
    public ICommand ReindexFacesCommand    { get; } = reindexFacesCommand;
    public ICommand ReturnToGridCommand    { get; } = returnToGridCommand;
    public ICommand DissolvePersonCommand  { get; } = dissolvePersonCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotIndexing))]
    private bool _isIndexing;
    public bool IsNotIndexing => !IsIndexing;

    [ObservableProperty]
    private bool _isPersonSelected;
}