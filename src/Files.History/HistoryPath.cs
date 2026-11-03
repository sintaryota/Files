// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// Windows path rules implemented on strings so they behave the same on any host OS.
	/// </summary>
	public static class HistoryPath
	{
		public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

		/// <summary>
		/// Returns the full path without a trailing separator (drive and share roots keep theirs), with "." and ".." resolved.
		/// Comparison is case-insensitive; the original casing is preserved.
		/// </summary>
		public static string Normalize(string path)
		{
			var (root, segments) = Split(path);
			return Join(root, segments);
		}

		/// <summary>
		/// Splits a path into its root (<c>C:\</c>, <c>\\server\share</c>, or empty) and its resolved segments.
		/// </summary>
		public static (string Root, string[] Segments) Split(string path)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(path);

			var s = path.Trim().Replace('/', '\\');
			string root;
			string rest;

			if (s.StartsWith(@"\\", StringComparison.Ordinal))
			{
				var parts = s.Split('\\', StringSplitOptions.RemoveEmptyEntries);
				var take = Math.Min(2, parts.Length);
				root = @"\\" + string.Join('\\', parts.Take(take));
				rest = string.Join('\\', parts.Skip(take));
			}
			else if (s.Length >= 2 && s[1] == ':')
			{
				root = s[..2] + '\\';
				rest = s[2..];
			}
			else
			{
				root = string.Empty;
				rest = s;
			}

			var stack = new List<string>();
			foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
			{
				if (part == ".")
					continue;
				if (part == "..")
				{
					if (stack.Count > 0)
						stack.RemoveAt(stack.Count - 1);
					continue;
				}
				stack.Add(part);
			}

			return (root, stack.ToArray());
		}

		public static string Join(string root, IReadOnlyList<string> segments)
		{
			if (segments.Count == 0)
				return root;

			var body = string.Join('\\', segments);
			if (root.Length == 0)
				return body;

			return root.EndsWith('\\') ? root + body : root + '\\' + body;
		}

		/// <summary>
		/// True when <paramref name="descendant"/> lies strictly inside <paramref name="ancestor"/>. Both must be normalized.
		/// </summary>
		public static bool IsAncestor(string ancestor, string descendant)
		{
			if (descendant.Length <= ancestor.Length ||
				!descendant.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
				return false;

			return ancestor.EndsWith('\\') || descendant[ancestor.Length] == '\\';
		}
	}
}
