// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Files.History
{
	/// <param name="TargetPath">The stored target path of the shortcut.</param>
	/// <param name="IsFolder">True when the target was a folder when the shortcut was written.</param>
	public readonly record struct ShellLinkInfo(string TargetPath, bool IsFolder);

	/// <summary>
	/// Reads the stored target of a Windows shortcut (.lnk) straight from its bytes (MS-SHLLINK).
	/// It never resolves or opens the target, so it cannot show UI or touch the network.
	/// </summary>
	public static class ShellLinkParser
	{
		private const int HeaderSize = 0x4C;
		private const uint HasLinkTargetIdList = 0x1;
		private const uint HasLinkInfo = 0x2;
		private const uint DirectoryAttribute = 0x10;
		private const uint VolumeIdAndLocalBasePath = 0x1;
		private const uint CommonNetworkRelativeLinkAndPathSuffix = 0x2;

		private static readonly Encoding AnsiEncoding = CreateAnsiEncoding();

		/// <returns>The target, or null when the data is not a shortcut with a stored path.</returns>
		public static ShellLinkInfo? TryRead(ReadOnlySpan<byte> data)
		{
			try
			{
				return Read(data);
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}

		private static ShellLinkInfo? Read(ReadOnlySpan<byte> data)
		{
			if (data.Length < HeaderSize || U32(data, 0) != HeaderSize)
				return null;

			var flags = U32(data, 20);
			var attributes = U32(data, 24);

			var offset = HeaderSize;
			if ((flags & HasLinkTargetIdList) != 0)
				offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

			if ((flags & HasLinkInfo) == 0)
				return null;

			var info = data[offset..];
			var size = (int)U32(info, 0);
			if (size < 0x1C || size > info.Length)
				return null;
			info = info[..size];

			var headerSize = U32(info, 4);
			var infoFlags = U32(info, 8);
			var hasUnicode = headerSize >= 0x24;

			string? basePath = null;
			if ((infoFlags & VolumeIdAndLocalBasePath) != 0)
			{
				basePath = hasUnicode && U32(info, 28) != 0
					? ReadUnicode(info, (int)U32(info, 28))
					: ReadAnsi(info, (int)U32(info, 16));
			}

			if (string.IsNullOrEmpty(basePath) && (infoFlags & CommonNetworkRelativeLinkAndPathSuffix) != 0)
			{
				var network = (int)U32(info, 20);
				var netNameOffset = U32(info, network + 8);
				basePath = netNameOffset > 0x14
					? ReadUnicode(info, network + (int)U32(info, network + 20))
					: ReadAnsi(info, network + (int)netNameOffset);
			}

			if (string.IsNullOrEmpty(basePath))
				return null;

			var suffix = hasUnicode && U32(info, 32) != 0
				? ReadUnicode(info, (int)U32(info, 32))
				: ReadAnsi(info, (int)U32(info, 24));

			var path = string.IsNullOrEmpty(suffix)
				? basePath
				: basePath.EndsWith('\\') ? basePath + suffix : basePath + '\\' + suffix;

			return new ShellLinkInfo(path, (attributes & DirectoryAttribute) != 0);
		}

		private static uint U32(ReadOnlySpan<byte> data, int offset)
			=> BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

		private static string ReadAnsi(ReadOnlySpan<byte> info, int offset)
		{
			if (offset <= 0 || offset >= info.Length)
				return string.Empty;

			var text = info[offset..];
			var end = text.IndexOf((byte)0);
			return AnsiEncoding.GetString(end < 0 ? text : text[..end]);
		}

		private static string ReadUnicode(ReadOnlySpan<byte> info, int offset)
		{
			if (offset <= 0 || offset >= info.Length)
				return string.Empty;

			var text = info[offset..];
			var length = 0;
			while (length + 1 < text.Length && !(text[length] == 0 && text[length + 1] == 0))
				length += 2;

			return Encoding.Unicode.GetString(text[..length]);
		}

		private static Encoding CreateAnsiEncoding()
		{
			try
			{
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
			}
			catch (ArgumentException)
			{
				return Encoding.Latin1;
			}
		}
	}
}
