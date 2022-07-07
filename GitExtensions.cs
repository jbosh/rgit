using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using KestrelToolbox.Types;
using LibGit2Sharp;

namespace rgit;

public static class GitExtensions
{
    public static string CurrentBranch(this Repository repo)
    {
        foreach (var branch in repo.Branches)
        {
            if (branch.IsCurrentRepositoryHead)
                return branch.FriendlyName;
        }

        return "HEADLESS";
    }

    public static async Task DiffFiles(this Repository repo, IEnumerable<GitStatus> files, string? gitVersion = null, Action? onProgress = null)
    {
        var workDir = repo.WorkingDirectory();

        var tasks = new List<Task>();
        foreach (var file in files)
        {
            onProgress?.Invoke();

            var leftReadOnly = false;
            var rightReadOnly = false;
            var filesToDelete = new List<string>();
            string baseFile, myFile, title1, title2;
            var closeCallback = default(Func<ValueTask>);

            switch (file.Status)
            {
                case GitStatusString.Unknown:
                {
                    if (gitVersion != null)
                        throw new InvalidDataException("Unversioned files cannot have a version.");
                    baseFile = Path.Combine(workDir, file.Path);
                    myFile = Path.Combine(workDir, file.Path);
                    title1 = file.Path;
                    title2 = file.Path;
                    break;
                }
                case GitStatusString.Added:
                {
                    if (gitVersion != null)
                        throw new NotImplementedException();

                    if (file.BranchShaAfter != null)
                    {
                        Debug.Assert(file.BranchShaBefore != null, "Cannot have added file with after and no before.");

                        // Old file is always empty.
                        var emptyFile = TemporaryFiles.GetFilePath(touch: true);
                        filesToDelete.Add(emptyFile);

                        baseFile = emptyFile;
                        title1 = $"{file.Path}: 00000000";
                        leftReadOnly = true;

                        if (file.BranchShaAfter == "WORKING")
                        {
                            title2 = $"{file.Path}: Working Tree";
                            myFile = file.Path;
                        }
                        else
                        {
                            var newFile = TemporaryFiles.GetFilePath(touch: true);
                            filesToDelete.Add(newFile);
                            await repo.GetFileAtChange(newFile, file.Path, file.BranchShaAfter);
                            rightReadOnly = true;
                            myFile = newFile;
                            title2 = $"{file.Path}: {file.BranchShaAfter}";
                        }
                    }
                    else
                    {
                        var tmpFile = TemporaryFiles.GetFilePath(touch: true);
                        filesToDelete.Add(tmpFile);
                        baseFile = tmpFile;
                        myFile = Path.Combine(workDir, file.Path);
                        title1 = file.Path;
                        title2 = $"{file.Path}: 00000000";
                        rightReadOnly = true;
                    }

                    break;
                }
                case GitStatusString.Deleted:
                case GitStatusString.Missing:
                {
                    var originalFile = TemporaryFiles.GetFilePath();
                    var emptyFile = TemporaryFiles.GetFilePath(touch: true);
                    filesToDelete.Add(originalFile);
                    filesToDelete.Add(emptyFile);
                    await repo.GetFileAtChange(originalFile, file.Path, gitVersion ?? "HEAD");

                    if (gitVersion != null)
                    {
                        baseFile = originalFile;
                        title1 = $"{file.Path}: {gitVersion}";
                        myFile = emptyFile;
                        title2 = $"{file.Path}: 000000";
                    }
                    else
                    {
                        if (file.Status == GitStatusString.Deleted)
                        {
                            baseFile = originalFile;
                            title1 = $"{file.Path}: HEAD";
                            myFile = emptyFile;
                            title2 = $"{file.Path}: STAGED";
                        }
                        else
                        {
                            baseFile = originalFile;
                            title1 = $"{file.Path}: HEAD";
                            myFile = emptyFile;
                            title2 = $"{file.Path}: Working Tree";
                        }
                    }

                    leftReadOnly = true;
                    rightReadOnly = true;

                    break;
                }
                case GitStatusString.Modified:
                case GitStatusString.ModifiedAndRenamed:
                {
                    if (gitVersion != null || file.BranchShaAfter != null)
                    {
                        if (file.GitTreeEntryChanges != null)
                        {
                            // We’re doing a log based diff.
                            var entry = file.GitTreeEntryChanges;

                            var oldFile = TemporaryFiles.GetFilePath();
                            var oldBlob = repo.Lookup<Blob>(entry.OldOid);
                            await oldBlob.WriteToFile(oldFile);
                            filesToDelete.Add(oldFile);
                            baseFile = oldFile;

                            title1 = $"{file.Path}: {file.BranchShaBefore}";
                            leftReadOnly = true;

                            if (file.BranchShaAfter == "WORKING")
                            {
                                title2 = $"{file.Path}: Working Tree";
                                myFile = file.Path;
                            }
                            else
                            {
                                var newFile = TemporaryFiles.GetFilePath();
                                var newBlob = repo.Lookup<Blob>(entry.Oid);
                                await newBlob.WriteToFile(newFile);
                                filesToDelete.Add(newFile);
                                myFile = newFile;
                                title2 = $"{file.Path}: {file.BranchShaAfter}";
                                rightReadOnly = true;
                            }
                        }
                        else
                        {
                            if (gitVersion == null)
                                throw new NotImplementedException();

                            // We're doing this diff against working tree.
                            var tmpFile = TemporaryFiles.GetFilePath();
                            await repo.GetFileAtChange(tmpFile, file.Path, gitVersion);
                            filesToDelete.Add(tmpFile);

                            baseFile = tmpFile;
                            myFile = Path.Combine(workDir, file.Path);
                            title1 = $"{file.Path}: {gitVersion}";
                            title2 = $"{file.Path}: Working Tree";
                            leftReadOnly = true;
                        }
                    }
                    else
                    {
                        var tmpFile = TemporaryFiles.GetFilePath();
                        var fileIndexPath = file.Path;
                        if (file.GitStatusEntry != null)
                        {
                            var statusEntry = file.GitStatusEntry;
                            if (statusEntry.HeadToIndexRenameDetails != null)
                                fileIndexPath = statusEntry.HeadToIndexRenameDetails.OldFilePath;
                            else
                                throw new NotImplementedException();
                        }

                        await repo.GetFileAtChange(tmpFile, fileIndexPath, "HEAD");

                        filesToDelete.Add(tmpFile);

                        baseFile = tmpFile;
                        myFile = Path.Combine(workDir, file.Path);
                        title1 = $"{file.Path}: HEAD";
                        title2 = $"{file.Path}: Working Tree";
                        leftReadOnly = true;
                    }

                    break;
                }
                case GitStatusString.Staged:
                {
                    if (gitVersion != null)
                    {
                        var oldFile = TemporaryFiles.GetFilePath();
                        await repo.GetFileAtChange(oldFile, file.Path, gitVersion);
                        filesToDelete.Add(oldFile);

                        baseFile = oldFile;
                        myFile = Path.Combine(workDir, file.Path);
                        title1 = $"{file.Path}: {gitVersion}";
                        title2 = $"{file.Path}: 000000";
                        leftReadOnly = true;
                    }
                    else
                    {
                        var headFile = TemporaryFiles.GetFilePath();
                        var stagedFile = TemporaryFiles.GetFilePath();
                        await repo.GetFileAtChange(headFile, file.Path, "HEAD");
                        await repo.GetFileAtChange(stagedFile, file.Path, "STAGED");
                        filesToDelete.Add(headFile);
                        filesToDelete.Add(stagedFile);

                        baseFile = headFile;
                        myFile = stagedFile;
                        title1 = $"{file.Path}: HEAD";
                        title2 = $"{file.Path}: STAGED";
                        leftReadOnly = true;

                        var modifiedTime = new FileInfo(stagedFile).LastWriteTime;
                        closeCallback = async () =>
                        {
                            var newModTime = new FileInfo(stagedFile).LastWriteTime;
                            if (newModTime != modifiedTime)
                            {
                                await repo.WriteFileToStage(stagedFile, file.Path);
                            }
                        };
                    }

                    break;
                }
                case GitStatusString.Staging:
                {
                    // Staging is a special case where we're staring from working using GUI
                    var stagedFile = TemporaryFiles.GetFilePath();
                    await repo.GetFileAtChange(stagedFile, file.Path, "STAGED");
                    filesToDelete.Add(stagedFile);

                    baseFile = Path.Combine(workDir, file.Path);
                    myFile = stagedFile;
                    title1 = $"{file.Path}: Working Tree";
                    title2 = $"{file.Path}: STAGED";
                    leftReadOnly = true;

                    var modifiedTime = new FileInfo(stagedFile).LastWriteTime;
                    closeCallback = async () =>
                    {
                        var newModTime = new FileInfo(stagedFile).LastWriteTime;
                        if (newModTime != modifiedTime)
                        {
                            await repo.WriteFileToStage(stagedFile, file.Path);
                        }
                    };
                    break;
                }
                case GitStatusString.Renamed:
                {
                    if (file.GitStatusEntry != null)
                    {
                        var statusEntry = file.GitStatusEntry;
                        if (statusEntry.State == FileStatus.RenamedInIndex)
                        {
                            var details = statusEntry.HeadToIndexRenameDetails;
                            var oldFile = TemporaryFiles.GetFilePath();

                            await repo.GetFileAtChange(oldFile, details.OldFilePath, "HEAD");
                            filesToDelete.Add(oldFile);

                            baseFile = oldFile;
                            myFile = Path.Combine(workDir, file.Path);
                            title1 = $"{file.Path}: HEAD";
                            title2 = $"{file.Path}: 000000";
                            leftReadOnly = true;
                            rightReadOnly = false;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    }
                    else if (file.GitTreeEntryChanges != null)
                    {
                        var treeEntry = file.GitTreeEntryChanges;

                        var newFile = TemporaryFiles.GetFilePath();
                        var oldFile = TemporaryFiles.GetFilePath();

                        await repo.GetFileAtChange(newFile, treeEntry.Path, treeEntry.Oid.Sha);
                        await repo.GetFileAtChange(oldFile, treeEntry.OldPath, treeEntry.OldOid.Sha);
                        filesToDelete.Add(newFile);
                        filesToDelete.Add(oldFile);

                        baseFile = oldFile;
                        myFile = Path.Combine(workDir, file.Path);
                        title1 = $"{treeEntry.OldPath}: {treeEntry.Oid.Sha}";
                        title2 = $"{treeEntry.Path}: {treeEntry.OldOid.Sha}";
                        leftReadOnly = true;
                        rightReadOnly = true;
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    break;
                }
                case GitStatusString.Working:
                {
                    if (gitVersion == null)
                        throw new InvalidDataException("working files must have a version.");
                    var leftFile = TemporaryFiles.GetFilePath();
                    var blob = repo.Lookup<Blob>($"{gitVersion}:{file.Path}");
                    await repo.GetFileAtChange(leftFile, file.Path, blob.Sha);
                    filesToDelete.Add(leftFile);

                    baseFile = leftFile;
                    myFile = Path.Combine(workDir, file.Path);
                    title1 = $"{file.Path}: {gitVersion}";
                    title2 = $"{file.Path}: Working Tree";
                    leftReadOnly = true;

                    break;
                }
                default:
                {
                    throw new NotImplementedException();
                }
            }

            var args = new List<string>(Settings.Diff.Command.Select(a => a
                .Replace("%base", baseFile)
                .Replace("%mine", myFile)
                .Replace("%bname", title1)
                .Replace("%yname", title2)));
            if (leftReadOnly)
                args.Add(Settings.Diff.LeftReadOnly);
            if (rightReadOnly)
                args.Add(Settings.Diff.RightReadOnly);

            var proc = new ProcessExtended(args[0], args.Skip(1));
            foreach (var (key, value) in Settings.Diff.Environment)
            {
                proc.Environment.Add(key, value);
            }

            var task = proc
                .RunAsync()
                .ContinueWith(async t =>
                {
                    var code = t.Result;
                    if (code == 127)
                    {
                        await Console.Error.WriteLineAsync($"Could not find '{args[0]}'. Please modify diff path.");
                        throw new FileNotFoundException($"Could not find '{args[0]}'. Please modify diff path.", args[0]);
                    }

                    if (closeCallback != null)
                    {
                        await closeCallback();
                    }

                    foreach (var fileToDelete in filesToDelete)
                    {
                        File.Delete(fileToDelete);
                    }
                });

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }

    public static Task StageFiles(this Repository repo, IEnumerable<GitStatus> files) =>
        Task.Run(() =>
        {
            foreach (var file in files)
            {
                switch (file.Status)
                {
                    case GitStatusString.Missing:
                    {
                        repo.Index.Remove(file.Path);
                        break;
                    }
                    default:
                    {
                        repo.Index.Add(file.Path);
                        break;
                    }
                }
            }

            repo.Index.Write();
        });

    public static Task UnstageFiles(this Repository repo, IEnumerable<GitStatus> files) =>
        Task.Run(() =>
        {
            var head = repo.Head.Tip;
            foreach (var file in files)
            {
                var treeEntry = head[file.Path];
                if (treeEntry != null)
                {
                    repo.Index.Add((Blob)treeEntry.Target, treeEntry.Path, treeEntry.Mode);
                }
            }

            repo.Index.Write();
        });

    public static Task RevertFiles(this Repository repo, IEnumerable<GitStatus> files) =>
        Task.Run(async () =>
        {
            foreach (var file in files)
            {
                TreeEntry treeEntry;
                if (file.GitStatusEntry != null)
                {
                    if (file.GitStatusEntry.HeadToIndexRenameDetails != null)
                        treeEntry = repo.Head[file.GitStatusEntry.HeadToIndexRenameDetails.OldFilePath];
                    else
                        throw new NotImplementedException();
                }
                else
                {
                    treeEntry = repo.Head[file.Path];
                }

                if (treeEntry.Target is Blob blob)
                {
                    await using var stream = File.Open(Path.Combine(repo.WorkingDirectory(), file.Path), FileMode.Create, FileAccess.Write, FileShare.Read);
                    await using var contentStream = blob.GetContentStream();
                    await contentStream.CopyToAsync(stream);
                }
            }
        });

    /// <summary>
    /// Gets a file using revision and path.
    /// </summary>
    /// <param name="repo">Repo to read from.</param>
    /// <param name="destinationPath">Destination file path for where to write blob to.</param>
    /// <param name="filePath">Git path.</param>
    /// <param name="revision">Revision to read from. Can be sha2, HEAD, or STAGED.</param>
    public static async Task GetFileAtChange(this Repository repo, string destinationPath, string filePath, string? revision = null)
    {
        Blob blob;
        switch (revision)
        {
            case null:
            {
                throw new NotImplementedException();
            }
            case "STAGED":
            {
                var indexEntry = repo.Index[filePath];
                if (indexEntry == null)
                {
                    // File doesn't exist in index, make an empty file.
                    await using var file = File.Open(destinationPath, FileMode.Create);
                    return;
                }
                else
                {
                    blob = repo.Lookup<Blob>(indexEntry.Id);
                }

                break;
            }
            case "HEAD":
            {
                blob = repo.Lookup<Blob>($"HEAD:{filePath}");
                break;
            }
            default:
            {
                // Ignore path in this case because we're grabbing the blob directly.
                blob = repo.Lookup<Blob>(revision);

                // If blob is null, try to get it using filePath
                if (blob == null)
                {
                    blob = repo.Lookup<Blob>($"{revision}:{filePath}");
                }

                break;
            }
        }

        await blob.WriteToFile(destinationPath);
    }

    public static async Task WriteToFile(this Blob blob, string path)
    {
        await using var file = File.Open(path, FileMode.Create);
        await blob.GetContentStream().CopyToAsync(file);
    }

    /// <summary>
    /// Writes a file to the stage.
    /// </summary>
    /// <param name="repo">Repo to write to.</param>
    /// <param name="sourcePath">Path to read data from.</param>
    /// <param name="filePath">Git path.</param>
    public static async Task WriteFileToStage(this Repository repo, string sourcePath, string filePath)
    {
        Blob writtenObject;
        await using (var stream = File.OpenRead(Path.Combine(repo.WorkingDirectory(), sourcePath)))
        {
            var objectId = repo.ObjectDatabase.Write<Blob>(stream, stream.Length);
            writtenObject = repo.Lookup<Blob>(objectId);
        }

        var indexEntry = repo.Index[filePath];
        var mode = default(Mode?);
        if (indexEntry == null)
        {
            var blob = repo.Lookup<Blob>($"HEAD:{filePath}");
            if (blob != null)
            {
                var treeEntry = repo.Head[filePath];
                if (treeEntry != null)
                {
                    mode = treeEntry.Mode;
                }
            }
        }

        if (mode == null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotImplementedException("Need to get mode from platform.");
        }

        repo.Index.Add(writtenObject, filePath, mode ?? Mode.NonExecutableFile);
        repo.Index.Write();
    }

    /// <summary>
    /// Committing the libgit2 way is a pain if you want to add in signatures and all that. This method calls out to a git exe.
    /// This will commit HEAD.
    /// </summary>
    /// <param name="repo">Repository to commit to.</param>
    /// <param name="message">Message to write.</param>
    /// <param name="amend">True if the commit should be amended to the previous commit.</param>
    /// <returns>Null if there were no errors, non-null if there were.</returns>
    /// <remarks>
    /// Doing this better in the future will probably involve something like this:
    /// https://github.com/libgit2/libgit2sharp/issues/1900
    /// But, using gpg4win is only 32 bit, so I'm less motivated because I'd probably use the command line for gpg anyway. May
    /// as well let git do most of the lifting considering there's a lot that goes into a commit.
    /// </remarks>
    public static async Task<string?> Commit(this Repository repo, string message, bool amend = false)
    {
        var messageFile = TemporaryFiles.GetFilePath();
        await File.WriteAllTextAsync(messageFile, message);

        var args = new List<string> { "commit", "-q", "-F", messageFile };
        if (amend)
            args.Add("--amend");

        var outputBuilder = new StringBuilder();
        var proc = new ProcessExtended("git", args);
        proc.WorkingDirectory = repo.WorkingDirectory();
        proc.OnData += e => outputBuilder.AppendLine(e);
        proc.OnError += e => outputBuilder.AppendLine(e);
        var exitCode = await proc.RunAsync();

        TemporaryFiles.SafeDelete(messageFile);
        return exitCode == 0 ? null : outputBuilder.ToString();
    }

    public static bool TryGet(this BranchCollection collection, string branchName, [NotNullWhen(true)] out Branch? branch)
    {
        try
        {
            branch = collection[branchName];
            return branch != null;
        }
        catch (InvalidSpecificationException)
        {
            branch = null;
            return false;
        }
    }
}