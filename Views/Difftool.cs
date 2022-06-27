using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace rgit.Views;

public static class Difftool
{
    public static void Run(Repository repository, CommandLineArgs.StatusArgs? args)
    {
        var statuses = new List<GitStatus>();

        // If BeforeVersion is null, we're in a standard diff.
        if (args != null && args.BeforeVersion != null)
        {
            Tree? beforeTree;
            string? beforeSha;

            // Assume before == null means working. We cannot show multiple parents in difftool.
            if (args.BeforeVersion == "WORKING")
            {
                beforeTree = repository.Head.Tip?.Tree;
                beforeSha = "Working Tree";
            }
            else
            {
                var commit = repository.Lookup<Commit>(args.BeforeVersion);
                beforeTree = commit?.Tree;
                beforeSha = commit?.Sha;
            }

            switch (args.AfterVersion)
            {
                case "WORKING":
                case null:
                {
                    var tree = repository.Diff.Compare<TreeChanges>(beforeTree, DiffTargets.WorkingDirectory);
                    foreach (var entry in tree)
                    {
                        statuses.Add(GitStatus.FromChanges(entry, beforeSha, "WORKING"));
                    }

                    break;
                }
                case "STAGING":
                {
                    var tree = repository.Diff.Compare<TreeChanges>(beforeTree, DiffTargets.Index);
                    foreach (var entry in tree)
                    {
                        statuses.Add(GitStatus.FromChanges(entry, beforeSha, "STAGE"));
                    }

                    break;
                }
                default:
                {
                    var afterSha = args.AfterVersion;
                    var after = repository.Lookup<Commit>(args.AfterVersion);
                    var tree = repository.Diff.Compare<TreeChanges>(beforeTree, after.Tree);
                    foreach (var entry in tree)
                    {
                        statuses.Add(GitStatus.FromChanges(entry, beforeSha, afterSha));
                    }

                    break;
                }
            }
        }
        else
        {
            Debug.Assert(args?.AfterVersion == null, "Cannot set after version without setting before version in DiffTool.");

            foreach (var item in repository.RetrieveStatus(new StatusOptions()))
            {
                var status = item.State switch
                {
                    FileStatus.DeletedFromIndex => new GitStatus(item.FilePath, GitStatusString.Deleted),
                    FileStatus.DeletedFromWorkdir => new GitStatus(item.FilePath, GitStatusString.Missing),
                    FileStatus.NewInWorkdir => new GitStatus(item.FilePath, GitStatusString.Unknown),
                    FileStatus.NewInIndex | FileStatus.ModifiedInWorkdir => new GitStatus(item.FilePath, GitStatusString.Added),
                    FileStatus.ModifiedInWorkdir => new GitStatus(item.FilePath, GitStatusString.Modified),
                    FileStatus.ModifiedInIndex => new GitStatus(item.FilePath, GitStatusString.Staged),
                    FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir => new GitStatus(item.FilePath, GitStatusString.Modified),
                    FileStatus.ModifiedInIndex | FileStatus.RenamedInIndex | FileStatus.ModifiedInWorkdir => new GitStatus(item.FilePath, GitStatusString.Renamed),
                    FileStatus.NewInIndex => new GitStatus(item.FilePath, GitStatusString.Added),
                    FileStatus.RenamedInIndex => new GitStatus(item.FilePath, GitStatusString.Renamed, item),
                    FileStatus.RenamedInIndex | FileStatus.ModifiedInWorkdir => new GitStatus(item.FilePath, GitStatusString.Renamed, item),
                    FileStatus.Ignored => null,
                    _ => throw new NotImplementedException($"{item.State} not implemented."),
                };

                if (status != null)
                    statuses.Add(status);
            }
        }

        if (statuses.Count > 20)
        {
            Console.Out.WriteLine($"You're about to open {statuses.Count} diffs. Continue [y/N]?");
            var response = Console.ReadLine();
            if (!string.Equals(response, "y", StringComparison.InvariantCultureIgnoreCase)
                && !string.Equals(response, "yes", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }
        }

        var task = repository.DiffFiles(statuses);
        task.Wait();
    }
}