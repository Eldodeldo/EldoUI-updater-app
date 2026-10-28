// The updater's own settings, kept as a small JSON file in its data folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EldoUI.Updater.Core
{
	public sealed class Settings
	{
		/// <summary>The AddOns folder EldoUI goes into. Empty until found or picked.</summary>
		public string AddOnsDir = "";
		/// <summary>Install a new EldoUI by itself. Off: only tell the player.</summary>
		public bool AutoInstall = true;
		public bool ExtraAddons;
		public List<string> SelectedCompanions = new List<string> { "dbm", "rxp", "media" };
		public Dictionary<string, string> ExtraVersions = new Dictionary<string, string>();
		public Dictionary<string, List<string>> ExtraFiles = new Dictionary<string, List<string>>();
		/// <summary>Addons kept at their installed version: the updater shows a newer one but does not install it.</summary>
		public HashSet<string> Held = new HashSet<string>();
		/// <summary>Addons that follow their publisher's test releases too. Stable releases only is the default.</summary>
		public HashSet<string> PreRelease = new HashSet<string>();
		/// <summary>Addons whose hand-installed folders the player agreed the updater takes over at the next install.</summary>
		public HashSet<string> Adopted = new HashSet<string>();
		/// <summary>Addons the player asked to install again at the next check, even though they are up to date. Not saved.</summary>
		public HashSet<string> Reinstall = new HashSet<string>();
		/// <summary>AddOns folders the player chose not to see in the installed list.</summary>
		public HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Keep the updater itself up to date.</summary>
		public bool SelfUpdate = true;
		/// <summary>"auto" follows Windows, or "en" / "da".</summary>
		public string Language = "auto";
		/// <summary>Folders the last install put into AddOns (to remove one a later release drops).</summary>
		public List<string> InstalledFolders = new List<string>();
		/// <summary>The newest version already announced, so a notice is never shown twice.</summary>
		public string AnnouncedVersion = "";
		public DateTime? LastCheck;
		public bool Welcomed;
		/// <summary>The one-time note on what runs by itself (start with the system, automatic updates) was shown.</summary>
		public bool Introduced;
		/// <summary>A newer version of the updater itself, and when it was first seen (found while the app runs, it installs 24 hours later; found at start, at once).</summary>
		public string UpdaterSeenVersion = "";
		public DateTime? UpdaterSeenAt;
		/// <summary>The Extra addons window's size and column widths, as the player left them.</summary>
		public WindowLayout AddonsWindow = new WindowLayout();

		public void SelectCompanion(string id, bool selected)
		{
			if (!Selectable(id)) throw new ArgumentException("Unknown companion addon.", nameof(id));
			if (!ExtraAddons) SelectedCompanions.Clear();
			SelectedCompanions.Remove(id);
			if (selected) SelectedCompanions.Add(id);
			else { Held.Remove(id); PreRelease.Remove(id); Adopted.Remove(id); Reinstall.Remove(id); }
			ExtraAddons = SelectedCompanions.Count > 0;
		}

		/// <summary>A catalog addon the updater can install or check: not one that is only a page to open.</summary>
		public static bool Selectable(string id)
		{
			var entry = AddonCatalog.Find(id);
			return entry != null && entry.Source != AddonSource.Page;
		}

		public static Settings Load(string path)
		{
			var s = new Settings();
			try
			{
				if (!File.Exists(path)) return s;
				var o = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
				s.AddOnsDir = o.Str("addOnsDir") ?? "";
				s.AutoInstall = o.Bool("autoInstall", true);
				s.ExtraAddons = o.Bool("extraAddons");
				if (o.Arr("selectedCompanions") != null) s.SelectedCompanions = o.Arr("selectedCompanions").OfType<string>().Where(Selectable).Distinct().ToList();
				foreach (var item in o.Obj("extraVersions") ?? new Dictionary<string, object>())
					if (item.Value is string) s.ExtraVersions[item.Key] = (string)item.Value;
				foreach (var item in o.Obj("extraFiles") ?? new Dictionary<string, object>())
					if (item.Value is List<object>) s.ExtraFiles[item.Key] = ((List<object>)item.Value).OfType<string>().ToList();
				s.Held = Ids(o, "held");
				s.PreRelease = Ids(o, "preRelease");
				s.Adopted = Ids(o, "adopted");
				s.IgnoredFolders = new HashSet<string>((o.Arr("ignoredFolders") ?? new List<object>()).OfType<string>(), StringComparer.OrdinalIgnoreCase);
				s.SelfUpdate = o.Bool("selfUpdate", true);
				s.Language = o.Str("language") ?? "auto";
				s.AnnouncedVersion = o.Str("announcedVersion") ?? "";
				s.Welcomed = o.Bool("welcomed");
				s.Introduced = o.Bool("introduced");
				var folders = o.Arr("installedFolders");
				if (folders != null) s.InstalledFolders = folders.OfType<string>().ToList();
				s.LastCheck = Time(o.Str("lastCheck"));
				s.UpdaterSeenVersion = o.Str("updaterSeenVersion") ?? "";
				s.UpdaterSeenAt = Time(o.Str("updaterSeenAt"));
				s.AddonsWindow = WindowLayout.FromJson(o.Obj("addonsWindow"));
			}
			catch
			{
				// A broken file starts fresh rather than stopping the app.
			}
			return s;
		}

		public void Save(string path)
		{
			var o = new Dictionary<string, object>
			{
				{ "addOnsDir", AddOnsDir ?? "" },
				{ "autoInstall", AutoInstall },
				{ "extraAddons", ExtraAddons },
				{ "selectedCompanions", SelectedCompanions },
				{ "extraVersions", ExtraVersions.ToDictionary(p => p.Key, p => (object)p.Value) },
				{ "extraFiles", ExtraFiles.ToDictionary(p => p.Key, p => (object)p.Value) },
				{ "held", Held.OrderBy(x => x, StringComparer.Ordinal).ToList() },
				{ "preRelease", PreRelease.OrderBy(x => x, StringComparer.Ordinal).ToList() },
				{ "adopted", Adopted.OrderBy(x => x, StringComparer.Ordinal).ToList() },
				{ "ignoredFolders", IgnoredFolders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() },
				{ "selfUpdate", SelfUpdate },
				{ "language", Language ?? "auto" },
				{ "installedFolders", InstalledFolders ?? new List<string>() },
				{ "announcedVersion", AnnouncedVersion ?? "" },
				{ "lastCheck", Time(LastCheck) },
				{ "welcomed", Welcomed },
				{ "introduced", Introduced },
				{ "updaterSeenVersion", UpdaterSeenVersion ?? "" },
				{ "updaterSeenAt", Time(UpdaterSeenAt) },
				{ "addonsWindow", (AddonsWindow ?? new WindowLayout()).ToJson() },
			};
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string tmp = path + ".tmp";
			File.WriteAllText(tmp, Json.Write(o), new UTF8Encoding(false));
			if (File.Exists(path)) File.Replace(tmp, path, null);
			else File.Move(tmp, path);
		}

		static HashSet<string> Ids(Dictionary<string, object> o, string key)
		{
			return new HashSet<string>((o.Arr(key) ?? new List<object>()).OfType<string>().Where(Selectable));
		}

		static DateTime? Time(string text)
		{
			DateTime t;
			if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t))
				return t;
			return null;
		}

		static string Time(DateTime? t)
		{
			return t.HasValue ? t.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) : null;
		}
	}
}
