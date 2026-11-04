// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// Entry point for recording file use. Attributes each use to the folder that directly holds the file.
	/// </summary>
	public sealed class FolderHistoryRecorder(HistoryLog log, TimeProvider? timeProvider = null)
	{
		private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

		public HistoryLog Log { get; } = log;

		/// <summary>
		/// Records one use of a file (opened, renamed).
		/// </summary>
		public bool RecordFileUse(string filePath, HistorySource source = HistorySource.Files, DateTimeOffset? at = null)
		{
			var parent = HistoryPath.GetParent(filePath);
			if (parent is null)
				return false;

			return Log.Add(new HistoryEvent(at ?? time.GetUtcNow(), filePath, parent, source));
		}

		/// <summary>
		/// Records files that were moved or copied. One event is written per destination folder, however many files it received.
		/// </summary>
		public int RecordFilesInto(IEnumerable<string> destinationFilePaths)
		{
			var now = time.GetUtcNow();
			var added = 0;

			foreach (var group in destinationFilePaths
				.Select(p => (Path: p, Parent: HistoryPath.GetParent(p)))
				.Where(x => x.Parent is not null)
				.GroupBy(x => x.Parent!, HistoryPath.Comparer))
			{
				if (Log.Add(new HistoryEvent(now, group.First().Path, group.Key, HistorySource.Files)))
					added++;
			}

			return added;
		}
	}
}
