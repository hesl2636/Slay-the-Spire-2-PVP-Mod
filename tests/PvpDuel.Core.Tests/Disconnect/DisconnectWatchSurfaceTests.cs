using System.Reflection;

using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Branching;
using PvpDuel.Disconnect;
using PvpDuel.Localization;
using PvpDuel.Logging;

using Xunit;

namespace PvpDuel.Core.Tests.Disconnect;

/// <summary>
/// Ticket #25: static-reflection asserts over the game surfaces the disconnect
/// watch hangs on — the official events (zero Harmony: the mod must never
/// touch the vanilla disconnect paths, spec §5.1 regression red line), the
/// directed host send used for the state backfill, the reflective branch-table
/// seam and the localized overlay keys. No game singletons are touched (the
/// game Logger static ctor 0xC0000005 rule).
/// </summary>
public class DisconnectWatchSurfaceTests
{
    static DisconnectWatchSurfaceTests() => PvpDuelLog.Enabled = false;

    [Fact]
    public void RunManager_RoomWindowEvents_Exist()
    {
        var entered = typeof(RunManager).GetEvent(nameof(RunManager.RoomEntered), BindingFlags.Public | BindingFlags.Instance);
        var exited = typeof(RunManager).GetEvent(nameof(RunManager.RoomExited), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(entered);
        Assert.NotNull(exited);
        Assert.Equal(typeof(Action), entered!.EventHandlerType);
        Assert.Equal(typeof(Action), exited!.EventHandlerType);
    }

    [Fact]
    public void RunManager_RunLobby_IsPubliclyReadable()
    {
        var property = typeof(RunManager).GetProperty(nameof(RunManager.RunLobby), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(typeof(RunLobby), property!.PropertyType);
        // Readable by the mod (the watch subscribes the per-run lobby events).
        Assert.NotNull(property.GetGetMethod());
    }

    [Fact]
    public void RunLobby_DisconnectAndRejoinEvents_MatchHandlerShapes()
    {

        var disconnected = typeof(RunLobby).GetEvent(nameof(RunLobby.RemotePlayerDisconnected), BindingFlags.Public | BindingFlags.Instance);
        var rejoined = typeof(RunLobby).GetEvent(nameof(RunLobby.PlayerRejoined), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(disconnected);
        Assert.NotNull(rejoined);
        Assert.Equal(typeof(Action<ulong>), disconnected!.EventHandlerType);
        Assert.Equal(typeof(Action<RunLobbyPlayer>), rejoined!.EventHandlerType);
    }

    [Fact]
    public void RunLobby_LocalDisconnectedEvent_Exists()
    {
        // The client flags its own session loss on this official event to
        // schedule the rejoin backfill request (no game-type interference).
        var local = typeof(RunLobby).GetEvent(nameof(RunLobby.LocalPlayerDisconnected), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(local);
        Assert.Equal(typeof(Action), local!.EventHandlerType);
    }

    [Fact]
    public void NetGameService_DirectedSend_Exists()
    {
        // The state backfill must reach the rejoined peer only — the directed
        // generic SendMessage<T>(message, playerId) overload (RunLobby rejoin
        // precedent, available on both service roles).
        var send = typeof(INetGameService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(m => m.Name == nameof(INetGameService.SendMessage)
                && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericParameter
                && m.GetParameters()[1].ParameterType == typeof(ulong));

        Assert.NotNull(send);
    }

    [Fact]
    public void BranchSync_TableField_ReflectiveSeamExists()
    {
        // DisconnectWatch.BranchTableRef reads this field exactly like the save
        // side channel does (no widened surface on the branch layer).
        var field = typeof(BranchSync).GetField("Table", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(typeof(PvpDuel.Core.Duel.BranchTable), field!.FieldType);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void ReconnectAndForfeitLocKeys_Resolve(string language)
    {
        var reconnect = Loc.Text(language, "PVP_DUEL_RECONNECT");
        var forfeit = Loc.Text(language, "PVP_DUEL_DISCONNECT_TIMEOUT");

        // Unknown keys resolve to the key itself — assert real copy instead.
        Assert.NotEqual("PVP_DUEL_RECONNECT", reconnect);
        Assert.NotEqual("PVP_DUEL_DISCONNECT_TIMEOUT", forfeit);
        Assert.Equal(1, reconnect.Count(c => c == '{'));
        Assert.Equal(2, forfeit.Count(c => c == '{'));
    }

    [Fact]
    public void BackfillMessages_RoundTripThroughPacketWriter()
    {
        var response = new DuelStateBackfillMessage { PayloadJson = "{\"schema\":1,\"runKey\":\"abc\"}" };
        var writer = new PacketWriter();
        response.Serialize(writer);
        var bytes = writer.Buffer[..writer.BytePosition];

        var reader = new PacketReader();
        reader.Reset(bytes);
        var decoded = new DuelStateBackfillMessage();
        decoded.Deserialize(reader);

        Assert.Equal(response.PayloadJson, decoded.PayloadJson);

        var request = new DuelStateBackfillRequestMessage();
        var requestWriter = new PacketWriter();
        request.Serialize(requestWriter);
        Assert.Equal(0, requestWriter.BytePosition);
    }
}
