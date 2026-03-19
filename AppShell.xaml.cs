namespace app
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("CollectionPage", typeof(CollectionPage));
        }
    }
}
