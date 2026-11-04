// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Globalization;

namespace Files.History
{
	/// <summary>
	/// Parses Office "File MRU" registry values such as <c>[F00000000][T01DA3C5A12345678][O00000000]*C:\dir\file.docx</c>.
	/// </summary>
	public static class OfficeMruParser
	{
		public static ExternalEntry? Parse(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var star = value.IndexOf('*');
			if (star < 0)
				return null;

			var path = value[(star + 1)..].Trim();
			if (path.Length == 0)
				return null;

			var tag = value.IndexOf("[T", StringComparison.Ordinal);
			if (tag < 0 || tag > star)
				return null;

			var end = value.IndexOf(']', tag);
			if (end < 0 || end > star)
				return null;

			var hex = value[(tag + 2)..end];
			if (!long.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var fileTime) || fileTime <= 0)
				return null;

			try
			{
				return new ExternalEntry(new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime)), path);
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}
	}
}
