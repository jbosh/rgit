using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LibGit2Sharp;
using MessageBox.Avalonia.Enums;
using SkiaSharp;

namespace rgit.Controls;

public partial class StatusPanel : UserControl
{
    public static readonly DirectProperty<StatusPanel, Repository?> RepositoryProperty =
        AvaloniaProperty.RegisterDirect<StatusPanel, Repository?>(nameof(Repository), o => o.Repository, (o, v) => o.Repository = v);

    public Repository? Repository
    {
        get => this.repository;
        set
        {
            this.SetAndRaise(RepositoryProperty, ref this.repository, value);
            this.OnCollectionChanged();
        }
    }

    public static readonly DirectProperty<StatusPanel, string[]?> PathsProperty =
        AvaloniaProperty.RegisterDirect<StatusPanel, string[]?>(nameof(Paths), o => o.Paths, (o, v) => o.Paths = v);

    public string[]? Paths
    {
        get => this.paths;
        set
        {
            this.SetAndRaise(PathsProperty, ref this.paths, value);
            this.OnCollectionChanged();
        }
    }

    private readonly ContextMenuItem[] contextMenuItems;
    private readonly IList<GitStatus> stagedItems = new List<GitStatus>();
    private readonly IList<GitStatus> unstagedItems = new List<GitStatus>();
    private readonly IList<GitStatus> unversionedItems = new List<GitStatus>();
    private readonly IList<GitStatus> diffParent0 = new List<GitStatus>();
    private readonly IList<GitStatus> diffParent1 = new List<GitStatus>();

    private Repository? repository;
    private string[]? paths;

    public StatusPanelModel Model { get; } = new();

    private string? gitVersionBefore;
    private string? gitVersionAfter;
    private bool isLogs;

    public bool IsLogs
    {
        get => this.isLogs;
        set
        {
            this.Model.Columns[2].Text = value ? "Action" : "Status";
            this.isLogs = value;
        }
    }

    private int sortColumn;

    private static readonly SKPaint GroupHeadingPaint = new() { Color = new SKColor(128, 222, 234) };

    public StatusPanel()
    {
        this.InitializeComponent();

        this.contextMenuItems = new[]
        {
            new ContextMenuItem
            {
                Text = "Compare revisions",
                OnClick = this.DiffRevisions,
                AllowStagedItems = true,
                AllowUnstagedItems = true,
                AllowVersioned = true,
                AllowLogs = true,
                LogsOnly = true,
                VersionedOnly = true,
                Filter = (_) => this.gitVersionAfter != null,
            },
            new ContextMenuItem
            {
                Text = "Compare with base",
                OnClick = this.DiffWithBase,
                AllowStagedItems = true,
                AllowUnstagedItems = true,
                AllowLogs = true,
                AllowVersioned = true,
                AllowUnversionedItems = true,
                Filter = (_) => this.gitVersionAfter == null,
            },
            new ContextMenuItem
            {
                Text = "Compare with working tree",
                OnClick = this.DiffFilesWithWorkingTree,
                AllowStagedItems = true,
                AllowUnstagedItems = true,
                AllowVersioned = true,
                AllowLogs = true,
                VersionedOnly = true,
            },
            new ContextMenuItem { Text = "-" },
            new ContextMenuItem
            {
                Text = "Stage",
                OnClick = this.StageFiles,
                AllowUnstagedItems = true,
                AllowUnversionedItems = true,
                Filter = items => items.Any(i => !i.IsUnversioned),
            },
            new ContextMenuItem
            {
                Text = "Stage with GUI",
                OnClick = this.StageFilesWithGUI,
                AllowUnstagedItems = true,
            },
            new ContextMenuItem
            {
                Text = "Add",
                OnClick = this.StageFiles,
                AllowUnversionedItems = true,
            },

            new ContextMenuItem { Text = "-" },
            new ContextMenuItem
            {
                Text = "Reset",
                OnClick = this.UnstageFiles,
                AllowStagedItems = true,
            },
            new ContextMenuItem
            {
                Text = "Revert",
                OnClick = this.RevertFiles,
                AllowUnstagedItems = true,
            },
            new ContextMenuItem
            {
                Text = "Delete",
                OnClick = this.DeleteFiles,
                AllowUnversionedItems = true,
            },
        };

        this.ListView.Multiselect = true;
        this.ListView.Model = this.Model;
        this.Model.OnContextMenu += this.OnContextMenu;
        this.Model.OnSelectionChanged += this.OnSelectionChanged;
        this.Model.OnDoubleClicked += this.OnDoubleClicked;
        this.Model.OnKeyDown += this.OnKeyDownEvent;
        for (var i = 0; i < this.Model.Columns.Count; i++)
        {
            var sortColumn = i;
            this.Model.Columns[i].OnSort += (c, o) =>
            {
                this.sortColumn = o == ListViewSortDirection.Ascending ? sortColumn : ~sortColumn;
                this.OnCollectionChanged();
            };
        }
    }

