// Version numbers as EldoUI writes them: "0.7.0002", "v0.7.0002", "1.0.0".
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EldoUI.Updater.Core
{
	public static class Versions
	{
		/// <summary>Strips a leading "v" and spaces. Returns null for an empty or unusable value.</summary>
		public static string Clean(string version)
		{
			if (string.IsNullOrWhiteSpace(version)) return null;
			string v = version.Trim();
			if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
			return Parts(v) == null ? null : v;
		}

		/// <summary>Negative when a is older than b, 0 when equal, positive when newer. Unusable values count as oldest.</summary>
		public static int Compare(string a, string b)
		{
			var pa = Parts(Clean(a));
			var pb = Parts(Clean(b));
			if (pa == null && pb == null) return 0;
			if (pa == null) return -1;
			if (pb == null) return 1;
			int n = Math.Max(pa.Count, pb.Count);
			for (int i = 0; i < n; i++)
			{
				long x = i < pa.Count ? pa[i] : 0;
				long y = i < pb.Count ? pb[i] : 0;
				if (x != y) return x < y ? -1 : 1;
			}
			return 0;
		}

		public static bool IsNewer(string candidate, string current)
		{
			return Clean(candidate) != null && Compare(candidate, current) > 0;
		}

		static List<long> Parts(string v)
		{
			if (string.IsNullOrEmpty(v)) return null;
			// "1.2.3-beta" or "1.2.3+build": only the numeric core counts.
			int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
			if (cut >= 0) v = v.Substring(0, cut);
			var parts = new List<long>();
			foreach (string p in v.Split('.'))
			{
				long n;
				if (p.Length == 0 || p.Length > 12 || !long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null;
				parts.Add(n);
			}
			return parts.Count == 0 ? null : parts;
		}
	}
}
