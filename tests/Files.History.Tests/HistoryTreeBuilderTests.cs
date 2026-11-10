// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using static Files.History.Tests.TestHelpers;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class HistoryTreeBuilderTests
	{
		private static readonly string[] UserBase = [@"C:\Users\me"];

		[TestMethod]
		public void T1_FoldersUnderTheUserFolderBecomeParents()
		{
			var tree = HistoryTreeBuilder.Build(
				[
					Sel(@"C:\Users\me\Documents\水道\2026\予算\当初", 3),
					Sel(@"C:\Users\me\Documents\水道\2026\決算", 2),
					Sel(@"C:\Users\me\Documents\財務\資料", 1)
				],
				UserBase);

			// The user folder is not shown; Documents is the parent. Folders that do not branch are skipped.
			Assert.AreEqual("Documents\n  2026\n    当初\n    決算\n  資料", Render(tree));
		}

		[TestMethod]
		public void T2_DesktopAndDocumentsAreSeparateParents()
		{
			var tree = HistoryTreeBuilder.Build(
				[
					Sel(@"C:\Users\me\Desktop\作業", 3),
					Sel(@"C:\Users\me\Documents\水道\2026\決算", 2),
					Sel(@"C:\Users\me\Documents\財務\資料", 1)
				],
				UserBase);

			Assert.AreEqual("Desktop\n  作業\nDocuments\n  決算\n  資料", Render(tree));
		}

		[TestMethod]
		public void T3_ASingleFolderStillHasItsDriveLevelParent()
		{
			var tree = HistoryTreeBuilder.Build([Sel(@"D:\共有\台帳\2026", 1)]);

			Assert.AreEqual("共有\n  2026", Render(tree));
			Assert.AreEqual(@"D:\共有", tree[0].FullPath);
			Assert.AreEqual(@"D:\共有\台帳\2026", tree[0].Children.Single().FullPath);
		}

		[TestMethod]
		public void T12_DrivesAreGroupedAndSortedByLastUsed()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\A\x\1", 1),
				Sel(@"C:\A\y\2", 2),
				Sel(@"D:\Z\only", 10)]);

			Assert.AreEqual("Z\n  only\nA\n  1\n  2", Render(tree));
		}

		[TestMethod]
		public void FoldersOutsideTheUserFolderUseTheirDriveRoot()
		{
			var tree = HistoryTreeBuilder.Build(
				[
					Sel(@"C:\Users\me\Downloads\a", 1),
					Sel(@"C:\Users\me\files", 2),
					Sel(@"C:\Users\me\OneDrive\x\y", 3),
					Sel(@"D:\testprogram\gas", 4)
				],
				UserBase);

			Assert.AreEqual("testprogram\n  gas\nOneDrive\n  y\nfiles\nDownloads\n  a", Render(tree));
		}

		[TestMethod]
		public void SiblingsShareTheirDriveLevelParent()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"D:\testprogram\gas", 2),
				Sel(@"D:\testprogram\レコードから町を類推するマクロ\out", 1),
				Sel(@"D:\testprogram\レコードから町を類推するマクロ\src", 3)]);

			Assert.AreEqual("testprogram\n  gas\n  レコードから町を類推するマクロ\n    out\n    src", Render(tree));
			Assert.AreEqual(@"D:\testprogram", tree.Single().FullPath);
		}

		[TestMethod]
		public void FoldersBelowTheParentSkipNonBranchingLevels()
		{
			var tree = HistoryTreeBuilder.Build([
				Sel(@"C:\r\P\a\1", 1),
				Sel(@"C:\r\P\b\2", 2),
				Sel(@"C:\r\Q\3", 3)]);

			Assert.AreEqual("r\n  3\n  P\n    1\n    2", Render(tree));
		}

		[TestMethod]
		public void ParentThatIsASelectedFolder_IsOneRow()
		{
			var tree = HistoryTreeBuilder.Build(
				[
					Sel(@"C:\r\P", 1),
					Sel(@"C:\r\Q\3", 2)
				],
				[@"C:\r"]);

			var p = tree.Single(n => n.Name == "P");
			Assert.IsTrue(p.IsLeaf);
			Assert.AreEqual(0, p.Children.Count);
		}

		[TestMethod]
		public void TheBaseFolderItselfIsShownAsARow()
		{
			var tree = HistoryTreeBuilder.Build([Sel(@"C:\Users\me", 1), Sel(@"C:\Users\me\Documents\a", 2)], UserBase);

			Assert.AreEqual("Documents\n  a\nme", Render(tree));
			Assert.AreEqual(@"C:\Users\me", tree.Single(n => n.Name == "me").FullPath);
		}

		[TestMethod]
		public void FullPath_IsTheRealFolderPath()
		{
			var tree = HistoryTreeBuilder.Build(
				[
					Sel(@"C:\Users\me\Documents\水道\2026\予算\当初", 3),
					Sel(@"C:\Users\me\Documents\水道\2026\決算", 2)
				],
				[@"C:\Users\me\Documents"]);

			var parent = tree.Single();
			Assert.AreEqual(@"C:\Users\me\Documents\水道", parent.FullPath);

			var year = parent.Children.Single();
			Assert.AreEqual(@"C:\Users\me\Documents\水道\2026", year.FullPath);
			Assert.AreEqual(@"C:\Users\me\Documents\水道\2026\予算\当初", year.Children.Single(n => n.Name == "当初").FullPath);
		}
	}
}
