using System.Text.Json;

namespace PvpDuel.Core.Text;

/// <summary>
/// Game-independent localization table: flat key/value JSON per language with a
/// deterministic fallback chain (requested language → fallback languages → key).
/// The game project feeds it the embedded locale files; all player-visible
/// strings flow through keys (PVP_DUEL_* / PVP_ANCIENT_*), never literals.
/// </summary>
public sealed class LocalizationCatalog
{

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = new(StringComparer.Ordinal);
    private readonly List<string> _fallbackOrder;

    /// <param name="fallbackOrder">Languages to try in order when a key is missing from the requested table.</param>
    public LocalizationCatalog(IEnumerable<string> fallbackOrder)
    {
        _fallbackOrder = [.. fallbackOrder ?? throw new ArgumentNullException(nameof(fallbackOrder))];
        if (_fallbackOrder.Count == 0)
        {
            throw new ArgumentException("Fallback order must contain at least one language.", nameof(fallbackOrder));
        }
    }

    /// <summary>Parses a flat key/value JSON table and registers it under <paramref name="language"/>.</summary>
    public void Load(string language, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new FormatException($"Localization table for '{language}' did not deserialize to a key/value map.");
        _tables[language] = parsed;
    }

    /// <summary>The raw table for a language (null when absent) — lets the mod mirror selected keys into the game's loc tables.</summary>
    public IReadOnlyDictionary<string, string>? GetTable(string language) =>
        _tables.TryGetValue(language, out var table) ? table : null;
    public bool HasLanguage(string language) => _tables.ContainsKey(language);

    /// <summary>
    /// Resolves a key: requested table first, then the fallback chain, then the
    /// key itself (so a missing translation is visible rather than silent).
    /// </summary>
    public string Get(string language, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var table in Candidates(language))
        {
            if (table.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return key;
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> Candidates(string language)
    {
        if (_tables.TryGetValue(language, out var requested))
        {
            yield return requested;
        }

        foreach (var fallback in _fallbackOrder)
        {
            if (fallback != language && _tables.TryGetValue(fallback, out var table))
            {
                yield return table;
            }
        }
    }
}
