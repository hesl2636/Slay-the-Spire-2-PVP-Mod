using PvpDuel.Core.Save;
using PvpDuel.Save;

using Xunit;

namespace PvpDuel.Core.Tests.Save;

/// <summary>
/// Ticket #21 (T9): side-channel file semantics — spec §4.3/§5.7/§8. A failed
/// read or write only drops mod-side data (never throws, never touches the
/// official run save), and payloads are ignored when they belong to a
/// different run or schema version.
/// </summary>
public class DuelRunSaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pvpduel-save-tests-" + Guid.NewGuid().ToString("N"));

    private string PathFor(string file) => Path.Combine(_dir, file);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Write_Then_ReadForRunKey_RoundTrips()
    {
        var path = PathFor("saves/pvpduel_current_run_mp.save.json");
        var payload = DuelSavePayload.Empty("seed-1");
        var json = DuelSaveCodec.Serialize(payload);

        Assert.True(DuelRunSaveStore.TryWrite(path, json));
        var (read, status) = DuelRunSaveStore.TryReadForRunKey(path, "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.Ok, status);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void Write_CreatesMissingDirectories_AndLeavesNoTempFile()
    {
        var path = PathFor("a/b/c/pvpduel_current_run.save.json");

        Assert.True(DuelRunSaveStore.TryWrite(path, DuelSaveCodec.Serialize(DuelSavePayload.Empty("s"))));

        Assert.Equal([], Directory.GetFiles(PathFor("a/b/c")).Where(f => f.EndsWith(".tmp")).ToArray());
    }

    [Fact]
    public void Write_ToUnwritablePath_DegradesWithoutThrowing()
    {
        Directory.CreateDirectory(_dir);
        // A FILE occupying the directory component → directory creation fails.
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "occupied");
        var path = Path.Combine(blocker, "nested", "pvpduel_current_run.save.json");

        Assert.False(DuelRunSaveStore.TryWrite(path, "{}"));
    }

    [Fact]
    public void Read_MissingFile_IsAbsent()
    {
        var (payload, status) = DuelRunSaveStore.TryReadForRunKey(PathFor("nope.json"), "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.Absent, status);
        Assert.Null(payload);
    }

    [Fact]
    public void Read_CorruptJson_IsCorrupt()
    {
        var path = PathFor("saves/pvpduel_current_run_mp.save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        var (payload, status) = DuelRunSaveStore.TryReadForRunKey(path, "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.Corrupt, status);
        Assert.Null(payload);
    }

    [Fact]
    public void Read_VersionMismatch_IsDropped()
    {
        var path = PathFor("saves/pvpduel_current_run_mp.save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schema":999,"runKey":"seed-1","branches":[],"duelResults":[],"ancientPicks":[]}""");

        var (payload, status) = DuelRunSaveStore.TryReadForRunKey(path, "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.VersionMismatch, status);
        Assert.Null(payload);
    }

    [Fact]
    public void Read_RunKeyMismatch_IsIgnored()
    {
        var path = PathFor("saves/pvpduel_current_run_mp.save.json");
        DuelRunSaveStore.TryWrite(path, DuelSaveCodec.Serialize(DuelSavePayload.Empty("another-run")));

        var (payload, status) = DuelRunSaveStore.TryReadForRunKey(path, "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.RunKeyMismatch, status);
        Assert.Null(payload);
    }

    [Fact]
    public void Read_EntryLevelCorruption_StaysLenient()
    {
        // A one-component coord is entry-level corruption: the codec accepts the
        // payload and the restore drops the entry (see DuelSaveMapperTests) —
        // the whole file is not rejected.
        var path = PathFor("saves/pvpduel_current_run_mp.save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"schema":1,"runKey":"seed-1","branches":[
                {"playerNetId":1,"state":{"actIndex":1,"coord":[2],"inBossWait":false}}],
             "duelResults":[],"ancientPicks":[]}
            """);

        var (payload, status) = DuelRunSaveStore.TryReadForRunKey(path, "seed-1");

        Assert.Equal(DuelSideChannelReadStatus.Ok, status);
        Assert.NotNull(payload);
    }
}
