using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using rgit.Controls;
using rgit.ViewModels;

namespace rgit.Views
{
    public partial class StatusWindow : Window
    {
        private GitViewModel? model;

        public StatusWindow()
            : this(null)
        {
        }

        public StatusWindow(CommandLineArgs.StatusArgs? args)
        {
            this.InitializeComponent();

            var settings = Settings.StatusWindow;
            if (settings.Bounds != null)
            {
                var bounds = settings.Bounds.Value;
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Position = PixelPoint.FromPoint(new Point(bounds.X, bounds.Y), 1);
                this.Width = bounds.Width;
                this.Height = bounds.Height;
            }

            if (settings.StatusColumnWidths != null)
            {
                this.StatusPanel.SetColumnWidths(settings.StatusColumnWidths);
            }

            if (settings.Maximized)
            {
                this.WindowState = WindowState.Maximized;
            }

            this.StatusPanel = this.FindControl<StatusPanel>(nameof(this.StatusPanel));
            this.BranchText = this.FindControl<TextBlock>(nameof(this.BranchText));

            this.StatusPanel.PathSpec = args?.Path;
            this.BranchText.Text = $"Branch: {this.model?.Repository.CurrentBranch()}";
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            this.model = this.DataContext as GitViewModel;
            this.Title = this.model?.Repository.WorkingDirectory() ?? this.Title;
            this.Refresh();
        }

        private void Refresh_OnClick(object? sender, RoutedEventArgs e)
        {
            this.Refresh();
        }

        private void Refresh()
        {
            this.BranchText.Text = $"Branch: {this.model?.Repository.CurrentBranch()}";
            this.StatusPanel.Refresh();
        }

        private void Ok_OnClick(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5
                || (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
                || (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                this.Refresh();
            }

            if (e.Key == Key.Escape)
                this.Close();
        }

        private async void Commit_OnClick(object? sender, RoutedEventArgs e)
        {
            var commitWindow = new CommitWindow
            {
                DataContext = this.model,
            };
            await commitWindow.ShowDialog(this);
            this.Refresh();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            var settings = Settings.StatusWindow;
            settings.Maximized = this.WindowState == WindowState.Maximized;
            if (!settings.Maximized)
                settings.Bounds = new Rect(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
            settings.StatusColumnWidths = this.StatusPanel.GetColumnWidths().ToArray();
        }
    }
}