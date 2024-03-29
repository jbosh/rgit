﻿using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using LibGit2Sharp;

namespace rgit;

public static class Program
{
    private static LaunchCommand launchCommand;
    private static Repository? repository;
    private static CommandLineArgs? commandLineArgs;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DarkMode.EnableConsole();
        }

        if (!CommandLineArgs.TryParse(args, out commandLineArgs))
        {
            return 1;
        }

        launchCommand = commandLineArgs.Command;
        repository = commandLineArgs.Repository;

        Settings.Load();

        try
        {
            if (commandLineArgs.Command == LaunchCommand.Difftool)
            {
                rgit.Views.Difftool.Run(repository, commandLineArgs.Status);
                return 0;
            }
            else
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
                Settings.Save();
            }
        }
        finally
        {
            TemporaryFiles.Cleanup();
        }

        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure(() =>
            {
                var app = new App
                {
                    Repository = repository,
                    LaunchCommand = launchCommand,
                    Args = commandLineArgs!,
                };
                return app;
            })
            .UsePlatformDetect()
            .AfterSetup(CustomWindowingPlatform.Initialize)
            .LogToTrace()
            .UseReactiveUI();
    }
}

public class CustomWindowingPlatform : IWindowingPlatform
{
    private readonly IWindowingPlatform parent;
    private static CustomWindowingPlatform? instance;

    public static void Initialize(AppBuilder appBuilder)
    {
        instance = new CustomWindowingPlatform();
    }

    public CustomWindowingPlatform()
    {
        this.parent = AvaloniaLocator.Current.GetService<IWindowingPlatform>()!;
        AvaloniaLocator.CurrentMutable.Bind<IWindowingPlatform>().ToConstant(this);
    }

    public IWindowImpl CreateWindow()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new DarkMode.DarkModeWindow();
        }
        else
        {
            return this.parent.CreateWindow();
        }
    }

    public IWindowImpl CreateEmbeddableWindow() => this.parent.CreateEmbeddableWindow();
    public ITrayIconImpl? CreateTrayIcon() => this.parent.CreateTrayIcon();
}