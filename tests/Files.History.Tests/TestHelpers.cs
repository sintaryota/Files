// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;

namespace Files.History.Tests
{
	internal static class TestHelpers
	{
		public static readonly DateTimeOffset Origin = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

		/// <summary>Creates an event <paramref name="minutes"/> after <see cref="Origin"/>.</summary>
		public static HistoryEvent Ev(string folder, int minutes)
			=> new(Origin.AddMinutes(minutes), folder + @"\file.txt", folder, HistorySource.Files);

		public static SelectedFolder Sel(string path, int minutes)
			=> new(path, Origin.AddMinutes(minutes));

		public static string Render(IEnumerable<HistoryNode> nodes)
		{
			var sb = new StringBuilder();
			Write(sb, nodes, 0);
			return sb.ToString().TrimEnd('\n');
		}

		private static void Write(StringBuilder sb, IEnumerable<HistoryNode> nodes, int depth)
		{
			foreach (var n in nodes)
			{
				sb.Append(' ', depth * 2).Append(n.Name).Append('\n');
				Write(sb, n.Children, depth + 1);
			}
		}
	}
}
