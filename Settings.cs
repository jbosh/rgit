using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace rgit;

public static class Settings
{
    private static class Loader
    {
        public static string RgitDir => ".rgit";
        public static string TempDir => "tmp";
        public static string SettingsFile => "settings.json";
    }

    public class _CommitWindowSettings
    {
        public Rect? Bounds { get; set; }
        public bool Maximized { get; set; }
        public string[]? MainRowDefinitions { get; set; }
        public int[]? StatusColumnWidths { get; set; }
    }

    public class _StatusWindowSettings
    {
        public Rect? Bounds { get; set; }
        public bool Maximized { get; set; }
        public int[]? StatusColumnWidths { get; set; }
    }

    public class _LogWindowSettings
    {
        public Rect? Bounds { get; set; }
        public bool Maximized { get; set; }
        public string[]? MainRowDefinitions { get; set; }
        public int[]? StatusColumnWidths { get; set; }
        public int[]? LogColumnWidths { get; set; }
    }

    public class _DiffSettings
    {
        private static string[] BCompare()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new[] { "bcompare" };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new[] { @"C:\Program Files\Beyond Compare 4\BComp.exe" };

            return new[] { "bcomp" };
        }

        public string[] Command { get; set; } = BCompare().Concat(new[] { "%base", "%mine", "-title1=%bname", "-title2=%yname" }).ToArray();
        public string LeftReadOnly { get; set; } = "-leftreadonly";
        public string RightReadOnly { get; set; } = "-rightreadonly";
        public Dictionary<string, string> Environment { get; set; } = new();
    }

    public static string GetGlobalSettingsDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rgit");
    public static string GetGlobalSettingsPath() => Path.Combine(GetGlobalSettingsDir(), "settings.json");

#pragma warning disable SA1401
    public static _CommitWindowSettings CommitWindow { get; } = new();
    public static _LogWindowSettings LogWindow { get; } = new();
    public static _StatusWindowSettings StatusWindow { get; } = new();
    public static _DiffSettings Diff { get; } = new();
#pragma warning restore

    private static Rect DeserializeRect(JToken json)
    {
        var x = (int)json["x"]!;
        var y = (int)json["y"]!;
        var w = (int)json["w"]!;
        var h = (int)json["h"]!;

        var rect = new Rect(x, y, w, h);
        return rect;
    }

    private static JObject SerializeRect(Rect rect)
    {
        var json = new JObject
        {
            ["x"] = rect.X,
            ["y"] = rect.Y,
            ["w"] = rect.Width,
            ["h"] = rect.Height,
        };
        return json;
    }

    public static void Load()
    {
        LoadGlobalSettings();
    }

    private static void LoadGlobalSettings()
    {
        var settingsPath = GetGlobalSettingsPath();
        if (!File.Exists(settingsPath))
            return;

        var file = File.ReadAllText(settingsPath);
        var json = JObject.Parse(file);

        JToken? value;
        var commit = json["commit"];
        if (commit != null)
        {
            if ((value = commit["bounds"]) != null)
                CommitWindow.Bounds = DeserializeRect(value);
            if ((value = commit["maximized"]) != null)
                CommitWindow.Maximized = (bool)value;
            if ((value = commit["rowDefinitions"]) != null)
                CommitWindow.MainRowDefinitions = ((JArray)value).Select(t => (string)t!).ToArray();
            if ((value = commit["statusColumns"]) != null)
            {
                var array = (JArray)commit["statusColumns"]!;
                CommitWindow.StatusColumnWidths = array.Select(j => (int)j).ToArray();
            }
        }

        var log = json["log"];
        if (log != null)
        {
            if ((value = log["bounds"]) != null)
                LogWindow.Bounds = DeserializeRect(value);
            if ((value = log["maximized"]) != null)
                LogWindow.Maximized = (bool)value;
            if ((value = log["rowDefinitions"]) != null)
                LogWindow.MainRowDefinitions = ((JArray)value).Select(t => (string)t!).ToArray();
            if ((value = log["statusColumns"]) != null)
            {
                var array = (JArray)log["statusColumns"]!;
                LogWindow.StatusColumnWidths = array.Select(j => (int)j).ToArray();
            }

            if ((value = log["logColumns"]) != null)
            {
                var array = (JArray)log["logColumns"]!;
                LogWindow.LogColumnWidths = array.Select(j => (int)j).ToArray();
            }
        }

        var status = json["status"];
        if (status != null)
        {
            if ((value = status["bounds"]) != null)
                StatusWindow.Bounds = DeserializeRect(value);
            if ((value = status["maximized"]) != null)
                StatusWindow.Maximized = (bool)value;
            if ((value = status["statusColumns"]) != null)
            {
                var array = (JArray)status["statusColumns"]!;
                StatusWindow.StatusColumnWidths = array.Select(j => (int)j).ToArray();
            }
        }

        var diff = json["diff"];
        if (diff != null)
        {
            if ((value = diff["env"]) != null)
            {
                Diff.Environment = ((IDictionary<string, JToken>)value).ToDictionary(p => p.Key, p => (string)p.Value!);
            }
        }
    }

    public static void Save()
    {
        SaveGlobalSettings();
    }

    private static void SaveGlobalSettings()
    {
        var settingsPath = GetGlobalSettingsPath();
        var originalFile = string.Empty;
        var json = new JObject();
        if (File.Exists(settingsPath))
        {
            originalFile = File.ReadAllText(settingsPath);
            json = JObject.Parse(originalFile);
        }

        json["commit"] = new JObject();
        if (CommitWindow.Bounds.HasValue)
            json["commit"]!["bounds"] = SerializeRect(CommitWindow.Bounds.Value);
        if (CommitWindow.MainRowDefinitions != null)
            json["commit"]!["rowDefinitions"] = new JArray(CommitWindow.MainRowDefinitions);
        if (CommitWindow.StatusColumnWidths != null)
            json["commit"]!["statusColumns"] = new JArray(CommitWindow.StatusColumnWidths);
        json["commit"]!["maximized"] = CommitWindow.Maximized;

        json["log"] = new JObject();
        if (LogWindow.Bounds.HasValue)
            json["log"]!["bounds"] = SerializeRect(LogWindow.Bounds.Value);
        if (LogWindow.MainRowDefinitions != null)
            json["log"]!["rowDefinitions"] = new JArray(LogWindow.MainRowDefinitions);
        if (LogWindow.StatusColumnWidths != null)
            json["log"]!["statusColumns"] = new JArray(LogWindow.StatusColumnWidths);
        if (LogWindow.LogColumnWidths != null)
            json["log"]!["logColumns"] = new JArray(LogWindow.LogColumnWidths);
        json["log"]!["maximized"] = LogWindow.Maximized;

        json["status"] = new JObject();
        if (StatusWindow.Bounds.HasValue)
            json["status"]!["bounds"] = SerializeRect(StatusWindow.Bounds.Value);
        if (StatusWindow.StatusColumnWidths != null)
            json["status"]!["statusColumns"] = new JArray(StatusWindow.StatusColumnWidths);
        json["status"]!["maximized"] = StatusWindow.Maximized;

        json["diff"] = new JObject();
        json["diff"]!["env"] = JObject.FromObject(Diff.Environment);

        if (!Directory.Exists(GetGlobalSettingsDir()))
            Directory.CreateDirectory(GetGlobalSettingsDir());

        var output = json.ToString(Formatting.Indented);
        if (originalFile == output)
            return;

        File.WriteAllText(settingsPath, output);
    }
}