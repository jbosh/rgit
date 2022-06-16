using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibGit2Sharp;
using rgit.Controls;
using rgit.ViewModels;

namespace rgit.Views;

public partial class LogWindow : Window
{
    private GitViewModel? model;
    private Repository? repository;
    private CommandLineArgs.LogArgs? args;

    public LogWindow()
        : this(null)
    {
    }

    public LogWindow(CommandLineArgs.LogArgs? args)
    {
        this.args = args;
        this.InitializeComponent();

        var settings = Settings.LogWindow;
        if (settings.MainRowDefinitions != null && settings.MainRowDefinitions.Length == this.MainGrid.RowDefinitions.Count)
        {
            for (var i = 0; i < settings.MainRowDefinitions.Length; i++)
            {
                // Skip absolute because those are hard coded as designed.
                if (!this.MainGrid.RowDefinitions[i].Height.IsStar)
                    continue;

                this.MainGrid.RowDefinitions[i].Height = GridLength.Parse(settings.MainRowDefinitions[i]);
            }
        }

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

        if (settings.LogColumnWidths != null)
        {
            this.LogPanel.SetColumnWidths(settings.LogColumnWidths);
        }

        if (settings.Maximized)
        {
            this.WindowState = WindowState.Maximized;
        }

        this.LogPanel = this.FindControl<LogPanel>(nameof(this.LogPanel));
        this.CommitMessageBox = this.FindControl<TextBox>(nameof(this.CommitMessageBox));
        this.StatusPanel = this.FindControl<StatusPanel>(nameof(this.StatusPanel));
        this.BranchText = this.FindControl<TextBlock>(nameof(this.BranchText));

        this.StatusPanel.IsLogs = true;
        this.LogPanel.OnSelectionChanged += this.LogPanelOnOnSelectionChanged;
        this.LogPanel.Branch = this.args?.Branch;
        this.LogPanel.PathSpec = this.args?.Path;
        this.BranchText.Text = $"Branch: {this.args?.Branch ?? this.repository?.CurrentBranch()}";
    }

    public void Refresh()
    {
        this.BranchText.Text = $"Branch: {this.args?.Branch ?? this.repository?.CurrentBranch()}";
        this.StatusPanel.Refresh();
        _ = this.LogPanel.Refresh();
    }

    private void LogPanelOnOnSelectionChanged(string? sha)
    {
        if (this.repository == null || sha == null)
        {
            this.CommitMessageBox.Text = string.Empty;
            this.StatusPanel.SetVersion(null, null);
        }
        else
        {
            var commit = this.repository.Lookup<Commit>(sha);
            this.CommitMessageBox.Text = $"SHA: {commit.Sha}\n{commit.Message}";
            this.StatusPanel.SetVersion(commit.Sha, null);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        this.model = this.DataContext as GitViewModel;
        this.repository = this.model?.Repository;
        this.Title = this.model?.Repository.WorkingDirectory() ?? this.Title;
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var settings = Settings.LogWindow;
        settings.Maximized = this.WindowState == WindowState.Maximized;
        if (!settings.Maximized)
            settings.Bounds = new Rect(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
        settings.MainRowDefinitions = this.MainGrid.RowDefinitions.Select(r => r.Height.ToString()).ToArray();
        settings.StatusColumnWidths = this.StatusPanel.GetColumnWidths().ToArray();
        settings.LogColumnWidths = this.LogPanel.GetColumnWidths().ToArray();
    }
}