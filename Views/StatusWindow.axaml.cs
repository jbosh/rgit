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
        private string? gitBeforeVersion;
        private string? gitAfterVersion;
        private bool isLogs;

        public StatusWindow()
            : this(null)
        {
        }

        public StatusWindow(CommandLineArgs.StatusArgs? args)
        {
            this.InitializeComponent();

            this.isLogs = args?.BeforeVersion != null;
            this.StatusPanel.IsLogs = this.isLogs;
            this.StatusPanel.Paths = args?.Paths;
            this.gitBeforeVersion = args?.BeforeVersion;
            this.gitAfterVersion = args?.AfterVersion;

            var settings = this.isLogs ? Settings.LogsStatusWindow : Settings.StatusWindow;
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
            this.StashButton = this.FindControl<Button>(nameof(this.StashButton));
            this.CommitButton = this.FindControl<Button>(nameof(this.CommitButton));
            this.RefreshButton = this.FindControl<Button>(nameof(this.RefreshButton));
            this.OkButton = this.FindControl<Button>(nameof(this.OkButton));

            this.StatusPanel.SetVersion(args?.BeforeVersion, args?.AfterVersion);
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
            if (this.StatusPanel.IsLogs)
            {
                this.BranchText.Text = this.gitAfterVersion switch
                {
                    "WORKING" => $"Branch: {this.gitBeforeVersion}",
                    "STAGING" => $"Comparing: {this.gitBeforeVersion}..Staged Files",
                    _ => $"Comparing: {this.gitBeforeVersion}..{this.gitAfterVersion}",
                };
            }
            else
            {
                this.BranchText.Text = $"Branch: {this.model?.Repository.CurrentBranch()}";
            }

            this.StashButton.IsVisible = !this.StatusPanel.IsLogs;
            this.CommitButton.IsVisible = !this.StatusPanel.IsLogs;
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
            var settings = this.isLogs ? Settings.LogsStatusWindow : Settings.StatusWindow;
            settings.Maximized = this.WindowState == WindowState.Maximized;
            if (!settings.Maximized)
                settings.Bounds = new Rect(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
            settings.StatusColumnWidths = this.StatusPanel.GetColumnWidths().ToArray();
        }
    }
}