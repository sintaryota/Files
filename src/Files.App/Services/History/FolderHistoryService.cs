// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.History;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Storage;

namespace Files.App.Services
{
	internal sealed class FolderHistoryService : IFolderHistoryService
	{
		private const string FileName = "folder_history.json";

		private readonly object gate = new();
		private readonly List<Action> pending = [];
		private readonly HistoryStore store;
		private readonly FolderHistoryRecorder recorder;
		private readonly List<IDisposable> sources = [];
		private bool initialized;

		public HistoryLog Log { get; } = new();

		public FolderHistoryService()
		{
			store = new HistoryStore(
				Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName),
				onError: ex => App.Logger.LogWarning(ex, "Folder history storage failed."));
			recorder = new FolderHistoryRecorder(Log);

			// Files' own data folder holds this history and the diagnostics report; opening them must not count as use.
			var dataFolder = HistoryPath.Normalize(ApplicationData.Current.LocalFolder.Path);
			Log.Exclude = path => HistoryPath.IsAncestor(dataFolder, path);
			Log.Changed += (_, _) => { if (initialized) store.ScheduleSave(Log); };
		}

		public async Task InitializeAsync()
		{
			try
			{
				await store.LoadAsync(Log).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not be loaded.");
			}

			List<Action> queued;
			lock (gate)
			{
				initialized = true;
				queued = [.. pending];
				pending.Clear();
			}

			foreach (var action in queued)
				Run(action);

			StartExternalSources();
		}

		// Runs after the saved history is loaded so imports compare against the stored last-import times.
		private void StartExternalSources()
		{
			try
			{
				var recent = new RecentItemsHistorySource(Log);
				var word = new OfficeMruHistorySource(Log, "Word", HistorySource.WordMru);
				sources.Add(recent);
				sources.Add(word);
				recent.Start();
				word.Start();
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not start its external sources.");
			}
		}

		public void RecordFileOpened(string filePath) => Enqueue(() => recorder.RecordFileUse(filePath));

		public void RecordFileRenamed(string newFilePath) => Enqueue(() => recorder.RecordFileUse(newFilePath));

		public void RecordFilesInto(IEnumerable<string> destinationFilePaths)
		{
			var paths = destinationFilePaths.ToList();
			Enqueue(() => recorder.RecordFilesInto(paths));
		}

		// Recording must never break the file operation that triggered it.
		private void Enqueue(Action action)
		{
			lock (gate)
			{
				if (!initialized)
				{
					pending.Add(action);
					return;
				}
			}

			Run(action);
		}

		private static void Run(Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not record an event.");
			}
		}
	}
}
