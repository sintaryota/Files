// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using Files.History;
using Files.Storage;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Files.App.Services
{
	/// <summary>
	/// Imports file use from the Windows "Recent" folder (.lnk files).
	/// </summary>
	internal sealed class RecentItemsHistorySource : IDisposable
	{
		private const int MaxLinksPerImport = 300;
		private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);

		private readonly HistoryLog log;
		private readonly string directory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
		private readonly SemaphoreSlim importLock = new(1, 1);
		private readonly System.Threading.Timer debounce;
		private FileSystemWatcher? watcher;

		public RecentItemsHistorySource(HistoryLog log)
		{
			this.log = log;
			debounce = new System.Threading.Timer(state => _ = ImportAsync(), null, Timeout.Infinite, Timeout.Infinite);
		}

		public void Start()
		{
			_ = ImportAsync();

			try
			{
				if (!Directory.Exists(directory))
					return;

				watcher = new FileSystemWatcher(directory, "*.lnk")
				{
					NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
				};
				watcher.Created += OnChanged;
				watcher.Changed += OnChanged;
				watcher.Renamed += OnChanged;
				watcher.EnableRaisingEvents = true;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not watch the Recent folder.");
			}
		}

		public void Dispose()
		{
			watcher?.Dispose();
			debounce.Dispose();
			importLock.Dispose();
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
			=> debounce.Change(DebounceInterval, Timeout.InfiniteTimeSpan);

		private async Task ImportAsync()
		{
			try
			{
				await importLock.WaitAsync().ConfigureAwait(false);
				try
				{
					var entries = await Task.Run(() => ReadEntries().ToList()).ConfigureAwait(false);
					ExternalImporter.Import(log, HistorySource.RecentItems, entries);
				}
				finally
				{
					importLock.Release();
				}
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not import Windows recent items.");
			}
		}

		private IEnumerable<ExternalEntry> ReadEntries()
		{
			if (!Directory.Exists(directory))
				yield break;

			var lastImported = log.GetLastImported(HistorySource.RecentItems)?.UtcDateTime;

			var links = new DirectoryInfo(directory)
				.EnumerateFiles("*.lnk")
				.Where(f => lastImported is null || f.LastWriteTimeUtc > lastImported)
				.OrderByDescending(f => f.LastWriteTimeUtc)
				.Take(MaxLinksPerImport)
				.ToList();

			foreach (var file in links)
			{
				var entry = TryReadLink(file);
				if (entry is not null)
					yield return entry.Value;
			}
		}

		private static ExternalEntry? TryReadLink(FileInfo file)
		{
			try
			{
				// resolve: false reads the stored target only, so it never shows UI or touches the network.
				using var link = new ShellLink(file.FullName, resolve: false);

				if (link.StoredTargetIsFolder() == true)
					return null;

				var target = Environment.ExpandEnvironmentVariables(link.TargetPath ?? string.Empty);
				if (string.IsNullOrWhiteSpace(target) || target.EndsWith('\\') || target.EndsWith('/'))
					return null;

				return new ExternalEntry(new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), target);
			}
			catch (Exception ex)
			{
				App.Logger.LogDebug(ex, "Folder history skipped an unreadable shortcut.");
				return null;
			}
		}
	}
}
