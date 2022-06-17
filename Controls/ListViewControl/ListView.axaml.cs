using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;

namespace rgit.Controls;

public partial class ListView : UserControl, IDisposable
{
    public const int HeadingPaddingX = 4;
    public const int HeadingPaddingY = 8;
    public const int HeadingHeight = RowHeight + HeadingPaddingY;
    public const int LineHeight = 14;
    public const int RowPadding = 4;
    public const int RowHeight = LineHeight + (2 * RowPadding);
    public const int ColumnResizeWidth = 6;

    public SKFont DefaultFont { get; }
    public SKFont DefaultHeadingFont { get; }
    public SKPaint HeadingPaint { get; }
    public SKPaint DefaultFontPaint { get; }
    public SKPaint HeadingSortLinePaint { get; }
    public SKPaint HoveredPaint { get; }
    public SKPaint BackgroundPaint { get; }
    public SKPaint SelectedBackgroundPaint { get; }
    public SKPaint ColumnSeparatorPaint { get; }
    public Cursor ColumnResizingCursor { get; }

    public static readonly DirectProperty<ListView, ListViewModel?> ModelProperty =
        AvaloniaProperty.RegisterDirect<ListView, ListViewModel?>(nameof(Model), o => o.Model, (o, v) => o.Model = v);

    public ListViewRowState[] RowStates { get; private set; } = Array.Empty<ListViewRowState>();
    private int? selectedIndex;

    public ListViewModel? Model
    {
        get => this.model;
        set
        {
            if (this.model?.ListView != null && !ReferenceEquals(this.model?.ListView, this))
                throw new InvalidOperationException($"Can't add {nameof(ListViewModel)} to multiple {nameof(ListView)}.");

            if (this.model != null)
                this.model.ListView = null;

            if (value != null)
                value.ListView = this;
            this.SetAndRaise(ModelProperty, ref this.model, value);
        }
    }

    private ListViewModel? model;

    private Point mousePosition;
    private int? resizingColumn;
    private int? previousSelectionIndex;

    public bool HoverDisabled { get; set; }
    public bool Multiselect { get; set; }

    /// <summary>
    /// Column to sort data by. Value bitwise negative to signify ascending.
    /// </summary>
    private int sortColumn;

    public ListView()
    {
        this.InitializeComponent();

        this.DefaultFont = new(SKFontManager.Default.MatchFamily(FontManager.Current.DefaultFontFamilyName), (float)this.FontSize);
        this.DefaultHeadingFont = new(SKFontManager.Default.MatchFamily(FontManager.Current.DefaultFontFamilyName, SKFontStyle.Bold), (float)this.FontSize);
        this.HeadingPaint = new() { Color = new SKColor(200, 200, 200) };
        this.DefaultFontPaint = new() { Color = new SKColor(200, 200, 200) };
        this.HeadingSortLinePaint = new() { Color = this.HeadingPaint.Color, StrokeWidth = 2.0f };
        this.HoveredPaint = new() { Color = new SKColor(27, 69, 89) };
        this.BackgroundPaint = new() { Color = new SKColor(0, 0, 0) };
        this.SelectedBackgroundPaint = new() { Color = new SKColor(38, 99, 128) };
        this.ColumnSeparatorPaint = new() { Color = new SKColor(27, 46, 48), StrokeWidth = 1.0f };
        this.ColumnResizingCursor = new(StandardCursorType.SizeWestEast);

        this.RenderPanel.OnRender += this.OnRender;
        this.ScrollViewer.ScrollChanged += this.OnScrollChanged;
        this.ScrollViewer.GetObservable(BoundsProperty).Subscribe(c => this.InvalidateModel());
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        this.ScrollViewer = this.FindControl<ScrollViewer>(nameof(this.ScrollViewer));
        this.Grid = this.FindControl<Grid>(nameof(this.Grid));
        this.Canvas = this.FindControl<Canvas>(nameof(this.Canvas));
        this.RenderPanel = this.FindControl<RenderPanel>(nameof(this.RenderPanel));
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        Canvas.SetTop(this.RenderPanel, this.ScrollViewer.Offset.Y);
        this.mousePosition = new Point(this.mousePosition.X + e.OffsetDelta.X, this.mousePosition.Y + e.OffsetDelta.Y);
    }

    protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
    {
        base.OnPropertyChanged(change);

        if (ReferenceEquals(ModelProperty, change.Property))
        {
            this.InvalidateModel();
        }
        else if (ReferenceEquals(BoundsProperty, change.Property))
        {
            this.InvalidateModel();
        }
    }

