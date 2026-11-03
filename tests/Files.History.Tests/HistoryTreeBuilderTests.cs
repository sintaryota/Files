// Copyright (c) Files Community
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class HistoryTreeBuilderTests
	{
		[TestMethod]
		public void T1_BranchUnderSameFolder()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\Users\me\Documents\水道\2026\予算\当初", 3),
				Sel(@"C:\Users\me\Documents\水道\2026\決算", 2),
				Sel(@"C:\Users\me\Documents\財務\資料", 1)]);

			Assert.AreEqual("水道\n  2026\n    当初\n    決算\n財務\n  資料", Render(tree));
		}

		[TestMethod]
		public void T2_DesktopAndDocumentsMixed()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\Users\me\Desktop\作業", 3),
				Sel(@"C:\Users\me\Documents\水道\2026\決算", 2),
				Sel(@"C:\Users\me\Documents\財務\資料", 1)]);

			Assert.AreEqual("Desktop\n  作業\nDocuments\n  決算\n  資料", Render(tree));
		}

		[TestMethod]
		public void T3_SingleLeafOnly()
		{
			var tree = HistoryTreeBuilder.Build([Sel(@"D:\共有\台帳\2026", 1)]);

			Assert.AreEqual("2026", Render(tree));
			Assert.AreEqual(@"D:\共有\台帳\2026", tree[0].FullPath);
			Assert.IsTrue(tree[0].IsLeaf);
		}

		[TestMethod]
		public void T12_DrivesAreGroupedAndSortedByLastUsed()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\A\x\1", 1),
				Sel(@"C:\A\y\2", 2),
				Sel(@"D:\Z\only", 10)]);

			// D: has no branching, so it is a lone leaf; top level is ordered by last use (D: 10, y 2, x 1).
			Assert.AreEqual("only\ny\n  2\nx\n  1", Render(tree));
		}

		[TestMethod]
		public void ParentIsBranchPoint_AppearsOnce()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\r\P\a\1", 1),
				Sel(@"C:\r\P\b\2", 2),
				Sel(@"C:\r\Q\3", 3)]);

			Assert.AreEqual("Q\n  3\nP\n  1\n  2", Render(tree));
		}

		[TestMethod]
		public void ParentThatIsLeaf_IsShownAsSingleRow()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\r\P", 1),
				Sel(@"C:\r\Q\3", 2)]);

			var p = tree.Single(n => n.Name == "P");
			Assert.IsTrue(p.IsLeaf);
			Assert.AreEqual(0, p.Children.Count);
		}

		[TestMethod]
		public void FullPath_IsTheRealFolderPath()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\Users\me\Documents\水道\2026\予算\当初", 3),
				Sel(@"C:\Users\me\Documents\水道\2026\決算", 2)]);

			// B = 2026; its children 予算 and 決算 become the parents.
			var yosan = tree.Single(n => n.Name == "予算");
			Assert.AreEqual(@"C:\Users\me\Documents\水道\2026\予算", yosan.FullPath);
			Assert.AreEqual(@"C:\Users\me\Documents\水道\2026\予算\当初", yosan.Children.Single().FullPath);
		}
	}
}
