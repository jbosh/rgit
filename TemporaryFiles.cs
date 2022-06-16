using System;
using System.Collections.Generic;
using System.IO;

namespace rgit;

public static class TemporaryFiles
{
    private static readonly List<string> TempPaths = new();

    public static string GetFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        AddPathToDelete(path);
        return path;
    }

    public static void AddPathToDelete(string path) => TempPaths.Add(path);

    public static void Cleanup()
    {
        foreach (var path in TempPaths)
        {
            SafeDelete(path);
        }
    }

    /// <summary>
    /// Try to delete a file and swallow any exceptions on failure.
    /// </summary>
    /// <param name="path">Path to the file to delete.</param>
    public static void SafeDelete(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore any failures.
        }
    }
}