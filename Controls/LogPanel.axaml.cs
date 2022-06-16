using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using LibGit2Sharp;
using SkiaSharp;

namespace rgit.Controls;

public partial class LogPanel : UserControl
{
    public static readonly DirectProperty<LogPanel, Repository?> RepositoryProperty =
        AvaloniaProperty.RegisterDirect<LogPanel, Repository?>(nameof(Repository), o => o.Repository, (o, v) => o.Repository = v);

    public Repository? Repository
    {
        get => this.repository;
        set => this.SetAndRaise(RepositoryProperty, ref this.repository, value);
    }

    private Repository? repository;
    private bool refreshing;
    public string? Branch { get; set; }
    public string? PathSpec { get; set; }

    private LogPanelModel Model { get; } = new();
    private readonly ContextMenuItem[] contextMenuItems;

    public delegate void OnSelectionChangedDelegate(string? sha);

    public event OnSelectionChangedDelegate? OnSelectionChanged;

    public LogPanel()
    {
        this.InitializeComponent();

        this.contextMenuItems = new[]
        {
            new ContextMenuItem
            {
                Text = "Compare with working tree",
                OnClick = (i) => throw new NotImplementedException(),
            },
            new ContextMenuItem
            {
                Text = "Copy SHA",
                OnClick = this.CopyShaToClipboard,
            },
        };

        this.ListView.Model = this.Model;
        this.Model.OnSelectionChanged += this.OnSelectionChangedCallback;
        this.Model.OnContextMenu += this.OnContextMenu;
    }

