using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace rgit.Views;

public static class Difftool
{
    public static void Run(Repository repository)
    {
        var statusOptions = new StatusOptions
        {
        };
        var statuses = new List<GitStatus>();
        foreach (var item in repository.RetrieveStatus(statusOptions))
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