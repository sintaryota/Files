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
	/// Builds the compressed tree: only branch points and leaf folders are kept. The folders directly under the drive (or a base folder such as the user profile) are the parents.
	/// </summary>
	public static class HistoryTreeBuilder
	{
		/// <param name="folders">The folders to show.</param>
		/// <param name="baseFolders">
		/// Folders whose direct children become the top level, such as the user profile.
		/// A folder outside all of them is placed under its drive (or share) root instead.
		/// </param>
		public static IReadOnlyList<HistoryNode> Build(IEnumerable<SelectedFolder> folders, IEnumerable<string>? baseFolders = null)
		{
			var bases = (baseFolders ?? []).Select(HistoryPath.Normalize).ToList();
			var result = new List<HistoryNode>();

			foreach (var group in folders.GroupBy(f => FindBase(f.Path, bases), HistoryPath.Comparer))
			{
				var (_, baseSegments) = HistoryPath.Split(group.Key);
				var root = new TrieNode(baseSegments.Length > 0 ? baseSegments[^1] : group.Key, group.Key);

				foreach (var folder in group)
				{
					var (_, segments) = HistoryPath.Split(folder.Path);
					var node = root;
					foreach (var segment in segments.Skip(baseSegments.Length))
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

		// The nearest base folder above (or equal to) the path, else the drive root.
		private static string FindBase(string path, List<string> bases)
		{
			var best = bases
				.Where(b => HistoryPath.Comparer.Equals(b, path) || HistoryPath.IsAncestor(b, path))
				.OrderByDescending(b => b.Length)
				.FirstOrDefault();

			return best ?? HistoryPath.Split(path).Root;
		}

		// The folders directly under the base are the parents. They are always shown; below them only branch points and leaves remain.
		private static IEnumerable<HistoryNode> BuildGroup(TrieNode root)
		{
			var parents = new List<HistoryNode>();
			if (root.LastUsed is not null)
				parents.Add(ToLeaf(root));

			parents.AddRange(root.Children.Values.SelectMany(ToParent));
			return parents;
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
