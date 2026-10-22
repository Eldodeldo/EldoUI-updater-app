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

		public static Settings Load(string path)
		{
			var s = new Settings();
			try
			{
				if (!File.Exists(path)) return s;
				var o = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
				s.AddOnsDir = o.Str("addOnsDir") ?? "";
				s.AutoInstall = o.Bool("autoInstall", true);
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
				{ "selfUpdate", SelfUpdate },
				{ "language", Language ?? "auto" },
				{ "installedFolders", InstalledFolders ?? new List<string>() },
				{ "announcedVersion", AnnouncedVersion ?? "" },
				{ "lastCheck", Time(LastCheck) },
				{ "welcomed", Welcomed },
				{ "introduced", Introduced },
				{ "updaterSeenVersion", UpdaterSeenVersion ?? "" },
				{ "updaterSeenAt", Time(UpdaterSeenAt) },
			};
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string tmp = path + ".tmp";
			File.WriteAllText(tmp, Json.Write(o), new UTF8Encoding(false));
			if (File.Exists(path)) File.Replace(tmp, path, null);
			else File.Move(tmp, path);
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
