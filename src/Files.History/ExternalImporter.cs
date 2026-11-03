// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// A file use reported by an external source (Windows recent items, Office MRU).
	/// </summary>
	public readonly record struct ExternalEntry(DateTimeOffset Time, string FilePath);

	/// <summary>
	/// Adds entries from external sources to the log, only those newer than the last import.
	/// </summary>
	public static class ExternalImporter
	{
		/// <returns>The number of events added to the log.</returns>
		public static int Import(HistoryLog log, HistorySource source, IEnumerable<ExternalEntry> entries)
		{
			var last = log.GetLastImported(source);

			var fresh = entries
				.Where(e => IsLocalPath(e.FilePath) && (last is null || e.Time > last))
				.OrderBy(e => e.Time)
				.ToList();

			var added = 0;
			foreach (var entry in fresh)
			{
				var parent = HistoryPath.GetParent(entry.FilePath);
				if (parent is not null && log.Add(new HistoryEvent(entry.Time, entry.FilePath, parent, source)))
					added++;
			}

			if (fresh.Count > 0)
				log.SetLastImported(source, fresh[^1].Time);

			return added;
		}

		/// <summary>
		/// True for drive-letter and UNC paths. URLs (OneDrive, SharePoint) and relative paths are not local.
		/// </summary>
		public static bool IsLocalPath(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return false;

			if (path.StartsWith(@"\\", StringComparison.Ordinal))
				return true;

			return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
		}
	}
}
