// Copyright (c) Files Community
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class HistoryLogTests
	{
		private static HistoryEvent At(string file, int seconds, HistorySource source)
			=> new(Origin.AddSeconds(seconds), file, HistoryPath.GetParent(file)!, source);

		[TestMethod]
		public void T8_SameFileFromTwoSourcesWithin30Seconds_IsOneEvent()
		{
			var log = new HistoryLog();

			Assert.IsTrue(log.Add(At(@"C:\a\x.txt", 0, HistorySource.Files)));
			Assert.IsFalse(log.Add(At(@"C:\a\x.txt", 10, HistorySource.RecentItems)));
			Assert.HasCount(1, log.Snapshot());
			Assert.AreEqual(HistorySource.Files, log.Snapshot()[0].Source);
		}

		[TestMethod]
		public void T9_SameFileFromTwoSourcesAfter40Seconds_IsTwoEvents()
		{
			var log = new HistoryLog();

			log.Add(At(@"C:\a\x.txt", 0, HistorySource.Files));
			Assert.IsTrue(log.Add(At(@"C:\a\x.txt", 40, HistorySource.RecentItems)));
			Assert.HasCount(2, log.Snapshot());
		}

		[TestMethod]
		public void SameSourceIsNeverDeduplicated()
		{
			var log = new HistoryLog();

			log.Add(At(@"C:\a\x.txt", 0, HistorySource.Files));
			Assert.IsTrue(log.Add(At(@"C:\a\x.txt", 1, HistorySource.Files)));
		}

		[TestMethod]
		public void DedupeIgnoresCaseAndTrailingSeparator()
		{
			var log = new HistoryLog();

			log.Add(At(@"C:\A\X.txt", 0, HistorySource.Files));
			Assert.IsFalse(log.Add(At(@"c:\a\x.TXT", 5, HistorySource.WordMru)));
		}

		[TestMethod]
		public void T14_101stEventDiscardsTheOldest()
		{
			var log = new HistoryLog();
			for (var i = 0; i < 101; i++)
				log.Add(At($@"C:\a\{i}.txt", i * 100, HistorySource.Files));

			var events = log.Snapshot();
			Assert.HasCount(100, events);
			Assert.IsFalse(events.Any(e => e.FilePath == @"C:\a\0.txt"));
			Assert.IsTrue(events.Any(e => e.FilePath == @"C:\a\100.txt"));
		}

		[TestMethod]
		public void T13_TenFilesMovedIntoOneFolder_IsOneEvent()
		{
			var log = new HistoryLog();
			var recorder = new FolderHistoryRecorder(log);

			var added = recorder.RecordFilesInto(Enumerable.Range(0, 10).Select(i => $@"C:\dest\f{i}.txt"));

			Assert.AreEqual(1, added);
			Assert.HasCount(1, log.Snapshot());
			Assert.AreEqual(@"C:\dest", log.Snapshot()[0].Folder);
		}

		[TestMethod]
		public void FilesIntoTwoFolders_AreTwoEvents()
		{
			var recorder = new FolderHistoryRecorder(new HistoryLog());

			Assert.AreEqual(2, recorder.RecordFilesInto([@"C:\a\1.txt", @"C:\a\2.txt", @"C:\b\3.txt"]));
		}

		[TestMethod]
		public void RecordFileUse_AttributesToContainingFolder()
		{
			var log = new HistoryLog();
			new FolderHistoryRecorder(log).RecordFileUse(@"C:\a\b\c.txt");

			Assert.AreEqual(@"C:\a\b", log.Snapshot().Single().Folder);
		}
	}
}