    private void OnSelectionChangedCallback()
    {
        var selected = default(string);
        for (var i = 0; i < this.ListView.RowStates.Length; i++)
        {
            if (this.ListView.RowStates[i].HasFlag(ListViewRowState.Selected))
            {
                selected = this.Model.Items[i].Sha;
                break;
            }
        }

        this.OnSelectionChanged?.Invoke(selected);
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
            _ = this.Refresh();
        }
    }

    public async Task Refresh()
    {
        if (this.repository == null)
            return;

        if (this.refreshing)
            return;

        this.refreshing = true;

        var selected = default(string);
        for (var i = 0; i < this.ListView.RowStates.Length; i++)
        {
            if (this.ListView.RowStates[i].HasFlag(ListViewRowState.Selected))
            {
                selected = this.Model.Items[i].Sha;
                break;
            }
        }

        var branch = this.Branch == null ? this.repository.Head : this.repository.Branches[this.Branch];
        var newItems = await Task.Run(() => this.GetPreparedRows(branch));

        this.Model.BeginUpdate();
        this.Model.Items = newItems;
        this.Model.EndUpdate();

        if (selected != null)
        {
            var selectedIndex = Array.FindIndex(this.Model.Items, r => r.Sha == selected);
            if (selectedIndex >= 0)
            {
                this.ListView.RowStates[selectedIndex] |= ListViewRowState.Selected;
            }
        }

        this.Model.InvalidateVisual();

        this.refreshing = false;
    }

    private GitLogRow[] GetPreparedRows(Branch branch)
    {
        Debug.Assert(this.repository != null, $"Can't call {nameof(this.GetPreparedRows)} with null repo.");
        var graph = GitGraph.GenerateGraph(this.repository, branch.Tip, this.PathSpec);
        var list = new List<GitLogRow>(graph.Length);
        foreach (var node in graph)
        {
            list.Add(new GitLogRow(node));
        }

        return list.ToArray();
    }

    private void OnContextMenu(PointerPressedEventArgs e)
    {
        var selectedItems = this.GetSelectedItems();
        if (selectedItems.Length != 0)
        {
            var menuItems = new List<MenuItem>();

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
                    anyItemsAdded = true;
#pragma warning disable S2583 // Expression is always false
                    if (separatorAdded)
#pragma warning restore S2583
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

    private async Task CopyShaToClipboard(GitLogRow[] items)
    {
        var data = string.Join('\n', items.Select(i => i.Sha));
        if (Application.Current?.Clipboard != null)
            await Application.Current.Clipboard.SetTextAsync(data);
    }

    private GitLogRow[] GetSelectedItems()
    {
        var result = new List<GitLogRow>();
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

    public class GitLogRow
    {
        public string Message { get; set; }
        public string MessageShort { get; set; }
        public string Author { get; set; }
        public DateTimeOffset AuthorDate { get; set; }
        public string Sha { get; set; }
        public bool Hidden { get; set; }

        public GitGraph.Node Node { get; set; }

        public GitLogRow(GitGraph.Node node)
        {
            this.Node = node;
            this.Sha = node.Sha;
            this.Message = node.Message;
            this.MessageShort = node.MessageShort;
            this.Author = node.Author.Name;
            this.AuthorDate = node.Author.When;
        }
    }

    private struct ContextMenuItem
    {
        /// <summary>
        /// Gets the text to display.
        /// </summary>
        public string Text { get; init; }

        public delegate Task OnClickDelegate(GitLogRow[] items);

        public OnClickDelegate OnClick { get; init; }
    }

    public class LogPanelModel : ListViewModel
    {
        private const int BranchWidth = 20;
        private const float BranchCircleRadius = 4f;

        public override int RowCount => this.Items.Length;
        public GitLogRow[] Items { get; set; } = Array.Empty<GitLogRow>();

        private static readonly SKColor[] GraphColors =
        {
            new(255, 255, 255),
            new(244, 67, 54),
            new(76, 175, 80),
            new(33, 148, 243),
            new(255, 87, 34),
            new(205, 220, 57),
            new(255, 235, 59),
        };

        private static readonly SKPaint[] GraphPaints = GraphColors.Select(c => new SKPaint { Color = c }).ToArray();
        private static readonly SKPaint[] GraphStrokes = GraphColors.Select(c => new SKPaint { Color = c, IsStroke = true }).ToArray();
        private static SKPaint GetGraphPaint(int index) => GraphPaints[index % GraphPaints.Length];
        private static SKPaint GetStrokePaint(int index) => GraphStrokes[index % GraphPaints.Length];

        public LogPanelModel()
        {
            this.Columns.Add(new ListViewColumn("Tree") { Width = 128, Sortable = false });
            this.Columns.Add(new ListViewColumn("Message") { Width = 256, Sortable = false });
            this.Columns.Add(new ListViewColumn("Author") { Width = 64, Sortable = false });
            this.Columns.Add(new ListViewColumn("Date") { Width = 64, Sortable = false });
        }

        public override string GetCellValue(int row, int col)
        {
            var item = this.Items[row];
            return col switch
            {
                0 => string.Empty,
                1 => item.MessageShort,
                2 => item.Author,
                3 => item.AuthorDate.ToLocalTime().DateTime.ToString(CultureInfo.CurrentCulture),
                _ => throw new ArgumentOutOfRangeException(nameof(col)),
            };
        }

        public override void RenderRow(SKCanvas canvas, SKRect bounds, int rowIndex, ListViewRowState state)
        {
            var node = this.Items[rowIndex].Node;

            Debug.Assert(this.ListView != null, $"Cannot render on detached {nameof(ListViewModel)}.");
            if ((state & (ListViewRowState.Selected | ListViewRowState.Hovered)) != 0)
            {
                var paint = state.HasFlag(ListViewRowState.Selected) ? this.ListView.SelectedBackgroundPaint : this.ListView.HoveredPaint;
                canvas.DrawRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height, paint);
            }

            var left = 0.0f;
            for (var col = 0; col < this.Columns.Count; col++)
            {
                var column = this.Columns[col];
                var text = this.GetCellValue(rowIndex, col);
                var right = left + column.Width;
                canvas.Save();
                canvas.ClipRect(new SKRect(left, bounds.Top, right - RowTextPadding, bounds.Bottom));

                if (col == 0)
                {
                    var x = left + (node.Column * BranchWidth) + (BranchWidth / 2.0f);
                    var midHeight = bounds.Height / 2.0f;
                    var midY = bounds.Top + midHeight;
                    var top = bounds.Top;
                    var bottom = bounds.Bottom;
                    var branchCurveWidth = MathF.Round(midHeight * 2);

                    // Draw lines to children. Only draw if we have multiple children otherwise the child will draw these curves.
                    if (node.Children.Length >= 2)
                    {
                        foreach (var child in node.Children)
                        {
                            var childStroke = GetStrokePaint(child.Column);
                            var childX = bounds.Left + (child.Column * BranchWidth) + (BranchWidth / 2.0f);

                            var drawLine = false;
                            if (child.Column == node.Column || node.Row + 1 == child.Row)
                            {
                                drawLine = true;
                            }
                            else
                            {
                                if (child.Parent0 != null)
                                {
                                    if (child.Parent0 != node && child.Parent0.Column == child.Column)
                                        drawLine = true;
                                }

                                if (child.Parent1 != null)
                                {
                                    if (child.Parent1 != node && child.Parent1.Column == child.Column)
                                        drawLine = true;
                                }
                            }

                            if (drawLine)
                            {
                                // Taken care of by lines work
                            }
                            else if (child.Column < node.Column)
                            {
                                canvas.DrawLine(x, midY, childX + (branchCurveWidth / 2.0f), midY, childStroke);
                                canvas.DrawArc(SKRect.Create(childX, top - (branchCurveWidth / 2.0f), branchCurveWidth, branchCurveWidth), 90, 90, false, childStroke);
                            }
                            else
                            {
                                canvas.DrawLine(x, midY, childX - (branchCurveWidth / 2.0f), midY, childStroke);
                                canvas.DrawArc(SKRect.Create(childX - branchCurveWidth, top - (branchCurveWidth / 2.0f), branchCurveWidth, branchCurveWidth), 0, 90, false, childStroke);
                            }
                        }
                    }
                    else if (node.Parent0 == null)
                    {
                        // Special case for initial commit with a single child.
                        canvas.DrawLine(x, midY, x, top, GetGraphPaint(node.Column));
                    }

                    // draw lines from parents
                    DrawParent(node.Parent0);
                    DrawParent(node.Parent1);

                    // draw running lines
                    foreach (var lineIndex in node.Lines)
                    {
                        var childX = bounds.Left + (lineIndex * BranchWidth) + (BranchWidth / 2.0f);
                        var paint = GetGraphPaint(lineIndex);
                        canvas.DrawLine(childX, top, childX, bottom, paint);
                    }

                    const float CircleNudge = -1.0f;
                    canvas.DrawCircle(x + CircleNudge, midY, BranchCircleRadius, GetGraphPaint(node.Column));

                    void DrawParent(GitGraph.Node? parent)
                    {
                        if (parent == null)
                            return;

                        var parentX = (bounds.Left + (parent.Column * BranchWidth)) + (BranchWidth / 2.0f);
                        var stroke = GetStrokePaint(parent.Column);

                        // Draw line if same column
                        var drawLine = parent.Column == node.Column;

                        // Draw a line if the parent didn't take care of.
                        if (parent.Children.Length >= 2)
                        {
                            if (node.Row + 1 == parent.Row)
                            {
                                drawLine = true;
                            }
                            else
                            {
                                foreach (var c in parent.Children)
                                {
                                    if (c.Column != parent.Column)
                                        continue;
                                    if (c.Row > node.Row)
                                        drawLine = true;
                                }

                                if (node.Parent0 != null && node.Parent1 != null)
                                    drawLine |= node.Parent0.Column != node.Column && node.Parent1.Column != node.Column;
                            }
                        }

                        if (drawLine)
                        {
                            var paint = GetGraphPaint(node.Column);
                            canvas.DrawLine(x, midY, x, bottom, paint);
                        }
                        else
                        {
                            if (parent.Column < node.Column)
                            {
                                canvas.DrawArc(SKRect.Create(parentX, top + (branchCurveWidth / 2.0f), branchCurveWidth, branchCurveWidth), 180, 90, false, stroke);
                                canvas.DrawLine(parentX + (branchCurveWidth / 2.0f), midY, x, midY, stroke);
                            }
                            else
                            {
                                canvas.DrawArc(SKRect.Create(parentX - branchCurveWidth, top + (branchCurveWidth / 2.0f), branchCurveWidth, branchCurveWidth), 270, 90, false, stroke);
                                canvas.DrawLine(parentX - (branchCurveWidth / 2.0f), midY, x, midY, stroke);
                            }
                        }
                    }
                }
                else
                {
                    canvas.DrawText(text, left + RowTextPadding, bounds.Top + this.ListView.DefaultFont.Size, this.ListView.DefaultFont, this.ListView.DefaultFontPaint);
                }

                canvas.Restore();
                left += column.Width;
            }
        }
    }
}