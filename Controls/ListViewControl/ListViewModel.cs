using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Input;
using SkiaSharp;

namespace rgit.Controls;

/// <summary>
/// Direction to sort a column.
/// </summary>
public enum ListViewSortDirection
{
    /// <summary>
    /// Items are sorted in ascending order.
    /// </summary>
    Ascending,

    /// <summary>
    /// Items are sorted in ascending order.
    /// </summary>
    Descending,
}

/// <summary>
/// Model used to back <see cref="ListView"/>.
/// </summary>
public abstract class ListViewModel
{
    public const int RowTextPadding = 4;

    public bool Updating { get; private set; }
    public abstract int RowCount { get; }
    public abstract string GetCellValue(int row, int col);
    public List<ListViewColumn> Columns { get; } = new();
    public object SynchronizationObject { get; } = new object();

    public ListView? ListView { get; internal set; }

    public delegate void OnContextMenuDelegate(PointerPressedEventArgs e);

    public event OnContextMenuDelegate? OnContextMenu;

    public delegate void OnSelectionChangedDelegate();
    public delegate void OnDoubleClickedDelegate();

    public event OnSelectionChangedDelegate? OnSelectionChanged;
    public event OnDoubleClickedDelegate? OnDoubleClicked;

    public void InvalidateVisual()
    {
        this.ListView?.InvalidateModel();
        this.ListView?.InvalidateVisual();
    }

    public void BeginUpdate()
    {
        lock (this.SynchronizationObject)
        {
            this.Updating = true;
        }
    }

    public void EndUpdate()
    {
        lock (this.SynchronizationObject)
        {
            this.Updating = false;
        }

        this.InvalidateVisual();
    }

    public virtual void RenderRow(SKCanvas canvas, SKRect bounds, int rowIndex, ListViewRowState state)
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

            canvas.DrawText(text, left + RowTextPadding, bounds.Top + this.ListView.DefaultFont.Size, this.ListView.DefaultFont, this.ListView.DefaultFontPaint);

            canvas.Restore();
            left += column.Width;
        }
    }

    public void InvokeOnContextMenu(PointerPressedEventArgs e) => this.OnContextMenu?.Invoke(e);
    public void InvokeOnSelectionChanged() => this.OnSelectionChanged?.Invoke();
    public void InvokeOnDoubleClicked() => this.OnDoubleClicked?.Invoke();
}

public class ListViewColumn
{
    public string Text { get; set; }
    public int Width { get; set; }
    public bool Sortable { get; set; }

    public delegate void OnSortDelegate(ListViewColumn column, ListViewSortDirection direction);

    public event OnSortDelegate? OnSort;

    public ListViewColumn(string text)
    {
        this.Text = text;
        this.Width = 64;
    }

    public ListViewColumn(string text, int width, bool sortable)
    {
        this.Text = text;
        this.Width = width;
        this.Sortable = sortable;
    }

    internal void InvokeSort(ListViewSortDirection direction) => this.OnSort?.Invoke(this, direction);
}

[Flags]
public enum ListViewRowState
{
    None = 0,
    Selected = 1 << 0,
    Hovered = 1 << 1,
}