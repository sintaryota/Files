// Copyright (c) Files Community
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class HistoryStoreTests
	{
		private string directory = null!;

		[TestInitialize]
		public void Setup()
		{
			directory = Path.Combine(Path.GetTempPath(), "files-history-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
		}

		[TestCleanup]
		public void Cleanup()
		{
			try { Directory.Delete(directory, true); } catch (IOException) { }
		}

		private string FilePath => Path.Combine(directory, "folder_history.json");

		[TestMethod]
		public async Task SaveThenLoad_RoundTrips()
		{
			var log = new HistoryLog();
			log.Add(new HistoryEvent(Origin, @"C:\a\x.txt", @"C:\a", HistorySource.Files));
			log.Add(new HistoryEvent(Origin.AddMinutes(1), @"C:\b\y.docx", @"C:\b", HistorySource.WordMru));
			log.SetLastImported(HistorySource.RecentItems, Origin.AddMinutes(2));

			var store = new HistoryStore(FilePath);
			await store.SaveAsync(log);

			var loaded = new HistoryLog();
			await new HistoryStore(FilePath).LoadAsync(loaded);

			CollectionAssert.AreEqual(log.Snapshot().ToList(), loaded.Snapshot().ToList());
			Assert.AreEqual(Origin.AddMinutes(2), loaded.GetLastImported(HistorySource.RecentItems));
			Assert.IsNull(loaded.GetLastImported(HistorySource.WordMru));
		}

		[TestMethod]
		public async Task SavedJsonUsesSpecShape()
		{
			var log = new HistoryLog();
			log.Add(new HistoryEvent(Origin, @"C:\a\x.txt", @"C:\a", HistorySource.RecentItems));
			await new HistoryStore(FilePath).SaveAsync(log);

			var json = await File.ReadAllTextAsync(FilePath);
			StringAssert.Contains(json, "\"version\": 1");
			StringAssert.Contains(json, "\"source\": \"recentItems\"");
			StringAssert.Contains(json, "2026-11-03T00:00:00Z");
		}

		[TestMethod]
		public async Task T15_CorruptFile_IsMovedToBakAndLogStartsEmpty()
		{
			await File.WriteAllTextAsync(FilePath, "{ this is not json");
			var log = new HistoryLog();
			var store = new HistoryStore(FilePath);

			await store.LoadAsync(log);

			Assert.HasCount(0, log.Snapshot());
			Assert.IsFalse(File.Exists(FilePath));
			Assert.AreEqual("{ this is not json", await File.ReadAllTextAsync(store.BackupPath));
		}

		[TestMethod]
		public async Task UnknownSource_IsTreatedAsCorrupt()
		{
			await File.WriteAllTextAsync(FilePath,
				"{\"version\":1,\"events\":[{\"time\":\"2026-11-03T00:00:00Z\",\"filePath\":\"C:\\\\a\\\\x\",\"folder\":\"C:\\\\a\",\"source\":\"bogus\"}]}");
			var store = new HistoryStore(FilePath);

			await store.LoadAsync(new HistoryLog());

			Assert.IsTrue(File.Exists(store.BackupPath));
		}

		[TestMethod]
		public async Task MissingFile_LeavesLogEmptyWithoutBackup()
		{
			var store = new HistoryStore(FilePath);

			await store.LoadAsync(new HistoryLog());

			Assert.IsFalse(File.Exists(store.BackupPath));
		}

		[TestMethod]
		public async Task ScheduleSave_DebouncesIntoOneWrite()
		{
			var log = new HistoryLog();
			var store = new HistoryStore(FilePath, TimeSpan.FromMilliseconds(150));

			for (var i = 0; i < 5; i++)
			{
				log.Add(new HistoryEvent(Origin.AddMinutes(i), $@"C:\a\{i}.txt", @"C:\a", HistorySource.Files));
				store.ScheduleSave(log);
			}

			Assert.IsFalse(File.Exists(FilePath));
			await Task.Delay(1000);
			Assert.IsTrue(File.Exists(FilePath));

			var loaded = new HistoryLog();
			await store.LoadAsync(loaded);
			Assert.HasCount(5, loaded.Snapshot());
		}
	}
}
