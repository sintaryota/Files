// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace Files.History.Tests
{
	[TestClass]
	public sealed class ShellLinkParserTests
	{
		[TestMethod]
		public void ReadsAnsiLocalPath()
		{
			var link = ShellLinkParser.TryRead(Build(@"C:\docs\a.txt", unicode: false));

			Assert.IsNotNull(link);
			Assert.AreEqual(@"C:\docs\a.txt", link.Value.TargetPath);
			Assert.IsFalse(link.Value.IsFolder);
		}

		[TestMethod]
		public void PrefersTheUnicodePath()
		{
			var link = ShellLinkParser.TryRead(Build(@"D:\testprogram\地震対策マニュアル\逐条解説版\a.docx", unicode: true));

			Assert.AreEqual(@"D:\testprogram\地震対策マニュアル\逐条解説版\a.docx", link!.Value.TargetPath);
		}

		[TestMethod]
		public void JoinsTheCommonPathSuffix()
		{
			var link = ShellLinkParser.TryRead(Build(@"C:\docs", suffix: "a.txt", unicode: true));

			Assert.AreEqual(@"C:\docs\a.txt", link!.Value.TargetPath);
		}

		[TestMethod]
		public void ReportsFolderTargets()
		{
			var link = ShellLinkParser.TryRead(Build(@"C:\docs", attributes: 0x10, unicode: true));

			Assert.IsTrue(link!.Value.IsFolder);
		}

		[TestMethod]
		public void SkipsATargetIdList()
		{
			var link = ShellLinkParser.TryRead(Build(@"C:\docs\a.txt", unicode: true, idList: true));

			Assert.AreEqual(@"C:\docs\a.txt", link!.Value.TargetPath);
		}

		[TestMethod]
		public void ReturnsNullWithoutLinkInfo()
			=> Assert.IsNull(ShellLinkParser.TryRead(Build(@"C:\docs\a.txt", unicode: true, linkInfo: false)));

		[TestMethod]
		public void ReturnsNullForTruncatedOrUnrelatedData()
		{
			var bytes = Build(@"C:\docs\a.txt", unicode: true);

			Assert.IsNull(ShellLinkParser.TryRead(bytes.AsSpan(0, 100)));
			Assert.IsNull(ShellLinkParser.TryRead(new byte[200]));
			Assert.IsNull(ShellLinkParser.TryRead(ReadOnlySpan<byte>.Empty));
		}

		private static byte[] Build(string basePath, string suffix = "", uint attributes = 0x20, bool unicode = true, bool linkInfo = true, bool idList = false)
		{
			var result = new List<byte>();

			var header = new byte[76];
			BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4C);
			var flags = 0x80u | (linkInfo ? 0x2u : 0u) | (idList ? 0x1u : 0u);
			BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), flags);
			BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), attributes);
			result.AddRange(header);

			if (idList)
				result.AddRange(new byte[] { 4, 0, 1, 2, 3, 4 });

			if (linkInfo)
				result.AddRange(BuildLinkInfo(basePath, suffix, unicode));

			return result.ToArray();
		}

		private static byte[] BuildLinkInfo(string basePath, string suffix, bool unicode)
		{
			var headerSize = unicode ? 0x24 : 0x1C;
			var body = new List<byte>();
			int Here() => headerSize + body.Count;

			var localOffset = Here();
			body.AddRange(Encoding.ASCII.GetBytes(basePath));
			body.Add(0);

			var suffixOffset = Here();
			body.AddRange(Encoding.ASCII.GetBytes(suffix));
			body.Add(0);

			var localUnicode = 0;
			var suffixUnicode = 0;
			if (unicode)
			{
				localUnicode = Here();
				body.AddRange(Encoding.Unicode.GetBytes(basePath));
				body.AddRange(new byte[] { 0, 0 });

				suffixUnicode = Here();
				body.AddRange(Encoding.Unicode.GetBytes(suffix));
				body.AddRange(new byte[] { 0, 0 });
			}

			var info = new byte[headerSize + body.Count];
			BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(0), (uint)info.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), (uint)headerSize);
			BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), 1);
			BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(16), (uint)localOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(24), (uint)suffixOffset);
			if (unicode)
			{
				BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(28), (uint)localUnicode);
				BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(32), (uint)suffixUnicode);
			}

			body.CopyTo(info, headerSize);
			return info;
		}
	}
}
