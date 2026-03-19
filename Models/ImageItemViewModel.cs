using System.ComponentModel;

namespace app.Models
{
    public class ImageItemViewModel : INotifyPropertyChanged
    {
        public string Path { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        private bool _isLiked;
        public bool IsLiked
        {
            get => _isLiked;
            set
            {
                if (_isLiked == value) return;
                _isLiked = value;
                OnPropertyChanged(nameof(IsLiked));
            }
        }

        public ImageItemViewModel(string path) => Path = path;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
