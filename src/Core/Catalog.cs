// The addons the player can add: the three companions from earlier versions and the reviewed catalog.
// Every entry points at where its author publishes it. Nothing is mirrored or hosted by EldoUI.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EldoUI.Updater.Core
{
	/// <summary>Where an addon's releases come from.</summary>
	public enum AddonSource
	{
		/// <summary>GitHub releases: checked and installed, with the publisher's SHA-256.</summary>
		GitHub,
		/// <summary>WoWInterface: the newest version is checked; it publishes no SHA-256, so the player installs it from its page.</summary>
		WowInterface,
		/// <summary>TukUI: as WoWInterface.</summary>
		Tukui,
		/// <summary>Only a page to open (CurseForge, or a download the updater never installs).</summary>
		Page,
	}

	public sealed class CatalogEntry
	{
		public string Id, Name, Description, Category, GitHub, AssetHint, Tukui, Url, Notice;
		public int WowInterface, CurseForge;
		public string[] Folders = new string[0];
		/// <summary>True when the author ships a Forever build, false when not yet, null when unknown.</summary>
		public bool? Forever;
		/// <summary>The companion definition from earlier versions (DBM, RestedXP, SharedMedia), or null.</summary>
		public Companion Legacy;
		/// <summary>The EldoUI modules that do this addon's job in full or in part, or work with it (CoverageData); empty when none.</summary>
		public List<CoveringModule> CoveredBy = new List<CoveringModule>();

		public AddonSource Source
		{
			get
			{
				if (GitHub != null) return AddonSource.GitHub;
				if (WowInterface > 0) return AddonSource.WowInterface;
				if (Tukui != null) return AddonSource.Tukui;
				return AddonSource.Page;
			}
		}

		/// <summary>The updater can install and update it: a GitHub source whose author does not say it lacks a Forever build.</summary>
		public bool Installable { get { return Source == AddonSource.GitHub && Forever != false; } }

		/// <summary>The publisher's page for this addon, built from the entry's id, or null.</summary>
		public string PageUrl
		{
			get
			{
				if (GitHub != null) return "https://github.com/" + GitHub;
				if (WowInterface > 0) return "https://www.wowinterface.com/downloads/info" + WowInterface;
				if (Tukui != null) return "https://tukui.org/" + Tukui;
				return Release.IsAllowedPage(Url) ? Url : null;
			}
		}

		/// <summary>The install rules for this addon: the earlier companion's, or one built from the catalog's folder list.</summary>
		public Companion Package
		{
			get
			{
				if (Legacy != null) return Legacy;
				if (_package == null && GitHub != null)
					_package = new Companion
					{
						Id = Id, Name = Name, Repository = GitHub, AssetHint = AssetHint, Folders = Folders,
						FolderPattern = @"\A(?:" + string.Join("|", Folders.Select(Regex.Escape)) + @")(?:[_-][A-Za-z0-9][A-Za-z0-9_.-]*)?\z",
					};
				return _package;
			}
		}
		Companion _package;
	}

	/// <summary>An EldoUI module that does an addon's job, with its name in English and Danish.</summary>
	public sealed class CoveringModule
	{
		/// <summary>"full" when the module does the addon's whole job, "part" when it does some of it, "works" when it works with the addon.</summary>
		public string Module, Scope, English, Danish;
	}

	public static class AddonCatalog
	{
		static List<CatalogEntry> _all;
		static readonly Regex Slug = new Regex(@"\A[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant);
		static readonly Regex Repo = new Regex(@"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant);
		static readonly Regex FolderName = new Regex(@"\A[A-Za-z0-9!_.-][A-Za-z0-9!_. -]{0,99}\z", RegexOptions.CultureInvariant);

		/// <summary>The catalog's AddonForge commit (see NOTICE).</summary>
		public static string Commit { get { return CatalogData.Commit; } }

		public static IReadOnlyList<CatalogEntry> All
		{
			get
			{
				if (_all == null) _all = WithCoverage(Load(CatalogData.Json), CoverageData.Json);
				return _all;
			}
		}

		public static CatalogEntry Find(string id)
		{
			return id == null ? null : All.FirstOrDefault(e => e.Id == id);
		}

		/// <summary>The entry whose folder list names this AddOns folder, or null.</summary>
		public static CatalogEntry ForFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder)) return null;
			return All.FirstOrDefault(e => e.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
				?? All.FirstOrDefault(e => e.Legacy != null && e.Legacy.Owns(folder + "/"));
		}

		/// <summary>The EldoUI version the coverage notes were made from.</summary>
		public static string CoverageVersion { get { return Json.ParseObject(CoverageData.Json).Str("eldoui"); } }

		/// <summary>Adds which EldoUI modules do each addon's job or work with it. Ids the catalog does not know are left out.</summary>
		public static List<CatalogEntry> WithCoverage(List<CatalogEntry> list, string json)
		{
			var addons = Json.ParseObject(json).Obj("addons") ?? new Dictionary<string, object>();
			foreach (var e in list)
			{
				object value;
				if (!addons.TryGetValue(e.Id, out value) || !(value is List<object>)) continue;
				foreach (var m in ((List<object>)value).OfType<Dictionary<string, object>>())
				{
					string en = m.Str("en");
					if (string.IsNullOrEmpty(en)) continue;
					string scope = m.Str("scope");
					if (scope != "part" && scope != "works") scope = "full";
					e.CoveredBy.Add(new CoveringModule { Module = m.Str("module"), Scope = scope, English = en, Danish = m.Str("da") ?? en });
				}
			}
			return list;
		}

		/// <summary>The categories in the catalog, in alphabetical order.</summary>
		public static IEnumerable<string> Categories { get { return All.Select(e => e.Category).Where(c => c != null).Distinct().OrderBy(c => c, StringComparer.Ordinal); } }

		/// <summary>Reads catalog JSON. Entries with a malformed id, repository or folder are left out.</summary>
		public static List<CatalogEntry> Load(string json)
		{
			var list = new List<CatalogEntry>();
			foreach (var item in Json.ParseObject(json).Arr("addons") ?? new List<object>())
			{
				var o = item as Dictionary<string, object>;
				if (o == null) continue;
				var e = new CatalogEntry
				{
					Id = o.Str("id"),
					Name = o.Str("name"),
					Description = o.Str("desc") ?? "",
					Category = o.Str("category") ?? "Utility",
					GitHub = o.Str("github"),
					AssetHint = o.Str("asset_hint"),
					Tukui = o.Str("tukui"),
					Url = o.Str("url"),
					Notice = o.Str("notice"),
					WowInterface = (int)o.Num("wowi"),
					CurseForge = (int)o.Num("curse"),
					Folders = (o.Arr("folders") ?? new List<object>()).OfType<string>().ToArray(),
				};
				object forever;
				if (o.TryGetValue("forever", out forever) && forever is bool) e.Forever = (bool)forever;
				if (e.Id == null || !Slug.IsMatch(e.Id) || string.IsNullOrEmpty(e.Name) || list.Any(x => x.Id == e.Id)) continue;
				if (e.GitHub != null && !Repo.IsMatch(e.GitHub)) continue;
				if (e.Tukui != null && !Slug.IsMatch(e.Tukui)) continue;
				if (e.Folders.Any(f => !FolderName.IsMatch(f) || f.EndsWith(".") || f.EndsWith(" ") || AddonInstaller.IsEldoUIFolder(f))) continue;
				if (e.GitHub != null && e.Folders.Length == 0) continue;
				e.Legacy = Companion.All.FirstOrDefault(c => c.Id == e.Id);
				list.Add(e);
			}
			// Companions from earlier versions that the catalog does not list keep their place.
			foreach (var c in Companion.All.Where(c => !list.Any(e => e.Id == c.Id)))
				list.Add(new CatalogEntry
				{
					Id = c.Id, Name = c.Name, Description = c.Description, Category = c.Category, GitHub = c.Repository,
					Url = "https://github.com/" + c.Repository, Folders = new[] { c.MainFolder }, Forever = true, Legacy = c,
				});
			return list;
		}
	}
}
