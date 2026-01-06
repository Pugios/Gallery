namespace app
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("ShowcasePage", typeof(ShowcasePage));
            Routing.RegisterRoute("CollectionPage", typeof(CollectionPage));
            Routing.RegisterRoute("Tester", typeof(Tester));
        }
    }
}
