using System;
using System.Collections.Generic;
using System.IO;

namespace rgit;

public static class TemporaryFiles
{
    private static readonly List<string> tempPaths = new();

    public static string GetFilePath()
    {
        var path = Path.GetTempFileName();
        var info = new FileInfo(path);
        info.Attributes = FileAttributes.Temporary;
        AddPathToDelete(path);
        return path;
    }

    public static void AddPathToDelete(string path) => tempPaths.Add(path);

    public static void Cleanup()
    {
        foreach (var path in tempPaths)
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