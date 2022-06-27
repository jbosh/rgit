using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using LibGit2Sharp;
using SkiaSharp;

namespace rgit;

public enum GitStatusString
{
    Added,
    Deleted,
    Missing,
    Modified,
    ModifiedAndRenamed,
    Renamed,
    Staged,
    Unknown,

    /// <summary>
    /// Special case when using GUI to write to stage.
    /// </summary>
    Staging,

    /// <summary>
    /// Special case where diffing a branch against working tree.
    /// </summary>
    Working,
}

public class GitStatus
{
    public string Path { get; set; }
    public string Extension { get; set; }
    public GitStatusString Status { get; set; }
    public string StatusString => GetStatusString(this.Status);
    public StatusEntry? GitStatusEntry { get; set; }
    public TreeEntryChanges? GitTreeEntryChanges { get; set; }
    public string? BranchShaBefore { get; set; }
    public string? BranchShaAfter { get; set; }
    public Color Color => GetColorFromStatus(this.Status);
    public SKPaint Paint => GetPaintFromStatus(this.Status);
    public override string ToString() => $"{this.Path} ({this.StatusString})";

    public GitStatus(string path, GitStatusString status)
    {
        this.Path = path;
        this.Extension = System.IO.Path.GetExtension(path);
        this.Status = status;
    }

    public GitStatus(string path, GitStatusString status, StatusEntry statusEntry)
        : this(path, status)
    {
        this.GitStatusEntry = statusEntry;
    }

    public GitStatus(string path, GitStatusString status, TreeEntryChanges treeEntryChanges, string? branchShaBefore, string? branchShaAfter)
        : this(path, status)
    {
        this.GitTreeEntryChanges = treeEntryChanges;
        this.BranchShaBefore = branchShaBefore;
        this.BranchShaAfter = branchShaAfter;
    }

    public static GitStatus FromChanges(TreeEntryChanges entry, string? branchShaBefore, string? branchShaAfter)
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

    private static readonly Dictionary<GitStatusString, Color> ColorsByStatus = new()
    {
        { GitStatusString.Added, Color.FromRgb(186, 104, 200) },
        { GitStatusString.Deleted, Color.FromRgb(141, 110, 99) },
        { GitStatusString.Missing, Color.FromRgb(244, 67, 54) },
        { GitStatusString.Modified, Color.FromRgb(100, 181, 246) },
        { GitStatusString.ModifiedAndRenamed, Color.FromRgb(142, 36, 170) },
        { GitStatusString.Renamed, Color.FromRgb(142, 36, 170) },
        { GitStatusString.Staged, Color.FromRgb(165, 214, 167) },
        { GitStatusString.Unknown, Color.FromRgb(224, 224, 224) },
    };

    private static readonly Dictionary<GitStatusString, SKPaint> PaintsByStatus = ColorsByStatus.ToDictionary(p => p.Key, p => new SKPaint { Color = new SKColor(p.Value.R, p.Value.G, p.Value.B, p.Value.A) });

    public static Color GetColorFromStatus(GitStatusString status) => ColorsByStatus[status];
    public static SKPaint GetPaintFromStatus(GitStatusString status) => PaintsByStatus[status];

    public static string GetStatusString(GitStatusString status)
    {
        switch (status)
        {
            case GitStatusString.Added:
            case GitStatusString.Deleted:
            case GitStatusString.Missing:
            case GitStatusString.Modified:
            case GitStatusString.Renamed:
            case GitStatusString.Staged:
            case GitStatusString.Unknown:
            case GitStatusString.Staging:
            case GitStatusString.Working:
                return status.ToString();
            case GitStatusString.ModifiedAndRenamed:
                return "Modified and Renamed";
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }
}