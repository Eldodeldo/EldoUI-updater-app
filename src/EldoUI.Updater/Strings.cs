// Every text the player sees, in English and Danish. The system's display language picks one,
// unless the player chose a language in the menu. The Linux app shares this file and adds its own words.
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

		/// <summary>Adds or replaces texts (the Linux app's own wording for Linux things).</summary>
		public static void Add(Dictionary<string, string> en, Dictionary<string, string> da)
		{
			foreach (var kv in en) En[kv.Key] = kv.Value;
			foreach (var kv in da) Da[kv.Key] = kv.Value;
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
			{ "menu.site", "EldoUI on GitHub" },
			{ "menu.notes", "What's new in EldoUI" },
			{ "menu.exit", "Exit" },
			{ "label.installed", "Installed" },
			{ "label.latest", "Newest" },
			{ "label.folder", "WoW folder" },
			{ "label.checked", "Last check" },
			{ "label.app", "This app" },
			{ "value.app.current", "{0} · up to date" },
			{ "value.app.new", "{0} is ready · updates in {1} h" },
			{ "value.app.soon", "{0} is ready · updates shortly" },
			{ "value.app.updating", "Updating to {0}…" },
			{ "menu.self", "Update the app to {0}" },
			{ "menu.appcheck", "Check for an app update" },
			{ "intro.title", "EldoUI Updater is set up" },
			{ "intro.body", "It starts with Windows and installs new versions of EldoUI and of itself by itself. You can turn off \"Start with Windows\" and \"Install updates automatically\" in this window or in the tray menu at any time. It only talks to GitHub and sends nothing about you." },
			{ "app.newer.title", "EldoUI Updater {0} is out" },
			{ "app.newer.body", "It installs now and restarts by itself." },
			{ "app.uptodate.title", "EldoUI Updater is up to date" },
			{ "app.uptodate.body", "You have the newest version, {0}." },
			{ "app.unreachable.title", "Could not check for an app update" },
			{ "app.unreachable.body", "The updater could not reach its update page on GitHub. Check your connection and try again later." },
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
			{ "state.nodownload", "No EldoUI download is published yet. The updater checks again later." },
			{ "state.failed", "The update did not finish. The updater tries again at the next check." },
			{ "state.unverified", "EldoUI {0} could not be verified, so nothing was changed. The updater tries again at the next check." },
			{ "state.ratelimited", "GitHub asks us to wait a little. The updater tries again later." },
			{ "tip.idle", "EldoUI Updater · EldoUI {0}" },
			{ "tip.idle.none", "EldoUI Updater" },
			{ "toast.installed.title", "EldoUI {0} is installed" },
			{ "toast.installed.body", "Type /reload in the game if it is open." },
			{ "toast.available.title", "EldoUI {0} is out" },
			{ "toast.available.body", "Click here to install it." },
			{ "toast.nofolder.title", "Where is WoW: Forever?" },
			{ "toast.nofolder.body", "Click here to choose your WoW folder." },
			{ "toast.failed.title", "EldoUI could not be updated" },
			{ "folder.prompt", "Choose the World of Warcraft folder WoW: Forever is installed in (it has a _forever_ folder, or _classic_beta_ for the beta)." },
			{ "folder.bad.title", "That is not a WoW: Forever folder" },
			{ "folder.bad.body", "Choose the World of Warcraft folder that has a _forever_ folder in it (or _classic_beta_ for the beta), or that folder itself." },
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
			{ "menu.site", "EldoUI på GitHub" },
			{ "menu.notes", "Nyt i EldoUI" },
			{ "menu.exit", "Afslut" },
			{ "label.installed", "Installeret" },
			{ "label.latest", "Nyeste" },
			{ "label.folder", "WoW-mappe" },
			{ "label.checked", "Sidst tjekket" },
			{ "label.app", "Appen" },
			{ "value.app.current", "{0} · nyeste version" },
			{ "value.app.new", "{0} er klar · opdaterer om {1} t" },
			{ "value.app.soon", "{0} er klar · opdaterer om lidt" },
			{ "value.app.updating", "Opdaterer til {0}…" },
			{ "menu.self", "Opdatér appen til {0}" },
			{ "menu.appcheck", "Søg efter app-opdatering" },
			{ "intro.title", "EldoUI Updater er sat op" },
			{ "intro.body", "Den starter med Windows og installerer selv nye versioner af EldoUI og af sig selv. Du kan altid slå \"Start med Windows\" og \"Installér opdateringer automatisk\" fra i dette vindue eller i menuen ved uret. Den taler kun med GitHub og sender intet om dig." },
			{ "app.newer.title", "EldoUI Updater {0} er ude" },
			{ "app.newer.body", "Den installeres nu og genstarter af sig selv." },
			{ "app.uptodate.title", "EldoUI Updater er opdateret" },
			{ "app.uptodate.body", "Du har den nyeste version, {0}." },
			{ "app.unreachable.title", "Kunne ikke søge efter app-opdatering" },
			{ "app.unreachable.body", "Updateren kunne ikke nå sin opdateringsside på GitHub. Tjek din forbindelse, og prøv igen senere." },
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
			{ "state.nodownload", "Der er ikke udgivet en EldoUI-download endnu. Updateren tjekker igen senere." },
			{ "state.failed", "Opdateringen blev ikke færdig. Updateren prøver igen ved næste tjek." },
			{ "state.unverified", "EldoUI {0} kunne ikke bekræftes, så intet blev ændret. Updateren prøver igen ved næste tjek." },
			{ "state.ratelimited", "GitHub beder os vente lidt. Updateren prøver igen senere." },
			{ "tip.idle", "EldoUI Updater · EldoUI {0}" },
			{ "tip.idle.none", "EldoUI Updater" },
			{ "toast.installed.title", "EldoUI {0} er installeret" },
			{ "toast.installed.body", "Skriv /reload i spillet, hvis det er åbent." },
			{ "toast.available.title", "EldoUI {0} er ude" },
			{ "toast.available.body", "Klik her for at installere den." },
			{ "toast.nofolder.title", "Hvor ligger WoW: Forever?" },
			{ "toast.nofolder.body", "Klik her for at vælge din WoW-mappe." },
			{ "toast.failed.title", "EldoUI kunne ikke opdateres" },
			{ "folder.prompt", "Vælg den World of Warcraft-mappe, WoW: Forever er installeret i (den har en _forever_-mappe, eller _classic_beta_ til betaen)." },
			{ "folder.bad.title", "Det er ikke en WoW: Forever-mappe" },
			{ "folder.bad.body", "Vælg den World of Warcraft-mappe, der har en _forever_-mappe (eller _classic_beta_ til betaen), eller selve den mappe." },
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
