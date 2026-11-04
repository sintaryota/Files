// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class ExternalImporterTests
	{
		private static string Mru(DateTimeOffset time, string path)
			=> $"[F00000000][T{time.UtcDateTime.ToFileTimeUtc():X16}][O00000000]*{path}";

		[TestMethod]
		public void Parse_ReadsTimeAndPath()
		{
			var entry = OfficeMruParser.Parse(Mru(Origin, @"C:\docs\a.docx"));

			Assert.IsNotNull(entry);
			Assert.AreEqual(Origin, entry.Value.Time);
			Assert.AreEqual(@"C:\docs\a.docx", entry.Value.FilePath);
		}

		[TestMethod]
		[DataRow(null)]
		[DataRow("")]
		[DataRow("no star here")]
		[DataRow("[F00000000][O00000000]*C:\\a.docx")]
		[DataRow("[F00000000][TZZZZ][O00000000]*C:\\a.docx")]
		[DataRow("[F00000000][T0000000000000000][O00000000]*C:\\a.docx")]
		[DataRow("[F00000000][T01DA3C5A12345678][O00000000]*")]
		public void Parse_RejectsMalformedValues(string? value)
			=> Assert.IsNull(OfficeMruParser.Parse(value));

		[TestMethod]
		public void T10_UrlPathsAreExcluded()
		{
			var log = new HistoryLog();
			var entry = OfficeMruParser.Parse(Mru(Origin, "https://contoso.sharepoint.com/sites/x/a.docx"));

			Assert.AreEqual(0, ExternalImporter.Import(log, HistorySource.WordMru, [entry!.Value]));
			Assert.HasCount(0, log.Snapshot());
		}

		[TestMethod]
		public void OnlyEntriesNewerThanLastImportAreAdded()
		{
			var log = new HistoryLog();
			var first = new[]
			{
				new ExternalEntry(Origin, @"C:\a\1.docx"),
				new ExternalEntry(Origin.AddMinutes(1), @"C:\a\2.docx")
			};

			Assert.AreEqual(2, ExternalImporter.Import(log, HistorySource.WordMru, first));
			Assert.AreEqual(Origin.AddMinutes(1), log.GetLastImported(HistorySource.WordMru));

			var second = first.Append(new ExternalEntry(Origin.AddMinutes(5), @"C:\b\3.docx"));
			Assert.AreEqual(1, ExternalImporter.Import(log, HistorySource.WordMru, second));
			Assert.HasCount(3, log.Snapshot());
			Assert.AreEqual(Origin.AddMinutes(5), log.GetLastImported(HistorySource.WordMru));
		}

		[TestMethod]
		public void EventUsesContainingFolderAndGivenSource()
		{
			var log = new HistoryLog();
			ExternalImporter.Import(log, HistorySource.RecentItems, [new ExternalEntry(Origin, @"C:\a\b\1.xlsx")]);

			var e = log.Snapshot().Single();
			Assert.AreEqual(@"C:\a\b", e.Folder);
			Assert.AreEqual(HistorySource.RecentItems, e.Source);
		}

		[TestMethod]
		public void ImportingTheSameFileAsAnotherSourceWithin30Seconds_IsDeduplicated()
		{
			var log = new HistoryLog();
			new FolderHistoryRecorder(log).RecordFileUse(@"C:\a\1.docx", HistorySource.Files, Origin);

			var added = ExternalImporter.Import(log, HistorySource.RecentItems, [new ExternalEntry(Origin.AddSeconds(10), @"C:\a\1.docx")]);

			Assert.AreEqual(0, added);
			Assert.HasCount(1, log.Snapshot());
		}

		[TestMethod]
		[DataRow(@"C:\a\b.txt", true)]
		[DataRow(@"c:/a/b.txt", true)]
		[DataRow(@"\\srv\share\b.txt", true)]
		[DataRow("https://x/y.docx", false)]
		[DataRow("http://x/y.docx", false)]
		[DataRow(@"a\b.txt", false)]
		[DataRow("", false)]
		public void IsLocalPath(string path, bool expected)
			=> Assert.AreEqual(expected, ExternalImporter.IsLocalPath(path));
	}
}
