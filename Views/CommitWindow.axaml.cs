using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessageBox.Avalonia.Enums;
using rgit.ViewModels;

namespace rgit.Views;

public partial class CommitWindow : Window
{
    private GitViewModel? model;
    private bool committing;

    public CommitWindow()
    {
        this.InitializeComponent();
        this.CommitMessageBox.GetObservable(TextBox.TextProperty).Subscribe(this.OnCommitMessageChanged);

        var settings = Settings.CommitWindow;
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

        if (settings.Maximized)
        {
            this.WindowState = WindowState.Maximized;
        }

        if (settings.LastCommitMessage != null)
        {
            this.CommitMessageBox.Text = settings.LastCommitMessage;
            this.OnCommitMessageChanged(this.CommitMessageBox.Text);
        }

        this.BranchText = this.FindControl<TextBlock>(nameof(this.BranchText));
        this.BranchText.Text = $"Branch: {this.model?.Repository.CurrentBranch()}";
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        this.model = this.DataContext as GitViewModel;
        this.Title = this.model?.Repository.WorkingDirectory() ?? this.Title;
        this.Refresh();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Refresh()
    {
        this.BranchText.Text = $"Branch: {this.model?.Repository.CurrentBranch()}";
        _ = this.StatusPanel.Refresh();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5
            || (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            || (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            this.Refresh();
            e.Handled = true;
        }

        if (e.Key == Key.Escape)
            this.Close();
    }

    private void OnAmendCommitChecked(object? sender, RoutedEventArgs e)
    {
        if (this.model == null)
            return;

        var repo = this.model.Repository;
        var isChecked = this.AmendCommitCheckbox.IsChecked ?? false;
        var firstCommit = repo.Head.Commits.FirstOrDefault();
        var log = firstCommit?.Message;
        if (log != null)
        {
            var text = this.CommitMessageBox.Text;
            if (isChecked)
            {
                if (string.IsNullOrWhiteSpace(text))
                    this.CommitMessageBox.Text = log;
            }
            else
            {
                if (text == log)
                    this.CommitMessageBox.Text = string.Empty;
            }
        }
    }

    private void OnCommitMessageChanged(string text)
    {
        this.CommitButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
    }

    private async void Commit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (this.model == null || this.committing)
            return;

        this.committing = true;
        var repo = this.model.Repository;
        var message = this.CommitMessageBox.Text;
        var amendCommit = this.AmendCommitCheckbox.IsChecked ?? false;
        var error = await repo.Commit(message, amendCommit);
        this.committing = false;

        if (error != null)
        {
            var lines = error.Trim().Split(new[] { '\n', '\r' });
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Length == 0)
                {
                    error = string.Join('\n', lines.Skip(i + 1));
                    break;
                }
            }

            var messageBox = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("Revert Files", error, ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner);
            await messageBox.Show();
            return;
        }

        this.CommitMessageBox.Text = string.Empty;
        this.Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var settings = Settings.CommitWindow;
        settings.Maximized = this.WindowState == WindowState.Maximized;
        if (!settings.Maximized)
            settings.Bounds = new Rect(this.Position.X, this.Position.Y, this.Bounds.Width, this.Bounds.Height);
        settings.MainRowDefinitions = this.MainGrid.RowDefinitions.Select(r => r.Height.ToString()).ToArray();
        settings.StatusColumnWidths = this.StatusPanel.GetColumnWidths().ToArray();
        settings.LastCommitMessage = this.CommitMessageBox.Text;
    }
}