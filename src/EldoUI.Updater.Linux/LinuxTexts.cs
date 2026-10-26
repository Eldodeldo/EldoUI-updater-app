// The Linux app's own words where Linux differs from Windows: autostart at login, the game in a Wine
// prefix, notices that cannot be clicked, and the Linux setup assistant. Everything else comes from Strings.cs.
using System.Collections.Generic;

namespace EldoUI.Updater
{
	static class LinuxTexts
	{
		public static void Apply()
		{
			T.Add(English(), Danish());
		}

		public static Dictionary<string, string> English()
		{
			return new Dictionary<string, string>
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
				{ "intro.body", "It starts when you log in and installs new versions of EldoUI and of itself by itself. You can turn off \"Start when I log in\" and \"Install updates automatically\" in its window or tray menu at any time. It talks to GitHub (and, for addons you add from them, WoWInterface or TukUI) and sends nothing about you." },
				{ "btn.cancel", "Cancel" },
				{ "menu.linuxsetup", "Linux setup…" },
				{ "lf.button", "Linux setup…" },
				{ "lf.hint", "Install Battle.net and WoW: Forever on Linux with Lightforge, a free program by its own authors." },
				{ "lf.title", "Linux setup assistant" },
				{ "lf.intro", "Lightforge is a free, separate program by its own authors that installs Battle.net and WoW: Forever on Linux. This assistant checks your system, downloads Lightforge {0} from its GitHub release, checks it against its published SHA-256 and puts it in your home folder. You then set up the game in Lightforge's own window. Nothing here asks for your password, changes your system or starts the game." },
				{ "lf.section.system", "Your system" },
				{ "lf.section.lightforge", "Lightforge" },
				{ "lf.section.services", "What Lightforge connects to" },
				{ "lf.check.distro", "Distribution" },
				{ "lf.check.arch", "Processor (x86_64)" },
				{ "lf.check.glibc", "glibc (2.36 or newer)" },
				{ "lf.check.fonts", "Fonts" },
				{ "lf.check.32bit", "32-bit support" },
				{ "lf.check.disk", "Free space (300 MB)" },
				{ "lf.found.yes", "found" },
				{ "lf.found.none", "not found" },
				{ "lf.fix.distro", "The distribution could not be read." },
				{ "lf.fix.arch", "Lightforge runs only on 64-bit x86 PCs." },
				{ "lf.fix.glibc.warn", "Older than the 2.36 Lightforge asks for. It may work, but its authors have not tested it." },
				{ "lf.fix.glibc.fail", "Too old for Lightforge. A newer release of your distribution is needed." },
				{ "lf.fix.fonts", "No fonts found. If text is missing in Battle.net, install your distribution's standard fonts with its package manager." },
				{ "lf.fix.32bit", "No 32-bit loader found. If Battle.net does not start, add your distribution's 32-bit libraries with its package manager." },
				{ "lf.fix.disk", "Not enough room in your home folder for Lightforge." },
				{ "lf.state.none", "Not installed. It goes to {0}, with an entry in your app menu." },
				{ "lf.state.ready", "Lightforge {0} is installed and ready." },
				{ "lf.state.outstanding", "Lightforge {0} is installed. Still to do in Lightforge: {1}." },
				{ "lf.state.forever", "WoW: Forever is installed at {0}." },
				{ "lf.state.noforever", "WoW: Forever is not installed yet. Install it in Lightforge." },
				{ "lf.problem.version", "Lightforge is there, but did not report its version." },
				{ "lf.problem.unknown", "Lightforge {0} is installed. This assistant knows version {1}, so it does not read this one further." },
				{ "lf.problem.status", "Lightforge did not report its state in the expected way." },
				{ "lf.problem.product", "Lightforge reported a game folder that is not WoW: Forever, so it is not used." },
				{ "lf.services", "Blizzard's servers (Battle.net and the game), its project's own servers at tuxcore.pro (artwork, its catalog and a download proxy), GitHub (Proton downloads), and CurseForge, WoWInterface and TukUI when you use its addon features. The updater sends nothing to them. Lightforge's page has the details." },
				{ "lf.license", "Lightforge {0} is licensed {1} and made by the Lightforge project, not by EldoUI. The assistant downloads it unchanged; its source is on its page." },
				{ "lf.btn.install", "Install Lightforge {0}" },
				{ "lf.btn.reinstall", "Install {0} again" },
				{ "lf.btn.open", "Open Lightforge" },
				{ "lf.btn.use", "Use its WoW: Forever" },
				{ "lf.btn.check", "Check again" },
				{ "lf.btn.page", "Lightforge's page" },
				{ "lf.btn.source", "Source code" },
				{ "lf.busy.read", "Reading your system and Lightforge…" },
				{ "lf.busy.download", "Downloading Lightforge {0} from GitHub…" },
				{ "lf.busy.unpack", "Checking and unpacking…" },
				{ "lf.busy.place", "Putting the files in place…" },
				{ "lf.done", "Lightforge {0} is installed. Open it to set up Battle.net and WoW: Forever." },
				{ "lf.failed", "Lightforge was not installed: {0} What was there before is unchanged." },
				{ "lf.used", "The updater now uses {0}." },
				{ "linux.notray", "The tray icon needs a panel with StatusNotifier support (on GNOME: the AppIndicator extension). The updater keeps working without it; open it from the app menu." },
			};
		}

