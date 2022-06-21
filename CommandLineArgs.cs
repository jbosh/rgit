using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace rgit;

public enum LaunchCommand
{
    Commit,
    Difftool,
    Log,
    Status,
}

public class CommandLineArgs
{
    public LaunchCommand Command { get; }
    public Repository Repository { get; }
    public LogArgs? Log { get; }
    public StatusArgs? Status { get; }

    public class LogArgs
    {
        public string? Branch { get; set; }
        public string? Path { get; set; }
    }

    public class StatusArgs
    {
        public string? Path { get; set; }
        public string? BeforeVersion { get; set; }
        public string? AfterVersion { get; set; }

        public static readonly Regex MultipleCommitRegex = new Regex(@"(?<before>[a-fA-F0-9]+)\.\.(?<after>[a-fA-F0-9]+)");
    }

    private CommandLineArgs(LaunchCommand command, Repository repository)
    {
        this.Command = command;
        this.Repository = repository;
    }

    private CommandLineArgs(LaunchCommand command, Repository repository, LogArgs args)
        : this(command, repository)
    {
        this.Log = args;
    }

    private CommandLineArgs(LaunchCommand command, Repository repository, StatusArgs args)
        : this(command, repository)
    {
        this.Status = args;
    }

    public static void PrintUsage(System.IO.TextWriter stream, LaunchCommand? command)
    {
        if (command.HasValue)
        {
            switch (command.Value)
            {
                case LaunchCommand.Commit:
                {
                    stream.WriteLine($"rgit commit");
                    stream.WriteLine();
                    stream.WriteLine("DESCRIPTION:");
                    stream.WriteLine("Displays commit dialog.");
                    break;
                }
                case LaunchCommand.Difftool:
                {
                    stream.WriteLine($"rgit difftool");
                    stream.WriteLine();
                    stream.WriteLine("DESCRIPTION:");
                    stream.WriteLine("Similar to git difftool except it launches them all simultaneously.");
                    break;
                }
                case LaunchCommand.Log:
                {
                    stream.WriteLine($"rgit log [<branch>] [[--] [<path>]]");
                    stream.WriteLine();
                    stream.WriteLine("DESCRIPTION:");
                    stream.WriteLine("Shows the commit logs.");
                    stream.WriteLine();
                    stream.WriteLine("'path' is the root git directory. If this is not specified, the current working directory will be used.");
                    break;
                }
                case LaunchCommand.Status:
                {
                    stream.WriteLine($"rgit status [<path>]");
                    stream.WriteLine();
                    stream.WriteLine("DESCRIPTION:");
                    stream.WriteLine("Displays working changes compared to HEAD.");
                    stream.WriteLine();
                    stream.WriteLine("'path' is the root git directory. If this is not specified, the current working directory will be used.");
                    break;
                }
                default:
                {
                    throw new NotImplementedException();
                }
            }
        }
        else
        {
            stream.WriteLine("rgit COMMAND [...]");
            stream.WriteLine("COMMAND:");
            stream.WriteLine("  commit   launches a commit window");
            stream.WriteLine("  difftool similar to git difftool but launches all simultaneously");
            stream.WriteLine("  log      launches a log window");
            stream.WriteLine("  status   launches a status window");
        }
    }

