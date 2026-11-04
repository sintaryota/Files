// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	public enum CandidateOrigin
	{
		/// <summary>One of the most recently used folders.</summary>
		Recent,

		/// <summary>Picked because it is used often.</summary>
		Frequent
	}

	public enum CandidateOutcome
	{
		Selected,
		SelectedReplacingAncestors,
		ReplacedByDescendant,
		SkippedHasSelectedDescendant,
		SkippedMissing,
		NotEvaluated
	}

	/// <summary>
	/// Why a candidate folder was or was not chosen. Used by diagnostics.
	/// </summary>
	public sealed class CandidateReport(string path, int count, DateTimeOffset lastUsed, CandidateOrigin origin)
	{
		public string Path { get; } = path;
		public int Count { get; } = count;
		public DateTimeOffset LastUsed { get; } = lastUsed;
		public CandidateOrigin Origin { get; } = origin;
		public CandidateOutcome Outcome { get; internal set; } = CandidateOutcome.NotEvaluated;

		/// <summary>The other folder involved in the decision, when there is one.</summary>
		public string? Detail { get; internal set; }
	}

	public sealed record SelectionReport(
		IReadOnlyList<SelectedFolder> Selected,
		IReadOnlyList<CandidateReport> Candidates,
		int EventsConsidered);

	/// <summary>
	/// Chooses the folders to display from the usage events.
	/// </summary>
	public static class FolderSelector
	{
		/// <inheritdoc cref="SelectWithReport"/>
		public static IReadOnlyList<SelectedFolder> Select(
			IEnumerable<HistoryEvent> events,
			Func<string, bool> exists,
			int historySize = HistoryDefaults.HistorySize,
			int recentRequired = HistoryDefaults.RecentRequired,
			int maxDisplay = HistoryDefaults.MaxDisplay)
			=> SelectWithReport(events, exists, historySize, recentRequired, maxDisplay).Selected;

		/// <param name="events">Usage events in any order. Only the newest <paramref name="historySize"/> are considered.</param>
		/// <param name="exists">Existence check. Called lazily, at most once per candidate; may be slow, so call this off the UI thread.</param>
		public static SelectionReport SelectWithReport(
			IEnumerable<HistoryEvent> events,
			Func<string, bool> exists,
			int historySize = HistoryDefaults.HistorySize,
			int recentRequired = HistoryDefaults.RecentRequired,
			int maxDisplay = HistoryDefaults.MaxDisplay)
		{
			var stats = new Dictionary<string, FolderStats>(HistoryPath.Comparer);
			var considered = 0;
			foreach (var e in events.OrderByDescending(e => e.Time).Take(historySize))
			{
				considered++;
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

			var recent = stats.Values
				.OrderByDescending(s => s.LastUsed)
				.ThenBy(s => s.Path, HistoryPath.Comparer)
				.Take(recentRequired)
				.ToList();
			var recentSet = new HashSet<string>(recent.Select(s => s.Path), HistoryPath.Comparer);

			var frequent = stats.Values
				.Where(s => !recentSet.Contains(s.Path))
				.OrderByDescending(s => s.Count)
				.ThenByDescending(s => s.LastUsed)
				.ThenBy(s => s.Path, HistoryPath.Comparer);

			var candidates = recent.Select(s => new CandidateReport(s.Path, s.Count, s.LastUsed, CandidateOrigin.Recent))
				.Concat(frequent.Select(s => new CandidateReport(s.Path, s.Count, s.LastUsed, CandidateOrigin.Frequent)))
				.ToList();

			var selected = new List<CandidateReport>();
			foreach (var c in candidates)
			{
				if (selected.Count >= maxDisplay)
					break;

				var descendant = selected.FirstOrDefault(s => HistoryPath.IsAncestor(c.Path, s.Path));
				if (descendant is not null)
				{
					c.Outcome = CandidateOutcome.SkippedHasSelectedDescendant;
					c.Detail = descendant.Path;
					continue;
				}

				if (!exists(c.Path))
				{
					c.Outcome = CandidateOutcome.SkippedMissing;
					continue;
				}

				var ancestors = selected.Where(s => HistoryPath.IsAncestor(s.Path, c.Path)).ToList();
				foreach (var ancestor in ancestors)
				{
					ancestor.Outcome = CandidateOutcome.ReplacedByDescendant;
					ancestor.Detail = c.Path;
					selected.Remove(ancestor);
				}

				c.Outcome = ancestors.Count > 0 ? CandidateOutcome.SelectedReplacingAncestors : CandidateOutcome.Selected;
				c.Detail = ancestors.Count > 0 ? string.Join(" , ", ancestors.Select(a => a.Path)) : null;
				selected.Add(c);
			}

			return new SelectionReport(
				selected.Select(s => new SelectedFolder(s.Path, s.LastUsed)).ToList(),
				candidates,
				considered);
		}

		private sealed class FolderStats(string path, DateTimeOffset lastUsed)
		{
			public string Path { get; } = path;
			public int Count { get; set; } = 1;
			public DateTimeOffset LastUsed { get; set; } = lastUsed;
		}
	}
}
