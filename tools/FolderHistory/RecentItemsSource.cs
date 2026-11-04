// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using Files.History;

namespace FolderHistory
{
	/// <summary>
	/// Imports file use from the Windows "Recent" folder. Shortcuts are read from their bytes, never resolved.
	/// </summary>
	internal sealed class RecentItemsSource : IDisposable
	{
		private const int MaxLinksPerImport = 300;
		private const int MaxLinkBytes = 1024 * 1024;
		private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);

		private readonly HistoryLog log;
		private readonly string directory = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
		private readonly SemaphoreSlim importLock = new(1, 1);
		private readonly System.Threading.Timer debounce;
		private FileSystemWatcher? watcher;
		private volatile bool disposed;

		public RecentItemsSource(HistoryLog log)
		{
			this.log = log;
			debounce = new System.Threading.Timer(_ => ImportNow(), null, Timeout.Infinite, Timeout.Infinite);
		}

		public void Start()
		{
			ImportNow();

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
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
			}
		}

		public void ImportNow() => _ = Task.Run(Import);

		public void Dispose()
		{
			disposed = true;
			watcher?.Dispose();
			debounce.Dispose();
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			if (!disposed)
				debounce.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
		}

		private void Import()
		{
			if (disposed)
				return;

			importLock.Wait();
			try
			{
				ExternalImporter.Import(log, HistorySource.RecentItems, ReadEntries().ToList());
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
			finally
			{
				importLock.Release();
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
				if (file.Length > MaxLinkBytes)
					return null;

				byte[] bytes;
				using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					bytes = new byte[stream.Length];
					stream.ReadExactly(bytes);
				}

				if (ShellLinkParser.TryRead(bytes) is not { } link || link.IsFolder)
					return null;

				var target = Environment.ExpandEnvironmentVariables(link.TargetPath);
				if (string.IsNullOrWhiteSpace(target) || target.EndsWith('\\') || target.EndsWith('/'))
					return null;

				return new ExternalEntry(new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), target);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}
		}
	}
}
