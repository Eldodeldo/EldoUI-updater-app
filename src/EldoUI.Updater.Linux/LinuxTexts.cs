// The Linux app's own words where Linux differs from Windows: autostart at login, the game in a Wine
// prefix, and notices that cannot be clicked. Everything else comes from the shared Strings.cs.
using System.Collections.Generic;

namespace EldoUI.Updater
{
	static class LinuxTexts
	{
		public static void Apply()
		{
			T.Add(new Dictionary<string, string>
			{
				{ "menu.startup", "Start when I log in" },
				{ "menu.lang.auto", "Same as the system" },
				{ "check.autostart", "Start when I log in" },
				{ "toast.available.body", "Open EldoUI Updater to install it." },
				{ "toast.nofolder.body", "Open EldoUI Updater and choose your WoW folder." },
				{ "folder.prompt", "Choose the World of Warcraft folder of WoW: Forever, or the Wine prefix it runs in (Lutris, Bottles, Steam or Wine)" },
				{ "folder.bad.title", "That is not a WoW: Forever folder" },
				{ "folder.bad.body", "Choose the World of Warcraft folder that has a _forever_ folder in it (or _classic_beta_ for the beta), or the Wine prefix WoW runs in: the folder with drive_c in it." },
				{ "uninstall.body", "The updater, its settings and its app menu entry are removed. EldoUI itself stays installed in the game." },
				{ "linux.notray", "The tray icon needs a panel with StatusNotifier support (on GNOME: the AppIndicator extension). The updater keeps working without it; open it from the app menu." },
			}, new Dictionary<string, string>
			{
				{ "menu.startup", "Start når jeg logger ind" },
				{ "menu.lang.auto", "Samme som systemet" },
				{ "check.autostart", "Start når jeg logger ind" },
				{ "toast.available.body", "Åbn EldoUI Updater for at installere den." },
				{ "toast.nofolder.body", "Åbn EldoUI Updater, og vælg din WoW-mappe." },
				{ "folder.prompt", "Vælg World of Warcraft-mappen til WoW: Forever, eller den Wine-mappe spillet kører i (Lutris, Bottles, Steam eller Wine)" },
				{ "folder.bad.title", "Det er ikke en WoW: Forever-mappe" },
				{ "folder.bad.body", "Vælg den World of Warcraft-mappe, der har en _forever_-mappe (eller _classic_beta_ til betaen), eller den Wine-mappe WoW kører i: mappen med drive_c i." },
				{ "uninstall.body", "Updateren, dens indstillinger og dens genvej i programmenuen fjernes. Selve EldoUI bliver i spillet." },
				{ "linux.notray", "Bakke-ikonet kræver et panel med StatusNotifier (på GNOME: udvidelsen AppIndicator). Updateren virker også uden; åbn den fra programmenuen." },
			});
		}

		/// <summary>The language for "auto": LANGUAGE, LC_ALL, LC_MESSAGES or LANG, as Linux programs read it.</summary>
		public static string SystemLanguage()
		{
			foreach (string v in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
			{
				string s = System.Environment.GetEnvironmentVariable(v);
				if (string.IsNullOrEmpty(s)) continue;
				return s.StartsWith("da", System.StringComparison.OrdinalIgnoreCase) ? "da" : "en";
			}
			return "en";
		}

		/// <summary>Sets the language: the player's choice, or the system's.</summary>
		public static void Use(string setting)
		{
			T.Use(setting == "en" || setting == "da" ? setting : SystemLanguage());
		}
	}
}
