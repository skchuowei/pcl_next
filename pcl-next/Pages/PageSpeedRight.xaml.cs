
namespace PCL
{
    public partial class PageSpeedRight
    {
        public PageSpeedRight()
        {
            InitializeComponent();
            this.Loaded += (_, __) => Init();
        }

        private void Init()
        {
            this.PanBack.ScrollToHome();
        }

    }
}