// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.History;

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// Records which folders the user works in and exposes the log the sidebar history is built from.
	/// </summary>
	public interface IFolderHistoryService
	{
		HistoryLog Log { get; }

		/// <summary>Loads the saved history. Recording calls made before it completes are applied afterwards.</summary>
		Task InitializeAsync();

		void RecordFileOpened(string filePath);

		void RecordFileRenamed(string newFilePath);

		/// <summary>Records files that arrived in a folder by move or copy: one event per destination folder.</summary>
		void RecordFilesInto(IEnumerable<string> destinationFilePaths);
	}
}
