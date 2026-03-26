using Gallery2.ViewModels.Windows;
using System.Windows.Controls.Primitives;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Gallery2.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();
            SetPageService(navigationViewPageProvider);

            navigationService.SetNavigationControl(RootNavigation);
            ViewModel.SetMenuItems(RootNavigation.MenuItems);

            GalleryNavItem.PreviewMouseLeftButtonDown += (_, _) => ViewModel.ClearActiveFolderCommand.Execute(null);

            RootNavigation.Loaded += (_, _) => PaneSplitter.Margin = new Thickness(RootNavigation.OpenPaneLength, 0, 0, 0);
        }

        private void PaneSplitter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var newWidth = RootNavigation.OpenPaneLength + e.HorizontalChange;
            RootNavigation.OpenPaneLength = Math.Clamp(newWidth, 150, 500);
            PaneSplitter.Margin = new Thickness(RootNavigation.OpenPaneLength, 0, 0, 0);
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }
    }
}