    public void SetColumnWidths(int[] columnWidths)
    {
        if (this.Model.Columns.Count != columnWidths.Length)
        {
            // This is an incorrect setting, ignore it.
            return;
        }

        for (var i = 0; i < columnWidths.Length; i++)
            this.Model.Columns[i].Width = columnWidths[i];

        this.MinWidth = columnWidths.Sum();
    }

    public IReadOnlyCollection<int> GetColumnWidths() => this.Model.Columns.Select(c => c.Width).ToArray();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        this.ListView = this.FindControl<ListView>(nameof(this.ListView));
    }

    protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
    {
        base.OnPropertyChanged(change);
        if (ReferenceEquals(change.Property, RepositoryProperty))
        {
            this.Refresh();
        }
        else if (ReferenceEquals(change.Property, PathsProperty))
        {
            this.Refresh();
        }
    }

    public void SetVersion(string? versionBefore, string? versionAfter)
    {
        if (this.gitVersionBefore == versionBefore && this.gitVersionAfter == versionAfter)
            return;

        this.gitVersionBefore = versionBefore;
        this.gitVersionAfter = versionAfter;
        this.Refresh();
    }

    public void Refresh()
    {
        this.stagedItems.Clear();
        this.unstagedItems.Clear();
        this.unversionedItems.Clear();
        this.diffParent0.Clear();
        this.diffParent1.Clear();

        if (this.repository == null)
            return;

        if (!this.IsLogs)
        {
            var statusOptions = new StatusOptions();
            if (this.paths != null)
                statusOptions.PathSpec = this.paths;
            foreach (var item in this.repository.RetrieveStatus(statusOptions))
            {
                switch (item.State)
                {
                    case FileStatus.DeletedFromIndex:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Deleted));
                        break;
                    case FileStatus.DeletedFromWorkdir:
                        this.unstagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Missing));
                        break;
                    case FileStatus.NewInWorkdir:
                        this.unversionedItems.Add(new GitStatus(item.FilePath, GitStatusString.Unknown));
                        break;
                    case FileStatus.NewInIndex | FileStatus.ModifiedInWorkdir:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Added));
                        this.unstagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Modified));
                        break;
                    case FileStatus.ModifiedInWorkdir:
                        this.unstagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Modified));
                        break;
                    case FileStatus.ModifiedInIndex:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Staged));
                        break;
                    case FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir:
                        this.unstagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Modified));
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Staged));
                        break;
                    case FileStatus.ModifiedInIndex | FileStatus.RenamedInIndex | FileStatus.ModifiedInWorkdir:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Renamed));
                        break;
                    case FileStatus.NewInIndex:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Added));
                        break;
                    case FileStatus.RenamedInIndex:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Renamed, item));
                        break;
                    case FileStatus.RenamedInIndex | FileStatus.ModifiedInWorkdir:
                        this.stagedItems.Add(new GitStatus(item.FilePath, GitStatusString.Renamed, item));
                        this.unstagedItems.Add(new GitStatus(item.FilePath, GitStatusString.ModifiedAndRenamed, item));
                        break;
                    case FileStatus.Ignored:
                        // nothing to do
                        break;
                    default:
                        throw new NotImplementedException($"{item.State} not implemented.");
                }
            }
        }
        else
        {
            if (this.gitVersionBefore == null)
            {
                // Empty
            }
            else if (this.gitVersionAfter == null)
            {
                // We're a log and looking at an individual commit.
                var commit = this.repository.Lookup<Commit>(this.gitVersionBefore);
                var parents = commit.Parents.ToArray();
                switch (parents.Length)
                {
                    case 0:
                    {
                        foreach (var entry in commit.Tree)
                        {
                            this.diffParent0.Add(new GitStatus(entry.Path, GitStatusString.Added));
                        }

                        break;
                    }
                    case 1:
                    {
                        var parent = this.repository.Lookup<Commit>(parents[0].Sha);
                        var tree = this.repository.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                        foreach (var entry in tree)
                        {
                            this.diffParent0.Add(GetStatus(entry, parent.Sha, commit.Sha));
                        }

                        break;
                    }
                    case 2:
                    {
                        var parent = this.repository.Lookup<Commit>(parents[1].Sha);
                        var tree = this.repository.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                        foreach (var entry in tree)
                        {
                            this.diffParent1.Add(GetStatus(entry, parent.Sha, commit.Sha));
                        }

#pragma warning disable S907 // Cannot use goto.
                        goto case 1;
#pragma warning restore S907
                    }
                    default:
                    {
                        throw new NotImplementedException();
                    }
                }
            }
            else
            {
                Tree? beforeTree;
                string? beforeSha;
                if (this.gitVersionBefore == "WORKING")
                {
                    beforeTree = this.repository.Head.Tip?.Tree;
                    beforeSha = "Working Tree";
                }
                else
                {
                    var commit = this.repository.Lookup<Commit>(this.gitVersionBefore);
                    beforeTree = commit?.Tree;
                    beforeSha = commit?.Sha;
                }

                switch (this.gitVersionAfter)
                {
                    case "WORKING":
                    {
                        var tree = this.repository.Diff.Compare<TreeChanges>(beforeTree, DiffTargets.WorkingDirectory);
                        foreach (var entry in tree)
                        {
                            this.diffParent0.Add(GetStatus(entry, beforeSha, "WORKING"));
                        }

                        break;
                    }
                    case "STAGING":
                    {
                        var tree = this.repository.Diff.Compare<TreeChanges>(beforeTree, DiffTargets.Index);
                        foreach (var entry in tree)
                        {
                            this.diffParent0.Add(GetStatus(entry, beforeSha, "STAGE"));
                        }

                        break;
                    }
                    default:
                    {
                        var after = this.repository.Lookup<Commit>(this.gitVersionAfter);
                        var tree = this.repository.Diff.Compare<TreeChanges>(beforeTree, after.Tree);
                        foreach (var entry in tree)
                        {
                            this.diffParent0.Add(GetStatus(entry, beforeSha, after.Sha));
                        }

                        break;
                    }
                }
            }
        }

        this.OnCollectionChanged();

        static GitStatus GetStatus(TreeEntryChanges entry, string? branchShaBefore, string? branchShaAfter)
        {
            var status = entry.Status switch
            {
                ChangeKind.Added => GitStatusString.Added,
                ChangeKind.Deleted => GitStatusString.Deleted,
                ChangeKind.Modified => GitStatusString.Modified,
                ChangeKind.Renamed => GitStatusString.Renamed,
                ChangeKind.TypeChanged => GitStatusString.Modified,
                _ => throw new Exception($"Unknown {nameof(entry.Status)} value {entry.Status}."),
            };
            return new GitStatus(entry.Path, status, entry, branchShaBefore, branchShaAfter);
        }
    }

    private void OnCollectionChanged()
    {
        this.Model.BeginUpdate();
        this.Model.Items.Clear();

        if (this.IsLogs)
        {
            if (this.diffParent1.Count == 0)
            {
                AddItems(null, this.diffParent0);
            }
            else
            {
                AddItems("Diff with parent 1", this.diffParent0);
                AddItems("Diff with parent 2", this.diffParent1);
            }

            AddItems("Staged Items", this.stagedItems);
            AddItems("Unstaged Items", this.unstagedItems);
            AddItems("Unversioned Items", this.unversionedItems);
        }
        else
        {
            AddItems("Staged Items", this.stagedItems);
            AddItems("Unstaged Items", this.unstagedItems);
            AddItems("Unversioned Items", this.unversionedItems);
        }

        void AddItems(string? heading, IList<GitStatus>? statuses)
        {
            if (statuses == null || statuses.Count == 0)
                return;

            if (heading != null)
            {
                var headingItem = new GitStatusRow(heading);
                this.Model.Items.Add(headingItem);
            }

            var sortedStatuses = new List<GitStatus>(statuses);
            Comparison<GitStatus> comparison = this.sortColumn switch
            {
                0 => (a, b) => string.Compare(a.Path, b.Path, StringComparison.InvariantCulture),
                ~0 => (a, b) => string.Compare(b.Path, a.Path, StringComparison.InvariantCulture),
                1 => (a, b) => string.Compare(a.Extension, b.Extension, StringComparison.InvariantCulture),
                ~1 => (a, b) => string.Compare(b.Extension, a.Extension, StringComparison.InvariantCulture),
                2 => (a, b) => a.Status.CompareTo(b.Status),
                ~2 => (a, b) => b.Status.CompareTo(a.Status),
                _ => throw new NotImplementedException(),
            };

            sortedStatuses.Sort(comparison);

            foreach (var status in sortedStatuses)
            {
                var item = new GitStatusRow(status);
                this.Model.Items.Add(item);
            }
        }

        this.Model.EndUpdate();
    }

    private void OnContextMenu(PointerPressedEventArgs e)
    {
        var selectedItems = this.GetSelectedItems();
        if (selectedItems.Length != 0)
        {
            var menuItems = new List<MenuItem>();
            var status = selectedItems[0].Status;
            var allTheSameStatus = true;
            var isStaged = false;
            var isUnstaged = false;
            var isUnversioned = false;
            for (var i = 0; i < selectedItems.Length; ++i)
            {
                var item = selectedItems[i];
                isStaged |= item.IsStaged;
                isUnstaged |= item.IsUnstaged;
                isUnversioned |= item.IsUnversioned;
                allTheSameStatus &= item.Status == status;
            }

            var anyItemsAdded = false;
            var separatorAdded = false;
            foreach (var menuItem in this.contextMenuItems)
            {
                // separator
                if (menuItem.Text == "-")
                {
                    if (anyItemsAdded)
                        separatorAdded = true;
                }
                else
                {
                    var add = true;
                    if (!menuItem.AllowStagedItems && isStaged)
                    {
                        add = false;
                    }
                    else if (!menuItem.AllowUnstagedItems && isUnstaged)
                    {
                        add = false;
                    }
                    else if (!menuItem.AllowUnversionedItems && isUnversioned)
                    {
                        add = false;
                    }
                    else if (menuItem.Filter != null && !menuItem.Filter(selectedItems))
                    {
                        add = false;
                    }
                    else
                    {
                        if (this.gitVersionBefore != null || this.gitVersionAfter != null)
                        {
                            if (!menuItem.AllowVersioned)
                                add = false;
                        }
                        else if (menuItem.VersionedOnly)
                        {
                            add = false;
                        }

                        if (this.IsLogs)
                        {
                            if (!menuItem.AllowLogs && !menuItem.LogsOnly)
                                add = false;
                        }
                        else if (menuItem.LogsOnly)
                        {
                            add = false;
                        }
                    }

                    if (add)
                    {
                        anyItemsAdded = true;
                        if (separatorAdded)
                        {
                            separatorAdded = false;
                            menuItems.Add(new MenuItem { Header = "-" });
                        }

                        var newMenuItem = new MenuItem
                        {
                            Header = menuItem.Text,
                        };
                        newMenuItem.PointerPressed += (_, _) => menuItem.OnClick(selectedItems);
                        menuItems.Add(newMenuItem);
                    }
                }
            }

            if (menuItems.Count != 0)
            {
                var mousePosition = e.GetPosition(this);
                var contextMenu = new ContextMenu
                {
                    Items = menuItems,
                    PlacementAnchor = PopupAnchor.TopLeft,
                    PlacementRect = new Rect(mousePosition, new Size(1, 1)),
                };
                this.ListView.HoverDisabled = true;
                contextMenu.Open(this);
                contextMenu.MenuClosed += (o, e) => this.ListView.HoverDisabled = false;
            }
        }
    }

    private void OnSelectionChanged()
    {
        for (var i = 0; i < this.Model.Items.Count; i++)
        {
            var item = this.Model.Items[i];
            if (item.IsHeading)
            {
                var state = this.ListView.RowStates[i];
                if (state.HasFlag(ListViewRowState.Selected))
                {
                    this.ListView.RowStates[i] &= ~ListViewRowState.Selected;
                    for (i++; i < this.Model.Items.Count; i++)
                    {
                        if (this.Model.Items[i].IsHeading)
                        {
                            i--;
                            break;
                        }

                        this.ListView.RowStates[i] |= ListViewRowState.Selected;
                    }
                }
            }
        }

        this.InvalidateVisual();
    }

    private void OnDoubleClicked()
    {
        var selectedItems = this.GetSelectedItems();
        if (selectedItems.Length != 1)
            return;
        if (selectedItems[0].IsHeading)
            return;

        _ = this.DiffWithBase(selectedItems);
    }

    private void OnKeyDownEvent(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.D:
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                {
                    _ = this.DiffWithBase();
                    e.Handled = true;
                }

                break;
            }
        }
    }

    private GitStatusRow[] GetSelectedItems()
    {
        var result = new List<GitStatusRow>();
        for (var i = 0; i < this.ListView.RowStates.Length; i++)
        {
            var state = this.ListView.RowStates[i];
            if (state.HasFlag(ListViewRowState.Selected))
            {
                var item = this.Model.Items[i];
                result.Add(item);
            }
        }

        return result.ToArray();
    }

    private async Task DiffRevisions(params GitStatusRow[] files)
    {
        if (files.Length == 0)
            files = this.GetSelectedItems();

        if (this.repository != null)
        {
            if (this.gitVersionAfter == "WORKING")
            {
                await this.repository.DiffFiles(files.Select(f => new GitStatus(f.Status!.Path, GitStatusString.Working)), this.gitVersionBefore);
            }
            else
            {
                await this.repository.DiffFiles(files.Select(r => r.Status!), this.gitVersionAfter);
            }

            this.Refresh();
        }
    }

    private async Task DiffWithBase(params GitStatusRow[] files)
    {
        if (files.Length == 0)
            files = this.GetSelectedItems();

        if (this.repository != null)
        {
            if (this.IsLogs)
            {
                if (this.gitVersionAfter == "WORKING")
                {
                    await this.repository.DiffFiles(files.Select(f => new GitStatus(f.Status!.Path, GitStatusString.Working)), this.gitVersionBefore);
                }
                else
                {
                    await this.repository.DiffFiles(files.Select(r => r.Status!), this.gitVersionBefore);
                }
            }
            else
            {
                // Not logs, compare working tree with HEAD.
                await this.repository.DiffFiles(files.Select(r => r.Status!));
            }

            this.Refresh();
        }
    }

    private async Task DiffFilesWithWorkingTree(params GitStatusRow[] files)
    {
        if (files.Length == 0)
            files = this.GetSelectedItems();

        if (this.repository != null)
        {
            await this.repository.DiffFiles(files.Select(f => new GitStatus(f.Status!.Path, GitStatusString.Working)), this.gitVersionBefore);
            this.Refresh();
        }
    }

    private async Task StageFiles(params GitStatusRow[] files)
    {
        if (this.repository != null)
        {
            await this.repository.StageFiles(files.Select(r => r.Status!));
            this.Refresh();
        }

        this.Refresh();
    }

    private async Task StageFilesWithGUI(params GitStatusRow[] files)
    {
        if (this.repository != null)
        {
            await this.repository.DiffFiles(files.Select(f => new GitStatus(f.Status!.Path, GitStatusString.Staging)));
            this.Refresh();
        }
    }

    private async Task UnstageFiles(params GitStatusRow[] files)
    {
        if (this.repository != null)
        {
            await this.repository.UnstageFiles(files.Select(f => f.Status!));
            this.Refresh();
        }
    }

    private async Task RevertFiles(params GitStatusRow[] files)
    {
        var msg = files.Length == 1
            ? $"Are you sure you want to revert {files[0].Status!.Path}?"
            : $"Are you sure you want to revert {files.Length} files?";
        var messageBox = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("Revert Files", msg, ButtonEnum.YesNo, Icon.Warning, WindowStartupLocation.CenterOwner);
        var result = await messageBox.Show();
        switch (result)
        {
            case ButtonResult.Yes:
            {
                if (this.repository != null)
                {
                    await this.repository.RevertFiles(files.Select(r => r.Status!));
                }

                break;
            }
            default:
            {
                // Do nothing.
                break;
            }
        }

        this.Refresh();
    }

    private async Task DeleteFiles(params GitStatusRow[] files)
    {
        var msg = files.Length == 1
            ? $"Are you sure you want to permanently delete {files[0].Status!.Path}?"
            : $"Are you sure you want to permanently delete {files.Length} files?";
        var messageBox = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("Delete Files", msg, ButtonEnum.YesNo, Icon.Warning, WindowStartupLocation.CenterOwner);
        var result = await messageBox.Show();

        switch (result)
        {
            case ButtonResult.Yes:
            {
                foreach (var file in files)
                {
                    try
                    {
                        if (this.repository != null)
                        {
                            var fullPath = Path.Combine(this.repository.WorkingDirectory(), file.Status!.Path);
                            File.Delete(fullPath);
                        }
                    }
                    catch
                    {
                        // Should probably show a message box.
                    }
                }

                break;
            }
            default:
            {
                // Do nothing.
                break;
            }
        }

        this.Refresh();
    }

    public class GitStatusRow
    {
        public string Text { get; set; }
        public GitStatus? Status { get; set; }
        public bool IsHeading => this.Status == null;
        public bool Selected { get; set; }
        public bool Hovered { get; set; }

        public bool IsStaged => this.Status?.Status switch
        {
            GitStatusString.Added => true,
            GitStatusString.Renamed => true,
            GitStatusString.Staged => true,
            GitStatusString.Deleted => true,
            _ => false,
        };

        public bool IsUnstaged => this.Status?.Status switch
        {
            GitStatusString.Unknown => false,
            _ => !this.IsStaged,
        };

        public bool IsUnversioned => this.Status?.Status switch
        {
            GitStatusString.Unknown => true,
            _ => false,
        };

        public GitStatusRow(string text)
        {
            this.Text = text;
        }

        public GitStatusRow(GitStatus status)
        {
            this.Text = status.Path;
            this.Status = status;
        }
    }

    [DebuggerDisplay("{Text}")]
    private struct ContextMenuItem
    {
        /// <summary>
        /// Gets optional filter to iterate over items. Returns true if the item should display.
        /// </summary>
        public Func<GitStatusRow[], bool>? Filter { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed when staged items are selected.
        /// </summary>
        public bool AllowStagedItems { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed when unstaged items are selected.
        /// </summary>
        public bool AllowUnstagedItems { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed when unversioned items are selected.
        /// </summary>
        public bool AllowUnversionedItems { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed when versioned items are selected.
        /// </summary>
        public bool AllowVersioned { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed only when using versioned files.
        /// </summary>
        public bool VersionedOnly { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is allowed during logs.
        /// </summary>
        public bool AllowLogs { get; init; }

        /// <summary>
        /// Gets a value indicating whether this menu is only allowed during logs.
        /// </summary>
        public bool LogsOnly { get; init; }

        /// <summary>
        /// Gets the text to display.
        /// </summary>
        public string Text { get; init; }

        public delegate Task OnClickDelegate(GitStatusRow[] items);

        public OnClickDelegate OnClick { get; init; }
    }

    public class StatusPanelModel : ListViewModel
    {
        private const int GroupHeadingPadding = 12;

        public override int RowCount => this.Items.Count;
        public List<GitStatusRow> Items { get; } = new();

        public StatusPanelModel()
        {
            this.Columns.Add(new ListViewColumn("Path") { Width = 256, Sortable = true });
            this.Columns.Add(new ListViewColumn("Extension") { Width = 72, Sortable = true });
            this.Columns.Add(new ListViewColumn("Status") { Width = 64, Sortable = true });
        }

        public override string GetCellValue(int row, int col)
        {
            var item = this.Items[row];
            if (item.Status == null)
                return col == 0 ? item.Text : string.Empty;

            return col switch
            {
                0 => item.Status.Path,
                1 => item.Status.Extension,
                2 => item.Status.StatusString,
                _ => throw new ArgumentOutOfRangeException(nameof(col))
            };
        }

        public override void RenderRow(SKCanvas canvas, SKRect bounds, int rowIndex, ListViewRowState state)
        {
            var item = this.Items[rowIndex];
            if (item.IsHeading)
            {
                Debug.Assert(this.ListView != null, $"Cannot render on detached {nameof(ListViewModel)}.");
                if ((state & (ListViewRowState.Selected | ListViewRowState.Hovered)) != 0)
                {
                    var paint = state.HasFlag(ListViewRowState.Selected) ? this.ListView.SelectedBackgroundPaint : this.ListView.HoveredPaint;
                    canvas.DrawRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height, paint);
                }

                var left = 0.0f;
                var text = item.Text;

                canvas.DrawText(text, left + GroupHeadingPadding, bounds.Top + this.ListView.DefaultFont.Size, this.ListView.DefaultFont, GroupHeadingPaint);
                var formattedText = new FormattedText(text, Typeface.Default, this.ListView.DefaultFont.Size, TextAlignment.Left, TextWrapping.NoWrap, Size.Empty);

                var headingLineLeft = (float)formattedText.Bounds.Right + GroupHeadingPadding + 8;
                const float NudgeCenter = 1.5f;
                var headingLineY = bounds.Top + (ListView.LineHeight / 2.0f) + NudgeCenter;
                canvas.DrawLine(headingLineLeft, headingLineY, (float)this.ListView.Bounds.Right - GroupHeadingPadding, headingLineY, GroupHeadingPaint);
            }
            else
            {
                Debug.Assert(this.ListView != null, $"Cannot render on detached {nameof(ListViewModel)}.");
                if ((state & (ListViewRowState.Selected | ListViewRowState.Hovered)) != 0)
                {
                    var paint = state.HasFlag(ListViewRowState.Selected) ? this.ListView.SelectedBackgroundPaint : this.ListView.HoveredPaint;
                    canvas.DrawRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height, paint);
                }

                var left = 0.0f;
                for (var i = 0; i < this.Columns.Count; i++)
                {
                    var column = this.Columns[i];
                    var text = this.GetCellValue(rowIndex, i);
                    var right = left + column.Width;
                    canvas.Save();
                    canvas.ClipRect(new SKRect(left, bounds.Top, left + right, bounds.Bottom));

                    canvas.DrawText(text, left + RowTextPadding, bounds.Top + this.ListView.DefaultFont.Size, this.ListView.DefaultFont, item.Status?.Paint ?? this.ListView.DefaultFontPaint);

                    canvas.Restore();
                    left += column.Width;
                }
            }
        }
    }
}