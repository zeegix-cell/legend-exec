using System;
using System.Threading.Tasks;
using System.Windows;

namespace Legend
{
    public partial class UpdaterWindow : Window
    {
        readonly UpdateInfo info;

        public UpdaterWindow(UpdateInfo info)
        {
            InitializeComponent();
            this.info = info;
            Detail.Text = "Downloading " + info.Version + "…";
            Loaded += async (s, e) => await RunAsync();
        }

        async Task RunAsync()
        {
            var progress = new Progress<double>(p => Bar.Width = Math.Max(0, ((FrameworkElement)Bar.Parent).ActualWidth * p));
            try
            {
                string staging = await Updater.DownloadAsync(info, progress);
                Detail.Text = "Installing…";
                Updater.Apply(staging);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Detail.Text = "Update failed: " + ex.Message;
                Headline.Text = "Starting current version";
                await Task.Delay(2500);
                Application.Current.Shutdown();
            }
        }
    }
}
