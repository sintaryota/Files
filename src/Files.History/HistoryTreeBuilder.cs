// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// A node of the compressed history tree.
	/// </summary>
	/// <param name="Name">Folder name shown in the sidebar.</param>
	/// <param name="FullPath">Full path, used for navigation and the tooltip.</param>
	/// <param name="IsLeaf">True for a selected (leaf) folder.</param>
	/// <param name="LastUsed">Latest use among the leaf folders under this node.</param>
	public sealed record HistoryNode(
		string Name,
		string FullPath,
		bool IsLeaf,
		DateTimeOffset LastUsed,
		IReadOnlyList<HistoryNode> Children);

	/// <summary>
	/// Builds the compressed tree: only branch points and leaf folders are kept. Each drive's top branch point is the root.
	/// </summary>
	public static class HistoryTreeBuilder
	{
		public static IReadOnlyList<HistoryNode> Build(IEnumerable<SelectedFolder> folders)
		{
			var result = new List<HistoryNode>();

			foreach (var group in folders.GroupBy(f => HistoryPath.Split(f.Path).Root, HistoryPath.Comparer))
			{
				var root = new TrieNode(group.Key, group.Key);
				foreach (var folder in group)
				{
					var (_, segments) = HistoryPath.Split(folder.Path);
					var node = root;
					foreach (var segment in segments)
						node = node.GetOrAddChild(segment);

					node.LastUsed = node.LastUsed is { } old && old > folder.LastUsed ? old : folder.LastUsed;
				}

				result.AddRange(BuildGroup(root));
			}

			return result
				.OrderByDescending(n => n.LastUsed)
				.ThenBy(n => n.Name, HistoryPath.Comparer)
				.ToList();
		}

		private static IEnumerable<HistoryNode> BuildGroup(TrieNode root)
		{
			var branch = root;
			while (branch.LastUsed is null && branch.Children.Count == 1)
				branch = branch.Children.Values.First();

			// No branching: show the single leaf only.
			if (branch.LastUsed is not null)
				return [ToLeaf(branch)];

			// The branch point is the root, so folders that share it stay visibly under it. Its children are always shown.
			var parents = branch.Children.Values.SelectMany(ToParent).ToList();
			return [ToNode(branch, parents, byRecency: true)];
		}

		private static IEnumerable<HistoryNode> ToParent(TrieNode parent)
		{
			if (parent.LastUsed is not null)
				return [ToLeaf(parent)];

			return [ToNode(parent, parent.Children.Values.SelectMany(Compress))];
		}

		// Drops non-branching folders between a node and what lies below it.
		private static IEnumerable<HistoryNode> Compress(TrieNode node)
		{
			if (node.LastUsed is not null)
				return [ToLeaf(node)];

			if (node.Children.Count == 1)
				return Compress(node.Children.Values.First());

			return [ToNode(node, node.Children.Values.SelectMany(Compress))];
		}

		private static HistoryNode ToLeaf(TrieNode node)
			=> new(node.Name, node.Path, true, node.LastUsed!.Value, []);

		private static HistoryNode ToNode(TrieNode node, IEnumerable<HistoryNode> children, bool byRecency = false)
		{
			var sorted = (byRecency
				? children.OrderByDescending(c => c.LastUsed).ThenBy(c => c.Name, HistoryPath.Comparer)
				: children.OrderBy(c => c.Name, HistoryPath.Comparer)).ToList();
			return new(node.Name, node.Path, false, sorted.Max(c => c.LastUsed), sorted);
		}

		private sealed class TrieNode(string name, string path)
		{
			public string Name { get; } = name;
			public string Path { get; } = path;
			public DateTimeOffset? LastUsed { get; set; }
			public Dictionary<string, TrieNode> Children { get; } = new(HistoryPath.Comparer);

			public TrieNode GetOrAddChild(string segment)
			{
				if (!Children.TryGetValue(segment, out var child))
				{
					var childPath = Path.EndsWith('\\') ? Path + segment : Path + '\\' + segment;
					child = new TrieNode(segment, childPath);
					Children[segment] = child;
				}
				return child;
			}
		}
	}
}
