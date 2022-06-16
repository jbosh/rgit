using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LibGit2Sharp;

namespace rgit;

public static class GitGraph
{
    public static Node[] GenerateGraph(Repository repo, Commit tip, string? path)
    {
        var visited = new HashSet<string>();
        var allNodes = new Dictionary<string, WorkingNode>();
        var segments = new Dictionary<string, Segment>();
        var index = 0;

        // Recursively fill out log graph.
        {
            var stackFrame = new Stack<(Commit commit, Segment segment, bool isHead)>();
            stackFrame.Push((tip, new Segment(), false));
            while (stackFrame.Count > 0)
            {
                var (commit, segment, isHead) = stackFrame.Pop();
                if (visited.Contains(commit.Sha))
                    continue;

                if (isHead)
                {
                    visited.Add(commit.Sha);

                    var node = new WorkingNode(commit, index);
                    node.Column = segment.Identifier;

                    if (path != null)
                    {
                        var visible = false;

                        var parents = commit.Parents.ToArray();
                        if (parents.Length == 0)
                        {
                            visible = commit.Tree.Any(e => e.Name.StartsWith(path));
                        }
                        else
                        {
                            foreach (var parent in commit.Parents)
                            {
                                var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                                visible |= changes.Any(e => e.Path.StartsWith(path) || e.OldPath.StartsWith(path));
                            }
                        }

                        node.Hidden = !visible;
                    }

                    allNodes.Add(commit.Sha, node);
                    segments.Add(commit.Sha, segment);
                    index++;
                }
                else
                {
                    var parents = commit.Parents.ToArray();
                    stackFrame.Push((commit, segment, true));
                    switch (parents.Length)
                    {
                        case 0:
                        {
                            break;
                        }
                        case 1:
                        {
                            stackFrame.Push((parents[0], segment, false));
                            break;
                        }
                        case 2:
                        {
                            stackFrame.Push((parents[1], new Segment(), false));
                            stackFrame.Push((parents[0], segment, false));
                            break;
                        }
                        default:
                        {
                            throw new NotImplementedException($"Unsupported {parents.Length} parents of a commit.");
                        }
                    }
                }
            }
        }

        var drawnNodes = allNodes.Values.OrderByDescending(n => n.TopologicalOrder).ToArray();
        for (var i = 0; i < drawnNodes.Length; i++)
        {
            var node = drawnNodes[i];
            node.Row = i;
            var segment = segments[node.Sha];
            if (segment.Start == null)
            {
                segment.Start = node.Sha;
            }

            segment.End = node.Sha;
        }

        // Match up children/parents
        foreach (var commit in drawnNodes)
        {
            var parents = commit.ParentShas;
            switch (parents.Length)
            {
                case 0:
                {
                    break;
                }
                case 1:
                {
                    commit.Parent0 = allNodes[parents[0]];
                    commit.Parent0.Children.Add(commit);
                    break;
                }
                case 2:
                {
                    commit.Parent0 = allNodes[parents[0]];
                    commit.Parent0.Children.Add(commit);
                    commit.Parent1 = allNodes[parents[1]];
                    commit.Parent1.Children.Add(commit);
                    break;
                }
                default:
                {
                    throw new NotImplementedException($"Unsupported {parents.Length} parents of a commit.");
                }
            }
        }

        // Determine X coordinates of nodes
        {
            var activeBranches = new Dictionary<Segment, int>();
            var branchRemovals = new Dictionary<int, List<Segment>>();

            for (var nodeRow = 0; nodeRow < drawnNodes.Length; nodeRow++)
            {
                var node = drawnNodes[nodeRow];
                var segment = segments[node.Sha];

                // Add to segments if we don't exist
                if (!activeBranches.TryGetValue(segment, out var column))
                {
                    Debug.Assert(segment.Start == node.Sha, "Starting node should match start of segment.");
                    column = Enumerable.Range(0, int.MaxValue).First(i => !activeBranches.ContainsValue(i));
                    activeBranches[segment] = column;

                    if (node.Children.Count != 0)
                    {
                        var highestRow = node.Children.Min(c => c.Row);
                        for (var i = node.Row - 1; i > highestRow; i--)
                        {
                            if (!drawnNodes[i].Lines.Contains(column))
                                drawnNodes[i].Lines.Add(column);
                        }
                    }
                }

                // Remove branch if it's enqueued
                if (branchRemovals.Remove(node.Row, out var removalSegments))
                {
                    foreach (var s in removalSegments)
                        activeBranches.Remove(s);
                }

                // Queue segment removal if we're at the end
                if (segment.End == node.Sha)
                {
                    if (node.Parent0 == null)
                    {
                        activeBranches.Remove(segment);
                    }
                    else
                    {
                        var removalRow = node.Parent0.Row;
                        if (node.Parent1 != null)
                        {
                            if (node.Parent0.Row < node.Parent1.Row)
                                removalRow = node.Parent1.Row;
                        }

                        if (branchRemovals.TryGetValue(removalRow, out removalSegments))
                            removalSegments.Add(segment);
                        else
                            branchRemovals.Add(removalRow, new List<Segment>() { segment });
                    }
                }

                node.Lines = activeBranches.Values.OrderBy(i => i).ToList();
                node.Column = column;
            }

            Debug.Assert(branchRemovals.Count == 0, "Didn't remove all branches.");
            Debug.Assert(activeBranches.Count == 0, "Didn't complete all active branches.");
        }

        var finalNodeLookup = new Dictionary<string, Node>();

        // Fixup a new nodes list
        foreach (var node in drawnNodes)
        {
            var children = node.Children.Select(c => finalNodeLookup[c.Sha]).ToArray();
            var newNode = new Node(
                node.Row,
                node.Column,
                node.Sha,
                null,
                null,
                node.Message,
                node.MessageShort,
                node.Author,
                node.Committer,
                children,
                node.Lines.ToArray());
            finalNodeLookup[node.Sha] = newNode;
        }

        foreach (var node in drawnNodes)
        {
            var newNode = finalNodeLookup[node.Sha];
            if (node.Parent0 != null)
                newNode.Parent1 = finalNodeLookup[node.Parent0.Sha];
            if (node.Parent1 != null)
                newNode.Parent1 = finalNodeLookup[node.Parent1.Sha];
        }

        // Only add visible nodes
        var result = new List<Node>();
        foreach (var node in drawnNodes)
        {
            if (node.Hidden)
                continue;

            var newNode = finalNodeLookup[node.Sha];
            result.Add(newNode);
        }

        return result.ToArray();
    }