		public static Dictionary<string, string> Danish()
		{
			return new Dictionary<string, string>
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
				{ "intro.body", "Den starter, når du logger ind, og installerer selv nye versioner af EldoUI og af sig selv. Du kan altid slå \"Start når jeg logger ind\" og \"Installér opdateringer automatisk\" fra i dens vindue eller menu. Den taler med GitHub (og med WoWInterface eller TukUI for addons, du tilføjer derfra) og sender intet om dig." },
				{ "btn.cancel", "Annullér" },
				{ "menu.linuxsetup", "Linux-opsætning…" },
				{ "lf.button", "Linux-opsætning…" },
				{ "lf.hint", "Installér Battle.net og WoW: Forever på Linux med Lightforge, et gratis program fra dets egne udviklere." },
				{ "lf.title", "Linux-opsætningsassistent" },
				{ "lf.intro", "Lightforge er et gratis, selvstændigt program fra dets egne udviklere, der installerer Battle.net og WoW: Forever på Linux. Assistenten tjekker dit system, henter Lightforge {0} fra dets GitHub-udgivelse, tjekker det mod dets offentliggjorte SHA-256 og lægger det i din hjemmemappe. Bagefter sætter du spillet op i Lightforges eget vindue. Intet her beder om din adgangskode, ændrer dit system eller starter spillet." },
				{ "lf.section.system", "Dit system" },
				{ "lf.section.lightforge", "Lightforge" },
				{ "lf.section.services", "Hvad Lightforge forbinder til" },
				{ "lf.check.distro", "Distribution" },
				{ "lf.check.arch", "Processor (x86_64)" },
				{ "lf.check.glibc", "glibc (2.36 eller nyere)" },
				{ "lf.check.fonts", "Skrifttyper" },
				{ "lf.check.32bit", "32-bit-understøttelse" },
				{ "lf.check.disk", "Ledig plads (300 MB)" },
				{ "lf.found.yes", "fundet" },
				{ "lf.found.none", "ikke fundet" },
				{ "lf.fix.distro", "Distributionen kunne ikke læses." },
				{ "lf.fix.arch", "Lightforge kører kun på 64-bit x86-pc'er." },
				{ "lf.fix.glibc.warn", "Ældre end de 2.36, Lightforge beder om. Det kan virke, men udviklerne har ikke testet det." },
				{ "lf.fix.glibc.fail", "For gammel til Lightforge. Der skal en nyere udgave af din distribution til." },
				{ "lf.fix.fonts", "Ingen skrifttyper fundet. Mangler der tekst i Battle.net, så installér din distributions standardskrifttyper med dens pakkehåndtering." },
				{ "lf.fix.32bit", "Ingen 32-bit-loader fundet. Starter Battle.net ikke, så tilføj din distributions 32-bit-biblioteker med dens pakkehåndtering." },
				{ "lf.fix.disk", "Der er ikke plads nok i din hjemmemappe til Lightforge." },
				{ "lf.state.none", "Ikke installeret. Det lægges i {0}, med en genvej i din programmenu." },
				{ "lf.state.ready", "Lightforge {0} er installeret og klar." },
				{ "lf.state.outstanding", "Lightforge {0} er installeret. Mangler stadig i Lightforge: {1}." },
				{ "lf.state.forever", "WoW: Forever er installeret i {0}." },
				{ "lf.state.noforever", "WoW: Forever er ikke installeret endnu. Installér det i Lightforge." },
				{ "lf.problem.version", "Lightforge er der, men oplyste ikke sin version." },
				{ "lf.problem.unknown", "Lightforge {0} er installeret. Assistenten kender version {1}, så den læser ikke denne videre." },
				{ "lf.problem.status", "Lightforge oplyste ikke sin tilstand på den forventede måde." },
				{ "lf.problem.product", "Lightforge oplyste en spilmappe, der ikke er WoW: Forever, så den bruges ikke." },
				{ "lf.services", "Blizzards servere (Battle.net og spillet), projektets egne servere på tuxcore.pro (grafik, dets katalog og en download-proxy), GitHub (Proton-downloads) og CurseForge, WoWInterface og TukUI, når du bruger dets addon-funktioner. Updateren sender intet til dem. Lightforges side har detaljerne." },
				{ "lf.license", "Lightforge {0} er udgivet under {1} og lavet af Lightforge-projektet, ikke af EldoUI. Assistenten henter det uændret; kildekoden ligger på dets side." },
				{ "lf.btn.install", "Installér Lightforge {0}" },
				{ "lf.btn.reinstall", "Installér {0} igen" },
				{ "lf.btn.open", "Åbn Lightforge" },
				{ "lf.btn.use", "Brug dets WoW: Forever" },
				{ "lf.btn.check", "Tjek igen" },
				{ "lf.btn.page", "Lightforges side" },
				{ "lf.btn.source", "Kildekode" },
				{ "lf.busy.read", "Læser dit system og Lightforge…" },
				{ "lf.busy.download", "Henter Lightforge {0} fra GitHub…" },
				{ "lf.busy.unpack", "Tjekker og pakker ud…" },
				{ "lf.busy.place", "Lægger filerne på plads…" },
				{ "lf.done", "Lightforge {0} er installeret. Åbn det for at sætte Battle.net og WoW: Forever op." },
				{ "lf.failed", "Lightforge blev ikke installeret: {0} Det, der var der før, er uændret." },
				{ "lf.used", "Updateren bruger nu {0}." },
				{ "linux.notray", "Bakke-ikonet kræver et panel med StatusNotifier (på GNOME: udvidelsen AppIndicator). Updateren virker også uden; åbn den fra programmenuen." },
			};
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
