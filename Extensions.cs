using System;
using Avalonia;
using Avalonia.Media;

namespace rgit;

public static class Extensions
{
    public static void DrawTextWithClip(this DrawingContext context, IBrush foreground, Point origin, Size clipSize, FormattedText text)
    {
        using (context.PushClip(new Rect(origin, clipSize)))
            context.DrawText(foreground, origin, text);
    }

    public static double Distance(this Point a, Point b)
    {
        var x = a.X - b.X;
        var y = a.Y - b.Y;
        return Math.Sqrt(x * x + y * y);
    }

    public static string WorkingDirectory(this LibGit2Sharp.Repository repo) => repo.Info.WorkingDirectory;
}