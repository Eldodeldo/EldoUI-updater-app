// Every text the player sees, in English and Danish. Windows' display language picks one,
// unless the player chose a language in the menu.
using System.Collections.Generic;
using System.Globalization;

namespace EldoUI.Updater
{
	static class T
	{
		static Dictionary<string, string> _table;
		public static string Lang { get; private set; } = "en";

		public static void Use(string setting)
		{
			string lang = setting;
			if (lang != "en" && lang != "da")
				lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "da" ? "da" : "en";
			Lang = lang;
			_table = lang == "da" ? Da : En;
		}

		public static string S(string key)
		{
			string v;
			if ((_table ?? En).TryGetValue(key, out v)) return v;
			return En.TryGetValue(key, out v) ? v : key;
		}

		public static string F(string key, params object[] args)
		{
			return string.Format(CultureInfo.CurrentCulture, S(key), args);
		}

		static readonly Dictionary<string, string> En = new Dictionary<string, string>
		{
			{ "app", "EldoUI Updater" },
			{ "tagline", "Keeps EldoUI up to date for WoW: Forever" },
			{ "menu.open", "Open EldoUI Updater" },
			{ "menu.check", "Check for updates now" },
			{ "menu.folder", "Choose WoW folder…" },
			{ "menu.addons", "Open AddOns folder" },
			{ "menu.auto", "Install updates automatically" },
			{ "menu.startup", "Start with Windows" },
			{ "menu.language", "Language" },
			{ "menu.lang.auto", "Same as Windows" },
			{ "menu.site", "EldoUI website" },
			{ "menu.notes", "What's new in EldoUI" },
			{ "menu.exit", "Exit" },
			{ "label.installed", "Installed" },
			{ "label.latest", "Newest" },
			{ "label.folder", "WoW folder" },
			{ "label.checked", "Last check" },
			{ "value.none", "Not installed" },
			{ "value.unknown", "—" },
			{ "value.nofolder", "Not chosen yet" },
			{ "value.never", "Not yet" },
			{ "value.justnow", "Just now" },
			{ "value.minutes", "{0} min ago" },
			{ "value.hours", "{0} h ago" },
			{ "btn.check", "Check now" },
			{ "btn.update", "Update now" },
			{ "btn.install", "Install EldoUI" },
			{ "btn.folder", "Choose folder…" },
			{ "btn.addons", "Open AddOns" },
			{ "btn.close", "Close" },
			{ "state.checking", "Checking for a new EldoUI…" },
			{ "state.installing", "Installing EldoUI {0}…" },
			{ "state.uptodate", "EldoUI is up to date." },
			{ "state.available", "EldoUI {0} is ready to install." },
			{ "state.installed", "EldoUI {0} is installed. Type /reload in the game if it is open." },
			{ "state.nofolder", "Choose your WoW folder so the updater knows where EldoUI goes." },
			{ "state.offline", "No connection right now. The updater tries again later." },
			{ "state.failed", "The update did not finish. The updater tries again at the next check." },
			{ "state.ratelimited", "GitHub asks us to wait a little. The updater tries again later." },
			{ "tip.idle", "EldoUI Updater · EldoUI {0}" },
			{ "tip.idle.none", "EldoUI Updater" },
			{ "toast.installed.title", "EldoUI {0} is installed" },
			{ "toast.installed.body", "Type /reload in the game if it is open." },
			{ "toast.available.title", "EldoUI {0} is out" },
			{ "toast.available.body", "Click here to install it." },
			{ "toast.nofolder.title", "Where is WoW: Forever?" },
			{ "toast.nofolder.body", "Click here to choose your WoW folder." },
			{ "toast.self.title", "EldoUI Updater was updated" },
			{ "toast.self.body", "Now running version {0}." },
			{ "toast.failed.title", "EldoUI could not be updated" },
			{ "folder.prompt", "Choose your World of Warcraft folder (or its _classic_beta_ folder) for WoW: Forever." },
			{ "folder.bad.title", "That is not a WoW: Forever folder" },
			{ "folder.bad.body", "Choose the World of Warcraft folder that has a _classic_beta_ folder in it, or the _classic_beta_ folder itself." },
			{ "check.autostart", "Start with Windows" },
			{ "check.auto", "Install updates automatically" },
			{ "welcome.found", "Found WoW: Forever in {0}." },
			{ "uninstall.title", "Remove EldoUI Updater?" },
			{ "uninstall.body", "The updater and its settings are removed. EldoUI itself stays installed in the game." },
			{ "uninstall.done", "EldoUI Updater has been removed." },
			{ "error.title", "EldoUI Updater" },
			{ "error.install", "The updater could not install itself: {0}" },
			{ "footer", "Free and open source · Not made by Blizzard Entertainment" },
		};

