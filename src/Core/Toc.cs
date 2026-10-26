// Reads an addon's .toc file: its title, version, the game versions it is made for and what it needs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EldoUI.Updater.Core
{
	public sealed class TocInfo
	{
		public string Title, Version, Notes;
		public List<int> Interfaces = new List<int>();
		/// <summary>Addons that must be installed for this one to load.</summary>
		public List<string> Dependencies = new List<string>();
		public List<string> OptionalDependencies = new List<string>();
		/// <summary>The file is named for Forever (Name_Forever.toc and the like).</summary>
		public bool ForeverFile;

		/// <summary>Made for WoW: Forever: a Forever .toc file, or an interface number of the 1.60 game (16000-16999).</summary>
		public bool IsForever { get { return ForeverFile || Interfaces.Any(Toc.IsForeverInterface); } }
	}

	public static class Toc
	{
		static readonly string[] ForeverSuffixes = { "_Forever", "-Forever", "_Camelot", "-Camelot" };
		static readonly Regex Codes = new Regex(@"\|c[0-9A-Fa-f]{8}|\|r|\|T[^|]*\|t|\|A[^|]*\|a", RegexOptions.CultureInvariant);

		public static bool IsForeverInterface(int number) { return number >= 16000 && number <= 16999; }

		/// <summary>True for a .toc file name that marks a Forever build (Name_Forever.toc, Name-Camelot.toc).</summary>
		public static bool IsForeverFile(string fileName)
		{
			if (fileName == null || !fileName.EndsWith(".toc", StringComparison.OrdinalIgnoreCase)) return false;
			string stem = fileName.Substring(0, fileName.Length - 4);
			return ForeverSuffixes.Any(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase));
		}

		public static TocInfo Parse(string text, string fileName)
		{
			var info = new TocInfo { ForeverFile = IsForeverFile(fileName) };
			foreach (string raw in (text ?? "").TrimStart('﻿').Split('\n'))
			{
				string line = raw.Trim();
				if (!line.StartsWith("##")) continue;
				int colon = line.IndexOf(':');
				if (colon < 0) continue;
				string key = line.Substring(2, colon - 2).Trim().ToLowerInvariant();
				string value = line.Substring(colon + 1).Trim();
				if (value.Length == 0) continue;
				if (key == "title" && info.Title == null) info.Title = CleanTitle(value);
				else if (key == "version" && info.Version == null) info.Version = value.Length > 64 ? value.Substring(0, 64) : value;
				else if (key == "notes" && info.Notes == null) info.Notes = CleanTitle(value);
				else if (key == "interface")
				{
					foreach (string part in value.Split(','))
					{
						int n;
						if (int.TryParse(part.Trim(), out n) && !info.Interfaces.Contains(n)) info.Interfaces.Add(n);
					}
				}
				else if (key == "optionaldeps") info.OptionalDependencies.AddRange(Names(value));
				// The game reads every tag that starts with "Dep" (Dependencies, Dep, Deps) as required, and RequiredDeps.
				else if (key.StartsWith("dep") || key == "requireddeps") info.Dependencies.AddRange(Names(value));
			}
			info.Dependencies = info.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return info;
		}

		static IEnumerable<string> Names(string value)
		{
			return value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && s.Length <= 100);
		}

		/// <summary>The title without the game's color and texture codes.</summary>
		public static string CleanTitle(string value)
		{
			string s = Codes.Replace(value ?? "", "");
			return string.Join(" ", s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
		}

		/// <summary>
		/// The .toc the game reads for an installed addon folder: the Forever file first, then Name.toc, then any .toc.
		/// Null when the folder has none.
		/// </summary>
		public static TocInfo ReadFolder(string folder)
		{
			if (!Directory.Exists(folder)) return null;
			string name = Path.GetFileName(folder.TrimEnd('/', '\\'));
			string[] files;
			try { files = Directory.GetFiles(folder, "*.toc"); }
			catch (IOException) { return null; }
			catch (UnauthorizedAccessException) { return null; }
			string pick = files.FirstOrDefault(f => IsForeverFile(Path.GetFileName(f)) && Path.GetFileName(f).StartsWith(name, StringComparison.OrdinalIgnoreCase))
				?? files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name + ".toc", StringComparison.OrdinalIgnoreCase))
				?? files.OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
			if (pick == null) return null;
			try
			{
				var info = new FileInfo(pick);
				if (info.Length > 256 * 1024) return null;
				return Parse(File.ReadAllText(pick, Encoding.UTF8), Path.GetFileName(pick));
			}
			catch (IOException) { return null; }
			catch (UnauthorizedAccessException) { return null; }
		}
	}

	public static class Dependencies
	{
		/// <summary>
		/// A loop of required addons reachable from the given folders (A needs B, B needs A), as the folder names in
		/// order, or null when there is none. Names compare without case, as the game does.
		/// </summary>
		public static List<string> FindCycle(IDictionary<string, List<string>> needs, IEnumerable<string> from)
		{
			var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var kv in needs) graph[kv.Key] = kv.Value ?? new List<string>();
			var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var path = new List<string>();
			foreach (string start in from)
			{
				var found = Visit(start, graph, state, path);
				if (found != null) return found;
			}
			return null;
		}

		static List<string> Visit(string node, Dictionary<string, List<string>> graph, Dictionary<string, int> state, List<string> path)
		{
			int s;
			state.TryGetValue(node, out s);
			if (s == 2) return null;
			if (s == 1)
			{
				int at = path.FindIndex(p => string.Equals(p, node, StringComparison.OrdinalIgnoreCase));
				var loop = path.Skip(at).ToList();
				loop.Add(node);
				return loop;
			}
			state[node] = 1;
			path.Add(node);
			List<string> next;
			if (graph.TryGetValue(node, out next))
				foreach (string n in next)
				{
					var found = Visit(n, graph, state, path);
					if (found != null) return found;
				}
			path.RemoveAt(path.Count - 1);
			state[node] = 2;
			return null;
		}
	}
}
