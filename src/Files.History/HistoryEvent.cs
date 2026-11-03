// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	public enum HistorySource
	{
		Files,
		RecentItems,
		WordMru
	}

	/// <summary>
	/// One recorded use of a file. <paramref name="Folder"/> is the folder the history is attributed to.
	/// </summary>
	public sealed record HistoryEvent(DateTimeOffset Time, string FilePath, string Folder, HistorySource Source);

	/// <summary>
	/// A folder chosen for display.
	/// </summary>
	public sealed record SelectedFolder(string Path, DateTimeOffset LastUsed);
}