    public static bool TryParse(string[] arguments, [NotNullWhen(true)] out CommandLineArgs? value)
    {
        var args = arguments.AsSpan();
        if (args.Length == 0)
        {
            PrintUsage(Console.Error, null);
            value = null;
            return false;
        }

        if (!Enum.TryParse<LaunchCommand>(args[0], true, out var command))
        {
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintUsage(Console.Error, null);
            value = null;
            return false;
        }

        args = args.Slice(1);

        var repo = default(Repository);
        var currentDirectory = Environment.CurrentDirectory;
        var directorySpec = default(string);
        while (currentDirectory != null && repo == null)
        {
            try
            {
                repo = new Repository(currentDirectory);
            }
            catch (RepositoryNotFoundException)
            {
                currentDirectory = Path.GetDirectoryName(currentDirectory);
                if (currentDirectory != null)
                {
                    directorySpec = Path.GetRelativePath(currentDirectory, Environment.CurrentDirectory);
                }
            }
        }

        if (repo == null)
        {
            Console.Error.WriteLine($"Couldn't find a valid git repo from {Environment.CurrentDirectory}.");
            value = null;
            return false;
        }

        switch (command)
        {
            case LaunchCommand.Commit:
            {
                // No arguments
                value = new CommandLineArgs(command, repo);

                break;
            }
            case LaunchCommand.Difftool:
            case LaunchCommand.Status:
            {
                // Difftool and Status have identical arguments.
                var statusArgs = new StatusArgs();

                if (directorySpec != null)
                    statusArgs.Path = directorySpec;

                switch (args.Length)
                {
                    case 0:
                    {
                        break;
                    }
                    case 1:
                    {
                        var pathOrCommitString = args[0];
                        args = args.Slice(1);
                        if (!string.IsNullOrWhiteSpace(pathOrCommitString))
                        {
                            if (StatusArgs.MultipleCommitRegex.IsMatch(pathOrCommitString))
                            {
                                var match = StatusArgs.MultipleCommitRegex.Match(pathOrCommitString);
                                var commitBefore = repo.Lookup<Commit>(match.Groups["before"].Value);
                                var commitAfter = repo.Lookup<Commit>(match.Groups["after"].Value);
                                if (commitBefore != null && commitAfter != null)
                                {
                                    statusArgs.BeforeVersion = commitBefore.Sha;
                                    statusArgs.AfterVersion = commitAfter.Sha;
                                    pathOrCommitString = null;
                                }
                            }
                            else if (repo.Lookup<Commit>(pathOrCommitString) != null)
                            {
                                var commit = repo.Lookup<Commit>(pathOrCommitString);
                                statusArgs.BeforeVersion = commit.Sha;
                                statusArgs.AfterVersion = "WORKING";
                                pathOrCommitString = null;
                            }

                            if (pathOrCommitString != null)
                            {
                                if (statusArgs.Path != null)
                                    statusArgs.Path = Path.Combine(statusArgs.Path, pathOrCommitString);
                                else
                                    statusArgs.Path = pathOrCommitString;
                            }
                        }

                        break;
                    }
                    case 2:
                    {
                        var commitString = args[0];
                        var pathString = default(string);
                        if (args[1] != "--")
                        {
                            pathString = args[1];
                        }

                        args = args.Slice(2);

                        if (StatusArgs.MultipleCommitRegex.IsMatch(commitString))
                        {
                            var match = StatusArgs.MultipleCommitRegex.Match(commitString);
                            var commitBefore = repo.Lookup<Commit>(match.Groups["before"].Value);
                            var commitAfter = repo.Lookup<Commit>(match.Groups["after"].Value);
                            if (commitBefore != null && commitAfter != null)
                            {
                                statusArgs.BeforeVersion = commitBefore.Sha;
                                statusArgs.AfterVersion = commitAfter.Sha;
                                commitString = null;
                            }
                        }
                        else if (repo.Lookup<Commit>(commitString) != null)
                        {
                            var commit = repo.Lookup<Commit>(commitString);
                            statusArgs.BeforeVersion = commit.Sha;
                            statusArgs.AfterVersion = "WORKING";
                            commitString = null;
                        }

                        if (commitString != null)
                        {
                            Console.Error.WriteLine($"Invalid commit {commitString}.");
                            PrintUsage(Console.Error, command);
                            value = null;
                            return false;
                        }

                        if (!string.IsNullOrWhiteSpace(pathString))
                        {
                            if (statusArgs.Path != null)
                                statusArgs.Path = Path.Combine(statusArgs.Path, pathString);
                            else
                                statusArgs.Path = pathString;
                        }

                        break;
                    }
                    case 3:
                    {
                        var commitString = args[0];
                        var dashes = args[1];
                        var pathString = args[2];
                        if (dashes != "--")
                        {
                            Console.Error.WriteLine($"Unknown argument {dashes}.");
                            PrintUsage(Console.Error, command);
                            value = null;
                            return false;
                        }

                        args = args.Slice(3);

                        if (StatusArgs.MultipleCommitRegex.IsMatch(commitString))
                        {
                            var match = StatusArgs.MultipleCommitRegex.Match(commitString);
                            var commitBefore = repo.Lookup<Commit>(match.Groups["before"].Value);
                            var commitAfter = repo.Lookup<Commit>(match.Groups["after"].Value);
                            if (commitBefore != null && commitAfter != null)
                            {
                                statusArgs.BeforeVersion = commitBefore.Sha;
                                statusArgs.AfterVersion = commitAfter.Sha;
                                commitString = null;
                            }
                        }
                        else if (repo.Lookup<Commit>(commitString) != null)
                        {
                            var commit = repo.Lookup<Commit>(commitString);
                            statusArgs.BeforeVersion = commit.Sha;
                            statusArgs.AfterVersion = "WORKING";
                            commitString = null;
                        }

                        if (commitString != null)
                        {
                            Console.Error.WriteLine($"Invalid commit {commitString}.");
                            PrintUsage(Console.Error, command);
                            value = null;
                            return false;
                        }

                        if (!string.IsNullOrWhiteSpace(pathString))
                        {
                            if (statusArgs.Path != null)
                                statusArgs.Path = Path.Combine(statusArgs.Path, pathString);
                            else
                                statusArgs.Path = pathString;
                        }

                        break;
                    }
                }

                if (args.Length > 0)
                {
                    var pathOrCommitString = args[0];
                    args = args.Slice(1);

                    if (pathOrCommitString.Contains(".."))
                    {
                        statusArgs.Path = pathOrCommitString;
                    }
                }

                value = new CommandLineArgs(command, repo, statusArgs);

                break;
            }
            case LaunchCommand.Log:
            {
                var logArgs = new LogArgs();

                if (directorySpec != null)
                    logArgs.Path = directorySpec;

                switch (args.Length)
                {
                    case 0:
                    {
                        break;
                    }
                    case 1:
                    {
                        var pathOrBranchString = args[0];
                        args = args.Slice(1);
                        if (repo.Branches[pathOrBranchString] != null)
                        {
                            logArgs.Branch = pathOrBranchString;
                        }
                        else
                        {
                            logArgs.Path = pathOrBranchString;
                        }

                        break;
                    }
                    case 2:
                    {
                        if (args[0] == "--")
                        {
                            logArgs.Path = args[1];
                        }
                        else
                        {
                            logArgs.Branch = args[0];
                            logArgs.Path = args[1];
                        }

                        args = args.Slice(2);

                        break;
                    }
                    case 3:
                    {
                        logArgs.Branch = args[0];
                        var dashes = args[1];
                        logArgs.Path = args[2];

                        if (dashes != "--")
                        {
                            Console.Error.WriteLine("Too many args.");
                            PrintUsage(Console.Error, command);
                            value = null;
                            return false;
                        }

                        args = args.Slice(3);

                        break;
                    }
                    default:
                    {
                        Console.Error.WriteLine("Too many args.");
                        PrintUsage(Console.Error, command);
                        value = null;
                        return false;
                    }
                }

                if (logArgs.Branch != null && repo.Branches[logArgs.Branch] == null)
                {
                    Console.Error.WriteLine($"Couldn't find branch {logArgs.Branch}.");
                    value = null;
                    return false;
                }

                value = new CommandLineArgs(command, repo, logArgs);

                break;
            }
            default:
            {
                throw new NotImplementedException();
            }
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("Too many arguments provided.");
            Console.Error.WriteLine($"Unknown: {args[0]}");
            PrintUsage(Console.Error, command);
            value = null;
            return false;
        }

        return true;
    }
}