// Saves and reads the player's addon choices as a small file, to move them to another PC or share them.
// The file holds catalog names and choices only: no folders, paths or anything about the player.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public sealed class AddonChoice
	{
		public string Id;
		public bool PreRelease, Held;
	}

	public static class AddonList
	{
		public const string Format = "eldoui-addons";
		public const int MaxBytes = 64 * 1024;

		public static string Export(Settings settings)
		{
			var addons = (settings.ExtraAddons ? settings.SelectedCompanions : new List<string>())
				.Where(Settings.Selectable)
				.Select(id => (object)new Dictionary<string, object>
				{
					{ "id", id },
					{ "channel", settings.PreRelease.Contains(id) ? "prerelease" : "stable" },
					{ "hold", settings.Held.Contains(id) },
				}).ToList();
			return Json.Write(new Dictionary<string, object> { { "format", Format }, { "version", 1 }, { "addons", addons } });
		}

		/// <summary>Reads an exported list. Names the catalog does not know go to unknown. Throws FormatException for any other file.</summary>
		public static List<AddonChoice> Import(string text, out List<string> unknown)
		{
			unknown = new List<string>();
			if (text == null || text.Length > MaxBytes) throw new FormatException("This is not an EldoUI addon list.");
			var o = Json.ParseObject(text);
			if (o.Str("format") != Format || o.Num("version") != 1) throw new FormatException("This is not an EldoUI addon list.");
			var choices = new List<AddonChoice>();
			foreach (var item in o.Arr("addons") ?? new List<object>())
			{
				var a = item as Dictionary<string, object>;
				string id = a == null ? null : a.Str("id");
				if (id == null) continue;
				if (!Settings.Selectable(id)) { if (id.Length <= 64 && !unknown.Contains(id)) unknown.Add(id); continue; }
				if (choices.Any(c => c.Id == id)) continue;
				choices.Add(new AddonChoice { Id = id, PreRelease = a.Str("channel") == "prerelease", Held = a.Bool("hold") });
			}
			return choices;
		}

		/// <summary>
		/// Adds the choices to the player's selection and returns how many were new. Nothing is removed and nothing is
		/// installed until the player says so.
		/// </summary>
		public static int Apply(Settings settings, IEnumerable<AddonChoice> choices)
		{
			int added = 0;
			foreach (var c in choices)
			{
				if (!settings.ExtraAddons || !settings.SelectedCompanions.Contains(c.Id)) added++;
				settings.SelectCompanion(c.Id, true);
				if (c.PreRelease) settings.PreRelease.Add(c.Id); else settings.PreRelease.Remove(c.Id);
				if (c.Held) settings.Held.Add(c.Id); else settings.Held.Remove(c.Id);
			}
			return added;
		}
	}
}
