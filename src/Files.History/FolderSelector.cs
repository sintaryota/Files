// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// Chooses the folders to display from the usage events.
	/// </summary>
	public static class FolderSelector
	{
		/// <param name="events">Usage events in any order. Only the newest <paramref name="historySize"/> are considered.</param>
		/// <param name="exists">Existence check. Called lazily, at most once per candidate; may be slow, so call this off the UI thread.</param>
		public static IReadOnlyList<SelectedFolder> Select(
			IEnumerable<HistoryEvent> events,
			Func<string, bool> exists,
			int historySize = HistoryDefaults.HistorySize,
			int recentRequired = HistoryDefaults.RecentRequired,
			int maxDisplay = HistoryDefaults.MaxDisplay)
		{
			var stats = new Dictionary<string, FolderStats>(HistoryPath.Comparer);
			foreach (var e in events.OrderByDescending(e => e.Time).Take(historySize))
			{
				var folder = HistoryPath.Normalize(e.Folder);
				if (stats.TryGetValue(folder, out var s))
				{
					s.Count++;
					if (e.Time > s.LastUsed)
						s.LastUsed = e.Time;
				}
				else
				{
					stats[folder] = new FolderStats(folder, e.Time);
				}
			}

			var byRecency = stats.Values
				.OrderByDescending(s => s.LastUsed)
				.ThenBy(s => s.Path, HistoryPath.Comparer)
				.ToList();

			var recent = byRecency.Take(recentRequired).ToList();
			var recentSet = new HashSet<string>(recent.Select(s => s.Path), HistoryPath.Comparer);

			var frequent = stats.Values
				.Where(s => !recentSet.Contains(s.Path))
				.OrderByDescending(s => s.Count)
				.ThenByDescending(s => s.LastUsed)
				.ThenBy(s => s.Path, HistoryPath.Comparer);

			var selected = new List<FolderStats>();
			foreach (var c in recent.Concat(frequent))
			{
				if (selected.Count >= maxDisplay)
					break;

				if (selected.Any(s => HistoryPath.IsAncestor(c.Path, s.Path)))
					continue;

				if (!exists(c.Path))
					continue;

				selected.RemoveAll(s => HistoryPath.IsAncestor(s.Path, c.Path));
				selected.Add(c);
			}

			return selected.Select(s => new SelectedFolder(s.Path, s.LastUsed)).ToList();
		}

		private sealed class FolderStats(string path, DateTimeOffset lastUsed)
		{
			public string Path { get; } = path;
			public int Count { get; set; } = 1;
			public DateTimeOffset LastUsed { get; set; } = lastUsed;
		}
	}
}