    [DebuggerDisplay("[{Column}] {MessageShort}")]
    private sealed class WorkingNode
    {
        public int TopologicalOrder { get; set; }
        public int Row { get; set; } = -1;
        public int Column { get; set; } = -1;
        public WorkingNode? Parent0 { get; set; }
        public WorkingNode? Parent1 { get; set; }
        public List<WorkingNode> Children { get; } = new();
        public List<int> Lines { get; set; } = new();
        public string Sha { get; }
        public string[] ParentShas { get; set; }
        public string Message { get; }
        public string MessageShort { get; }
        public Signature Author { get; }
        public Signature Committer { get; }
        public bool Hidden { get; set; }
        public override string ToString() => this.MessageShort;

        public WorkingNode(Commit commit, int topologicalOrder)
        {
            this.Sha = commit.Sha;
            this.ParentShas = commit.Parents.Select(p => p.Sha).ToArray();
            this.TopologicalOrder = topologicalOrder;
            this.Message = commit.Message;
            this.MessageShort = commit.MessageShort;
            this.Author = commit.Author;
            this.Committer = commit.Committer;
        }
    }

    [DebuggerDisplay("[{Column}] {MessageShort}")]
    public class Node
    {
        public int Row { get; }
        public int Column { get; }
        public Node? Parent0 { get; internal set; }
        public Node? Parent1 { get; internal set; }
        public Node[] Children { get; }
        public int[] Lines { get; }
        public string Sha { get; }
        public string Message { get; }
        public string MessageShort { get; }
        public Signature Author { get; }
        public Signature Committer { get; }
        public override string ToString() => this.MessageShort;

        public Node(
            int row,
            int column,
            string sha,
            Node? parent0,
            Node? parent1,
            string message,
            string messageShort,
            Signature author,
            Signature committer,
            Node[] children,
            int[] lines)
        {
            this.Row = row;
            this.Column = column;
            this.Sha = sha;
            this.Parent0 = parent0;
            this.Parent1 = parent1;
            this.Message = message;
            this.MessageShort = messageShort;
            this.Author = author;
            this.Committer = committer;

            this.Children = children;
            this.Lines = lines;
        }
    }

    [DebuggerDisplay("{Identifier}")]
    private sealed class Segment : IComparable<Segment>, IEquatable<Segment>
    {
        public string? Start { get; set; }
        public string? End { get; set; }
        public int Identifier { get; } = IdentifierCount++;
        private static int IdentifierCount { get; set; }

        public static bool operator ==(Segment a, Segment b) => a.Equals(b);
        public static bool operator !=(Segment a, Segment b) => !a.Equals(b);
        public static bool operator <(Segment a, Segment b) => a.Identifier < b.Identifier;
        public static bool operator <=(Segment a, Segment b) => a.Identifier <= b.Identifier;
        public static bool operator >(Segment a, Segment b) => a.Identifier > b.Identifier;
        public static bool operator >=(Segment a, Segment b) => a.Identifier >= b.Identifier;
        public int CompareTo(Segment? other) => this.Identifier.CompareTo(other?.Identifier ?? -1);

        public override bool Equals(object? obj)
        {
            if (obj is Segment s)
                return this.Equals(s);
            return false;
        }

        public bool Equals(Segment? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return this.Identifier == other.Identifier;
        }

        public override int GetHashCode()
        {
            return this.Identifier;
        }
    }
}