		static readonly Dictionary<string, string> Da = new Dictionary<string, string>
		{
			{ "app", "EldoUI Updater" },
			{ "tagline", "Holder EldoUI opdateret til WoW: Forever" },
			{ "menu.open", "Åbn EldoUI Updater" },
			{ "menu.check", "Søg efter opdatering nu" },
			{ "menu.folder", "Vælg WoW-mappe…" },
			{ "menu.addons", "Åbn AddOns-mappen" },
			{ "menu.auto", "Installér opdateringer automatisk" },
			{ "menu.startup", "Start med Windows" },
			{ "menu.language", "Sprog" },
			{ "menu.lang.auto", "Som Windows" },
			{ "menu.site", "EldoUI's hjemmeside" },
			{ "menu.notes", "Nyt i EldoUI" },
			{ "menu.exit", "Afslut" },
			{ "label.installed", "Installeret" },
			{ "label.latest", "Nyeste" },
			{ "label.folder", "WoW-mappe" },
			{ "label.checked", "Sidst tjekket" },
			{ "value.none", "Ikke installeret" },
			{ "value.unknown", "—" },
			{ "value.nofolder", "Ikke valgt endnu" },
			{ "value.never", "Ikke endnu" },
			{ "value.justnow", "Lige nu" },
			{ "value.minutes", "For {0} min. siden" },
			{ "value.hours", "For {0} t. siden" },
			{ "btn.check", "Tjek nu" },
			{ "btn.update", "Opdatér nu" },
			{ "btn.install", "Installér EldoUI" },
			{ "btn.folder", "Vælg mappe…" },
			{ "btn.addons", "Åbn AddOns" },
			{ "btn.close", "Luk" },
			{ "state.checking", "Søger efter en ny EldoUI…" },
			{ "state.installing", "Installerer EldoUI {0}…" },
			{ "state.uptodate", "EldoUI er opdateret." },
			{ "state.available", "EldoUI {0} er klar til at blive installeret." },
			{ "state.installed", "EldoUI {0} er installeret. Skriv /reload i spillet, hvis det er åbent." },
			{ "state.nofolder", "Vælg din WoW-mappe, så updateren ved, hvor EldoUI skal ligge." },
			{ "state.offline", "Ingen forbindelse lige nu. Updateren prøver igen senere." },
			{ "state.failed", "Opdateringen blev ikke færdig. Updateren prøver igen ved næste tjek." },
			{ "state.ratelimited", "GitHub beder os vente lidt. Updateren prøver igen senere." },
			{ "tip.idle", "EldoUI Updater · EldoUI {0}" },
			{ "tip.idle.none", "EldoUI Updater" },
			{ "toast.installed.title", "EldoUI {0} er installeret" },
			{ "toast.installed.body", "Skriv /reload i spillet, hvis det er åbent." },
			{ "toast.available.title", "EldoUI {0} er ude" },
			{ "toast.available.body", "Klik her for at installere den." },
			{ "toast.nofolder.title", "Hvor ligger WoW: Forever?" },
			{ "toast.nofolder.body", "Klik her for at vælge din WoW-mappe." },
			{ "toast.self.title", "EldoUI Updater er opdateret" },
			{ "toast.self.body", "Kører nu version {0}." },
			{ "toast.failed.title", "EldoUI kunne ikke opdateres" },
			{ "folder.prompt", "Vælg din World of Warcraft-mappe (eller dens _classic_beta_-mappe) til WoW: Forever." },
			{ "folder.bad.title", "Det er ikke en WoW: Forever-mappe" },
			{ "folder.bad.body", "Vælg den World of Warcraft-mappe, der har en _classic_beta_-mappe, eller selve _classic_beta_-mappen." },
			{ "check.autostart", "Start med Windows" },
			{ "check.auto", "Installér opdateringer automatisk" },
			{ "welcome.found", "Fandt WoW: Forever i {0}." },
			{ "uninstall.title", "Fjern EldoUI Updater?" },
			{ "uninstall.body", "Updateren og dens indstillinger fjernes. Selve EldoUI bliver i spillet." },
			{ "uninstall.done", "EldoUI Updater er fjernet." },
			{ "error.title", "EldoUI Updater" },
			{ "error.install", "Updateren kunne ikke installere sig selv: {0}" },
			{ "footer", "Gratis og open source · Ikke lavet af Blizzard Entertainment" },
		};
	}
}
