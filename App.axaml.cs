using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LibGit2Sharp;
using rgit.ViewModels;
using rgit.Views;

namespace rgit
{
    public class App : Application
    {
        public Repository? Repository { get; set; }
        public LaunchCommand LaunchCommand { get; set; }
        public CommandLineArgs? Args { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (this.Repository == null)
                    throw new Exception($"Need to set {nameof(Repository)} before initialization.");
                var model = new GitViewModel(this.Repository);
                desktop.MainWindow = this.LaunchCommand switch
                {
                    LaunchCommand.Commit => new CommitWindow { DataContext = model },
                    LaunchCommand.Log => new LogWindow(this.Args?.Log) { DataContext = model },
                    LaunchCommand.Status => new StatusWindow(this.Args?.Status) { DataContext = model },
                    _ => throw new ArgumentOutOfRangeException(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}