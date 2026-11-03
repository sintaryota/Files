// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History.Tests
{
	[TestClass]
	public sealed class HistoryPathTests
	{
		[TestMethod]
		[DataRow(@"C:\a\b\", @"C:\a\b")]
		[DataRow(@"C:/a/b", @"C:\a\b")]
		[DataRow(@"C:\a\.\b\..\c", @"C:\a\c")]
		[DataRow(@"C:\", @"C:\")]
		[DataRow(@"C:\a\\b", @"C:\a\b")]
		[DataRow(@"\\srv\share\x\", @"\\srv\share\x")]
		[DataRow(@"\\srv\share", @"\\srv\share")]
		public void Normalize(string input, string expected)
			=> Assert.AreEqual(expected, HistoryPath.Normalize(input));

		[TestMethod]
		public void IsAncestor()
		{
			Assert.IsTrue(HistoryPath.IsAncestor(@"C:\A\1", @"C:\a\1\x"));
			Assert.IsTrue(HistoryPath.IsAncestor(@"C:\", @"C:\A"));
			Assert.IsFalse(HistoryPath.IsAncestor(@"C:\A\1", @"C:\A\10"));
			Assert.IsFalse(HistoryPath.IsAncestor(@"C:\A\1", @"C:\A\1"));
		}
	}
}
