using System.Reflection;
using PvpDuel.Core.Text;

namespace PvpDuel.Localization;

/// <summary>
/// Mod localization loader. Locale files ship as embedded resources (DLL-only
/// skeleton), all player-visible strings are PVP_DUEL_* / PVP_ANCIENT_* keys —
/// never literals. Unknown keys resolve to the key itself so gaps stay visible.
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "en-US";
    public const string ChineseLanguage = "zh-CN";

    private static readonly object Gate = new();
    private static LocalizationCatalog? _catalog;

    /// <summary>Resolves a loc key against the current (or given) language.</summary>
    public static string Text(string key) => Text(CurrentLanguage(), key);

    public static string Text(string language, string key) => Catalog().Get(language, key);

    /// <summary>Resolves a key and fills {0}..{n} placeholders.</summary>
    public static string Text(string key, params object[] args) =>
        string.Format(Text(CurrentLanguage(), key), args);

    /// <summary>Exposed for tests / preload; lazily builds the embedded catalog.</summary>
    public static LocalizationCatalog Catalog()
    {
        lock (Gate)
        {
            if (_catalog != null)
            {
                return _catalog;
            }

            var catalog = new LocalizationCatalog([DefaultLanguage, ChineseLanguage]);
            foreach (var language in new[] { DefaultLanguage, ChineseLanguage })
            {
                var json = ReadEmbeddedLocale(language);
                if (json != null)
                {
                    catalog.Load(language, json);
                }
            }

            _catalog = catalog;
            return _catalog;
        }
    }

    /// <summary>Resets the catalog (used by tests / locale hot-reload).</summary>
    public static void Reset() => _catalog = null;

    /// <summary>Maps the game's Godot locale to a mod language code, defaulting to en-US.</summary>
    public static string CurrentLanguage()
    {
        try
        {
            var locale = Godot.TranslationServer.GetLocale(); // e.g. "zh_CN", "en_US", "en"
            return MapLocale(locale);
        }
        catch
        {
            return DefaultLanguage;
        }
    }

    public static string MapLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return DefaultLanguage;
        }

        var normalized = locale.Replace('-', '_');
        if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseLanguage;
        }

        if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLanguage;
        }

        return DefaultLanguage;
    }

    private static string? ReadEmbeddedLocale(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"PvpDuel.locales.{language}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
