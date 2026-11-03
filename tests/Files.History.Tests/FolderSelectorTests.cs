// Copyright (c) Files Community
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class FolderSelectorTests
	{
		private static bool Always(string _) => true;

		private static string[] Paths(IEnumerable<SelectedFolder> s) => s.Select(f => f.Path).ToArray();

		[TestMethod]
		public void T4_AncestorIsHiddenWhileDescendantIsInHistory()
		{
			var events = new List<HistoryEvent>
			{
				Ev(@"C:\A\1", 1), Ev(@"C:\A\1", 2), Ev(@"C:\A\1", 3),
				Ev(@"C:\A\1\ア", 4)
			};

			CollectionAssert.AreEqual(new[] { @"C:\A\1\ア" }, Paths(FolderSelector.Select(events, Always)));
		}

		[TestMethod]
		public void T5_AncestorReturnsWhenDescendantLeavesHistory()
		{
			// ア is the oldest of 101 events, so it falls out of the newest 100.
			var events = new List<HistoryEvent> { Ev(@"C:\A\1\ア", 0), Ev(@"C:\A\1", 1), Ev(@"C:\A\1", 2), Ev(@"C:\A\1", 3) };
			for (var i = 0; i < 97; i++)
				events.Add(Ev($@"C:\Z\{i}", 10 + i));

			Assert.AreEqual(101, events.Count);

			var result = Paths(FolderSelector.Select(events, Always));
			CollectionAssert.Contains(result, @"C:\A\1");
			CollectionAssert.DoesNotContain(result, @"C:\A\1\ア");
		}

		[TestMethod]
		public void T6_OverlapBetweenRecentAndFrequentIsRefilled()
		{
			var events = new List<HistoryEvent>();
			var t = 0;

			// F1..F12 used 13..2 times; the last use of F1 and F2 is placed at the very end.
			for (var i = 1; i <= 12; i++)
			{
				var count = 14 - i;
				var baseCount = i <= 2 ? count - 1 : count;
				for (var n = 0; n < baseCount; n++)
					events.Add(Ev($@"C:\F\{i}", t++));
			}

			foreach (var r in new[] { 3, 4, 5 })
				events.Add(Ev($@"C:\R\{r}", t++));

			events.Add(Ev(@"C:\F\2", t++));
			events.Add(Ev(@"C:\F\1", t++));

			Assert.IsLessThanOrEqualTo(HistoryDefaults.HistorySize, events.Count);

			var result = Paths(FolderSelector.Select(events, Always));

			Assert.HasCount(HistoryDefaults.MaxDisplay, result);
			CollectionAssert.Contains(result, @"C:\F\11");
			CollectionAssert.Contains(result, @"C:\F\12");
		}

		[TestMethod]
		public void T7_RecentAncestorIsDroppedForLaterDescendantAndRefilled()
		{
			var events = new List<HistoryEvent>();
			var t = 0;

			for (var n = 0; n < 5; n++)
				events.Add(Ev(@"C:\P\Q", t++));
			for (var i = 0; i < 20; i++)
				events.Add(Ev($@"C:\O\{i}", t++));
			events.Add(Ev(@"C:\P", t++));

			var result = Paths(FolderSelector.Select(events, Always));

			CollectionAssert.Contains(result, @"C:\P\Q");
			CollectionAssert.DoesNotContain(result, @"C:\P");
			Assert.HasCount(HistoryDefaults.MaxDisplay, result);
		}

		[TestMethod]
		public void T11_MissingFolderIsSkippedAndRefilled()
		{
			var events = Enumerable.Range(0, 30).Select(i => Ev($@"C:\D\{i}", i)).ToList();

			var result = Paths(FolderSelector.Select(events, p => !p.EndsWith(@"\29")));

			CollectionAssert.DoesNotContain(result, @"C:\D\29");
			Assert.HasCount(HistoryDefaults.MaxDisplay, result);
		}

		[TestMethod]
		public void FewerCandidatesThanMax_ReturnsAllOfThem()
		{
			var events = new[] { Ev(@"C:\a", 1), Ev(@"C:\b", 2) };

			Assert.HasCount(2, FolderSelector.Select(events, Always));
		}

		[TestMethod]
		public void PathsAreComparedCaseInsensitivelyAndTrailingSeparatorIgnored()
		{
			var events = new[] { Ev(@"C:\Docs\X", 1), Ev(@"c:\docs\x\", 2) };

			var result = FolderSelector.Select(events, Always);

			Assert.HasCount(1, result);
			Assert.AreEqual(Origin.AddMinutes(2), result[0].LastUsed);
		}

		[TestMethod]
		public void ExistsIsNotCalledForCandidatesPastTheLimit()
		{
			var events = Enumerable.Range(0, 60).Select(i => Ev($@"C:\D\{i}", i)).ToList();
			var calls = 0;

			FolderSelector.Select(events, _ => { calls++; return true; });

			Assert.AreEqual(HistoryDefaults.MaxDisplay, calls);
		}
	}
}