    internal void InvalidateModel()
    {
        if (this.Model == null)
            return;

        this.Grid.MinWidth = this.Model.Columns.Sum(c => c.Width);
        this.Grid.MinHeight = (this.Model.RowCount * RowHeight) // All rows
            + HeadingHeight // Add for header
            + LineHeight; // Some slop just because

        if (this.RowStates.Length != this.Model.RowCount)
        {
            this.RowStates = new ListViewRowState[this.Model.RowCount];
        }

        this.RenderPanel.Width = Math.Max(this.Grid.MinWidth, this.ScrollViewer.Bounds.Width);
        this.RenderPanel.Height = this.ScrollViewer.Bounds.Height;
    }

    public void Repaint()
    {
        this.RenderPanel.InvalidateVisual();
    }

    private void OnRender(ISkiaDrawingContextImpl context)
    {
        var model = this.model;
        if (model == null)
            return;

        if (model.Updating)
            return;

        lock (model.SynchronizationObject)
        {
            var canvas = context.SkCanvas;
            canvas.Save();

            canvas.Clear(new SKColor(0, 0, 0));

            var scrollOffsetY = (float)this.ScrollViewer.Offset.Y;

            // Draw column separators
            {
                var left = 0.5f; // Start at 0.5 so we are pixel perfect.
                var height = (float)this.RenderPanel.Bounds.Height;
                for (var i = 0; i < model.Columns.Count; i++)
                {
                    left += model.Columns[i].Width;
                    canvas.DrawLine(left, 0, left, height, this.ColumnSeparatorPaint);
                }
            }

            // Draw rows
            {
                var bounds = new SKRect(0, HeadingHeight - scrollOffsetY, (float)this.RenderPanel.Bounds.Width, HeadingHeight + RowHeight);
                var start = Math.Max(0, (int)((scrollOffsetY - HeadingHeight) / RowHeight));
                var end = Math.Min(this.RowStates.Length, (int)(((scrollOffsetY - HeadingHeight + this.RenderPanel.Bounds.Height) / RowHeight) + 1));
                for (var rowIndex = start; rowIndex < end; rowIndex++)
                {
                    bounds.Top = HeadingHeight + (RowHeight * rowIndex) - scrollOffsetY;
                    bounds.Bottom = HeadingHeight + (RowHeight * rowIndex) + RowHeight - scrollOffsetY;
                    Debug.Assert(bounds.Bottom >= 0, $"{nameof(start)} was too low.");
                    Debug.Assert(bounds.Top <= this.RenderPanel.Bounds.Height, $"{nameof(end)} was too high.");

                    canvas.Save();
                    canvas.ClipRect(bounds);

                    model.RenderRow(canvas, bounds, rowIndex, this.RowStates[rowIndex]);

                    canvas.Restore();
                }
            }

            // Draw headings
            {
                Span<SKPoint> sortPoints = stackalloc SKPoint[3];

                var left = 0;
                for (var columnIndex = 0; columnIndex < model.Columns.Count; columnIndex++)
                {
                    canvas.Save();

                    var column = model.Columns[columnIndex];
                    var columnWidth = column.Width;
                    var right = left + columnWidth;

                    var isHovered = this.mousePosition.Y - scrollOffsetY < HeadingHeight && this.mousePosition.X > left && this.mousePosition.X < right;

                    // Draw background always to cover rows
                    canvas.DrawRect(left, 0, right - left, HeadingHeight, isHovered ? this.HoveredPaint : this.BackgroundPaint);

                    if (column.Sortable)
                    {
                        if (this.sortColumn == columnIndex)
                        {
                            var center = (left + right) * 0.5f;
                            sortPoints[0] = new SKPoint(-4, 4);
                            sortPoints[1] = new SKPoint(0, 8);
                            sortPoints[2] = new SKPoint(4, 4);

                            for (var i = 0; i < sortPoints.Length; i++)
                                sortPoints[i] = new SKPoint(sortPoints[i].X + center, sortPoints[i].Y);

                            for (var i = 0; i < sortPoints.Length - 1; i++)
                            {
                                var a = sortPoints[i + 0];
                                var b = sortPoints[i + 1];
                                canvas.DrawLine(a, b, this.HeadingSortLinePaint);
                            }
                        }
                        else if (this.sortColumn == ~columnIndex)
                        {
                            var center = (left + right) * 0.5f;
                            sortPoints[0] = new SKPoint(-4, 8);
                            sortPoints[1] = new SKPoint(0, 4);
                            sortPoints[2] = new SKPoint(4, 8);

                            for (var i = 0; i < sortPoints.Length; i++)
                                sortPoints[i] = new SKPoint(sortPoints[i].X + center, sortPoints[i].Y);

                            for (var i = 0; i < sortPoints.Length - 1; i++)
                            {
                                var a = sortPoints[i + 0];
                                var b = sortPoints[i + 1];
                                canvas.DrawLine(a, b, this.HeadingSortLinePaint);
                            }
                        }
                    }

                    canvas.ClipRect(new SKRect(left + HeadingPaddingX, 0, right - HeadingPaddingX, HeadingHeight));
                    canvas.DrawText(column.Text, left + HeadingPaddingX, HeadingPaddingY + this.DefaultHeadingFont.Size, this.DefaultHeadingFont, this.HeadingPaint);
                    left = right;

                    canvas.Restore();
                }

                // Draw extra heading so that it doesn't look weird on the right when hovered
                canvas.DrawRect(left, 0, (float)this.RenderPanel.Bounds.Width - left, HeadingHeight, this.BackgroundPaint);
            }

            canvas.Restore();
        }
    }

