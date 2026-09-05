using System.Text.Json;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Diagnostics;
namespace PvpDuel.Core.Save;

public enum DuelSaveReadStatus
{
    Ok,

    /// <summary>Malformed JSON or structurally invalid payload — treat as absent.</summary>
    Corrupt,

    /// <summary>Payload written by a different schema version — treat as absent.</summary>
    VersionMismatch,
}

/// <summary>
/// (De)serializer for the mod save side-channel. Contract: a failed read only
/// drops mod-side data (Warn); it never blocks or corrupts the official run save.
/// </summary>
public static class DuelSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    public static string Serialize(DuelSavePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);
        return JsonSerializer.Serialize(payload, Options);
    }

    public static DuelSavePayload ReadOrThrow(string json)
    {
        var (payload, status) = TryRead(json);
        return status switch
        {
            DuelSaveReadStatus.Ok => payload!,
            DuelSaveReadStatus.Corrupt => throw new FormatException("Duel save side-channel payload is corrupt."),
            DuelSaveReadStatus.VersionMismatch => throw new FormatException(
                $"Duel save side-channel schema version mismatch (expected {SaveSchema.CurrentVersion})."),
            _ => throw new InvalidOperationException($"Unhandled read status {status}."),
        };
    }

    /// <summary>
    /// Lenient read used on load: returns a status so the caller can Warn and drop
    /// mod data without touching the official save.
    /// </summary>
    public static (DuelSavePayload? Payload, DuelSaveReadStatus Status) TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, DuelSaveReadStatus.Corrupt);
        }

        DuelSavePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DuelSavePayload>(json, Options);
        }
        catch (JsonException)
        {
            return (null, DuelSaveReadStatus.Corrupt);
        }

        if (payload == null)
        {
            return (null, DuelSaveReadStatus.Corrupt);
        }

        if (payload.Schema != SaveSchema.CurrentVersion)
        {
            return (payload, DuelSaveReadStatus.VersionMismatch);
        }

        try
        {
            Validate(payload);
        }
        catch (ArgumentException)
        {
            return (payload, DuelSaveReadStatus.Corrupt);
        }
        catch (FormatException)
        {
            return (payload, DuelSaveReadStatus.Corrupt);
        }

        return (payload, DuelSaveReadStatus.Ok);
    }

    /// <summary>
    /// Rebuilds Core domain state from a payload, using <paramref name="onError"/>
    /// to report (and skip) individual bad entries so one malformed entry does not
    /// nuke the rest of the mod data.
    /// </summary>
    public static (List<(ulong NetId, BranchState State)> Branches, List<DuelResult> Results)
        Materialize(DuelSavePayload payload, IModLog? log = null)
    {
        var branches = new List<(ulong, BranchState)>();
        foreach (var entry in payload.Branches)
        {
            try
            {
                branches.Add((entry.PlayerNetId, entry.State.ToBranchState()));
            }
            catch (FormatException ex)
            {
                log?.Warn($"Dropping corrupt branch entry for {entry.PlayerNetId}: {ex.Message}");
            }
        }

        var results = new List<DuelResult>();
        foreach (var dto in payload.DuelResults)
        {
            try
            {
                results.Add(dto.ToDuelResult());
            }
            catch (FormatException ex)
            {
                log?.Warn($"Dropping corrupt duel result for act {dto.ActIndex}: {ex.Message}");
            }
        }

        return (branches, results);
    }

    private static void Validate(DuelSavePayload payload)
    {
        if (payload.RunKey.Length == 0)
        {
            throw new ArgumentException("runKey must be non-empty.", nameof(payload));
        }

        foreach (var dto in payload.DuelResults)
        {
            // Force the lazy enum parse so validation errors surface in TryRead.
            _ = dto.ToDuelResult();
        }
    }
}
