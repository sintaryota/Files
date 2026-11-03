// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// In-memory usage events, deduplicated across sources and capped at <see cref="HistoryDefaults.HistorySize"/>. Thread-safe.
	/// </summary>
	public sealed class HistoryLog
	{
		private readonly object gate = new();
		private readonly List<HistoryEvent> events = [];
		private readonly Dictionary<HistorySource, DateTimeOffset> lastImported = [];
		private readonly int capacity;
		private readonly TimeSpan dedupeWindow;

		public HistoryLog(int capacity = HistoryDefaults.HistorySize, int dedupeSeconds = HistoryDefaults.DedupeSeconds)
		{
			this.capacity = capacity;
			dedupeWindow = TimeSpan.FromSeconds(dedupeSeconds);
		}

		/// <summary>Raised after the event list or import times change.</summary>
		public event EventHandler? Changed;

		/// <summary>
		/// Adds an event. Returns false when the same file was already recorded by another source within the dedupe window.
		/// </summary>
		public bool Add(HistoryEvent e)
		{
			var normalized = e with
			{
				FilePath = HistoryPath.Normalize(e.FilePath),
				Folder = HistoryPath.Normalize(e.Folder)
			};

			lock (gate)
			{
				if (events.Any(x =>
					x.Source != normalized.Source &&
					HistoryPath.Comparer.Equals(x.FilePath, normalized.FilePath) &&
					(x.Time - normalized.Time).Duration() <= dedupeWindow))
					return false;

				events.Add(normalized);
				Trim();
			}

			Changed?.Invoke(this, EventArgs.Empty);
			return true;
		}

		public IReadOnlyList<HistoryEvent> Snapshot()
		{
			lock (gate)
				return events.ToList();
		}

		public DateTimeOffset? GetLastImported(HistorySource source)
		{
			lock (gate)
				return lastImported.TryGetValue(source, out var t) ? t : null;
		}

		public void SetLastImported(HistorySource source, DateTimeOffset time)
		{
			lock (gate)
				lastImported[source] = time;

			Changed?.Invoke(this, EventArgs.Empty);
		}

		internal IReadOnlyDictionary<HistorySource, DateTimeOffset> SnapshotImports()
		{
			lock (gate)
				return new Dictionary<HistorySource, DateTimeOffset>(lastImported);
		}

		internal void Load(IEnumerable<HistoryEvent> loaded, IReadOnlyDictionary<HistorySource, DateTimeOffset> imports)
		{
			lock (gate)
			{
				events.Clear();
				events.AddRange(loaded.Select(e => e with
				{
					FilePath = HistoryPath.Normalize(e.FilePath),
					Folder = HistoryPath.Normalize(e.Folder)
				}));
				Trim();

				lastImported.Clear();
				foreach (var (k, v) in imports)
					lastImported[k] = v;
			}

			Changed?.Invoke(this, EventArgs.Empty);
		}

		private void Trim()
		{
			var excess = events.Count - capacity;
			if (excess <= 0)
				return;

			foreach (var old in events.OrderBy(e => e.Time).Take(excess).ToList())
				events.Remove(old);
		}
	}
}