    private void OnPointerLeave(object? sender, PointerEventArgs e)
    {
        this.mousePosition = new Point(-1, -1);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var fixedMousePosition = e.GetPosition(this);
        this.mousePosition = new Point(fixedMousePosition.X, fixedMousePosition.Y + this.ScrollViewer.Offset.Y);

        var model = this.model;
        if (model == null)
            return;

        var cursor = Cursor.Default;
        if (this.resizingColumn == null)
        {
            for (var i = 0; i < this.RowStates.Length; i++)
            {
                this.RowStates[i] &= ~ListViewRowState.Hovered;
            }

            if (fixedMousePosition.Y < HeadingHeight)
            {
                var left = 0;
                for (var i = 0; i < model.Columns.Count; i++)
                {
                    left += model.Columns[i].Width;
                    var distance = Math.Abs(left - (int)this.mousePosition.X);
                    if (distance < ColumnResizeWidth)
                    {
                        cursor = this.ColumnResizingCursor;
                        break;
                    }
                }
            }
            else if (!this.HoverDisabled)
            {
                // Hovering a row
                for (var i = 0; i < this.RowStates.Length; i++)
                {
                    var top = HeadingHeight + (RowHeight * i);
                    var bottom = top + RowHeight;
                    if (this.mousePosition.Y > bottom)
                        continue;

                    this.RowStates[i] |= ListViewRowState.Hovered;
                    break;
                }
            }
        }
        else
        {
            cursor = this.ColumnResizingCursor;
            var left = 0;
            for (var i = 0; i < this.resizingColumn.Value; i++)
                left += model.Columns[i].Width;
            var width = this.mousePosition.X - left;
            width = Math.Max(1, width);
            model.Columns[this.resizingColumn.Value].Width = (int)width;
            this.InvalidateModel();
        }

        if (this.Cursor != cursor)
            this.Cursor = cursor;

        if (this.mousePosition.X > this.Canvas.Bounds.Width)
            this.mousePosition = new Point(-1, -1);

        this.Repaint();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var model = this.model;
        if (model == null)
            return;

        var fixedMousePosition = e.GetPosition(this);
        this.mousePosition = new Point(fixedMousePosition.X, fixedMousePosition.Y + this.ScrollViewer.Offset.Y);
        var currentPoint = e.GetCurrentPoint(this);

        var doubleClicked = e.ClickCount == 2 && currentPoint.Properties.IsLeftButtonPressed;

        if (fixedMousePosition.Y < HeadingHeight)
        {
            var mouseX = (int)this.mousePosition.X;
            var left = 0;
            for (var i = 0; i < model.Columns.Count; i++)
            {
                var right = left + model.Columns[i].Width;

                // Check for resizing
                var distance = Math.Abs(right - mouseX);
                if (distance < ColumnResizeWidth)
                {
                    if (doubleClicked)
                        throw new NotImplementedException();

                    this.resizingColumn = i;
                    break;
                }

                // Check for sort order
                if (model.Columns[i].Sortable)
                {
                    if (mouseX > left && mouseX < right)
                    {
                        if (this.sortColumn == i || this.sortColumn == ~i)
                        {
                            this.sortColumn = ~this.sortColumn;
                        }
                        else
                        {
                            this.sortColumn = i;
                        }

                        var sortDirection = this.sortColumn >= 0 ? ListViewSortDirection.Ascending : ListViewSortDirection.Descending;
                        model.Columns[i].InvokeSort(sortDirection);
                        break;
                    }
                }

                left = right;
            }
        }
        else if (currentPoint.Properties.IsLeftButtonPressed || currentPoint.Properties.IsRightButtonPressed)
        {
            var rightClicked = currentPoint.Properties.IsRightButtonPressed;

            var canSelect = true;
            if (!this.Multiselect)
            {
                canSelect = !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            }

            if (canSelect)
            {
                // Clicking a row
                for (var rowIndex = 0; rowIndex < this.RowStates.Length; rowIndex++)
                {
                    var bottom = HeadingHeight + RowHeight + (RowHeight * rowIndex);
                    if (this.mousePosition.Y > bottom)
                        continue;

                    if (rightClicked)
                    {
                        if (this.RowStates.Count(i => i.HasFlag(ListViewRowState.Selected)) <= 1)
                        {
                            for (var i = 0; i < this.RowStates.Length; i++)
                                this.RowStates[i] &= ~ListViewRowState.Selected;

                            this.selectedIndex = rowIndex;
                            this.RowStates[rowIndex] |= ListViewRowState.Selected;
                            this.previousSelectionIndex = rowIndex;
                        }

                        for (var i = 0; i < this.RowStates.Length; i++)
                            this.RowStates[i] &= ~ListViewRowState.Hovered;

                        model.InvokeOnContextMenu(e);
                    }
                    else if (doubleClicked)
                    {
                        for (var i = 0; i < this.RowStates.Length; i++)
                            this.RowStates[i] &= ~ListViewRowState.Selected;
                        this.RowStates[rowIndex] |= ListViewRowState.Selected;
                        this.selectedIndex = rowIndex;
                        this.previousSelectionIndex = rowIndex;
                        model.InvokeOnDoubleClicked();
                    }
                    else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        if (!rightClicked && this.RowStates[rowIndex].HasFlag(ListViewRowState.Selected))
                            this.RowStates[rowIndex] &= ~ListViewRowState.Selected;
                        else
                            this.RowStates[rowIndex] |= ListViewRowState.Selected;

                        if (this.RowStates.Count(i => i.HasFlag(ListViewRowState.Selected)) == 1)
                        {
                            this.selectedIndex = rowIndex;
                        }
                        else
                        {
                            this.selectedIndex = null;
                        }

                        this.previousSelectionIndex = rowIndex;
                    }
                    else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        if (this.previousSelectionIndex == null)
                        {
                            this.RowStates[rowIndex] |= ListViewRowState.Selected;
                            this.previousSelectionIndex = rowIndex;
                        }
                        else
                        {
                            for (var i = 0; i < this.RowStates.Length; i++)
                                this.RowStates[i] &= ~ListViewRowState.Selected;
                            var start = Math.Min(this.previousSelectionIndex.Value, rowIndex);
                            var end = Math.Max(this.previousSelectionIndex.Value, rowIndex);

                            for (var i = start; i <= end; i++)
                                this.RowStates[i] |= ListViewRowState.Selected;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < this.RowStates.Length; i++)
                            this.RowStates[i] &= ~ListViewRowState.Selected;

                        this.RowStates[rowIndex] |= ListViewRowState.Selected;
                        this.selectedIndex = rowIndex;
                        this.previousSelectionIndex = rowIndex;
                    }

                    model.InvokeOnSelectionChanged();
                    break;
                }
            }
        }

        this.Repaint();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        this.resizingColumn = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.A:
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                {
                    for (var i = 0; i < this.RowStates.Length; i++)
                        this.RowStates[i] |= ListViewRowState.Selected;
                }
                this.model?.InvokeOnSelectionChanged();
                e.Handled = true;
                break;
            }
        }

        if (!e.Handled)
        {
            this.model?.InvokeOnKeyDown(e);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        this.DefaultFont.Dispose();
        this.HeadingPaint.Dispose();
        this.HeadingSortLinePaint.Dispose();
        this.HoveredPaint.Dispose();
        this.BackgroundPaint.Dispose();
        this.SelectedBackgroundPaint.Dispose();
        this.ColumnSeparatorPaint.Dispose();
        this.ColumnResizingCursor.Dispose();
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }
}