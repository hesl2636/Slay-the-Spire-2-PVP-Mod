using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Models;

using PvpDuel.Ancients;
using PvpDuel.Localization;

using Xunit;

namespace PvpDuel.Core.Tests.Ancients;

/// <summary>
/// Ticket #24: the self-built picker model's contract surface. Constructing
/// the model here is safe headless (the AbstractModel ctor only derives the
/// ModelId and reads the empty ModelDb map); no game singletons are touched.
/// These asserts lock the derived identity (loc keys, asset paths and the
/// dialogue keys all key off the entry string) and the minimal dialogue set
/// the ancient layout requires.
/// </summary>
public class DuelAncientEventModelSurfaceTests
{
    [Fact]
    public void Model_IsAncientEventModelSubclass()
    {
        var model = new DuelAncientEventModel();
        Assert.IsAssignableFrom<AncientEventModel>(model);
    }

    [Fact]
    public void ModelId_DerivesToStableEntry()
    {
        // ModelDb.GetId: category from the direct-abstract base (EventModel →
        // EVENT), entry from the slugified type name. The loc keys shipped in
        // the "ancients" table and every asset alias path derive from this
        // entry — renaming the type silently breaks the room.
        var id = ModelDb.GetId(typeof(DuelAncientEventModel));
        Assert.Equal(new ModelId("EVENT", "DUEL_ANCIENT_EVENT_MODEL"), id);
    }

    [Fact]
    public void AssetAlias_PathsMatchTheConsumersDerivation()
    {
        // NAncientEventLayout pulls the background scene; NEventLayout the
        // portrait; the dialogue lines the run-history icons; the map layer
        // the node icons. Each is derived from the model id exactly like the
        // vanilla properties do — the aliases must land on those paths.
        var entry = ModelDb.GetId(typeof(DuelAncientEventModel)).Entry; // the production input is the derived entry
        Assert.EndsWith("/events/background_scenes/duel_ancient_event_model.tscn",
            AncientAssetAlias.BackgroundScenePath(entry));
        Assert.EndsWith("/events/duel_ancient_event_model.png",
            AncientAssetAlias.PortraitPath(entry));
        Assert.EndsWith("/ui/run_history/duel_ancient_event_model.png",
            AncientAssetAlias.RunHistoryIconPath(entry));
        Assert.EndsWith("/ui/run_history/duel_ancient_event_model_outline.png",
            AncientAssetAlias.RunHistoryIconOutlinePath(entry));
        Assert.EndsWith("/packed/map/ancients/ancient_node_duel_ancient_event_model.png",
            AncientAssetAlias.MapIconPath(entry));
        Assert.EndsWith("/packed/map/ancients/ancient_node_duel_ancient_event_model_outline.png",
            AncientAssetAlias.MapIconOutlinePath(entry));
    }

    [Fact]
    public void DialogueSet_MinimalAgnosticDialogueIsValid()
    {
        // NEventRoom draws one dialogue via GetValidDialogues(charVisits=0)
        // and Rng.Chaotic.NextItem — an empty set would throw on the empty
        // draw, so the picker ships exactly one visit-0 agnostic dialogue.
        var set = DuelAncientEventModel.CreateDialogueSet();
        Assert.Null(set.FirstVisitEverDialogue);
        Assert.Empty(set.CharacterDialogues);
        var dialogue = Assert.Single(set.AgnosticDialogues);
        Assert.True(dialogue.IsRepeating);
        Assert.Null(dialogue.VisitIndex);

        // PopulateLocKeys itself needs the live LocManager (in-game only);
        // the shipped key is asserted against the mod catalog instead —
        // see LocKeys_ForDialogueAndPagesShipInBothLanguages.
        Assert.Single(dialogue.Lines);
    }

    [Fact]
    public void LocKeys_ForDialogueAndPagesShipInBothLanguages()
    {
        // The ancient layout resolves the description/DONE pages and the
        // dialogue line from the game's "ancients" table under the derived
        // entry; the mod locale files must carry every one of those keys
        // (DuelAncientFlow merges them into the game table at room entry).
        var entry = ModelDb.GetId(typeof(DuelAncientEventModel)).Entry;
        string[] keys =
        [
            entry + ".pages.INITIAL.description",
            entry + ".pages.WAITING.description",
            entry + ".pages.DONE.description",
            entry + ".talk.ANY.0-0.ancient",
        ];
        foreach (var language in new[] { Loc.DefaultLanguage, Loc.ChineseLanguage })
        {
            foreach (var key in keys)
            {
                Assert.NotEqual(key, Loc.Catalog().Get(language, key)); // missing keys resolve to themselves
            }
        }
    }

    [Fact]
    public void DialogueSet_GetValidDialoguesAlwaysYieldsTheAgnosticLine()
    {
        var set = DuelAncientEventModel.CreateDialogueSet();
        // (no PopulateLocKeys here — that path needs the live LocManager;
        // GetValidDialogues itself only reads dialogue metadata.)
        // Any character, any visit count — the stats for a modded ancient are
        // always null-derived (0 visits), but NextItem must never see empty.
        Assert.Single(set.GetValidDialogues(ModelId.none, charVisits: 0, totalVisits: 0, allowAnyCharacterDialogues: true));
        Assert.Single(set.GetValidDialogues(ModelId.none, charVisits: 7, totalVisits: 3, allowAnyCharacterDialogues: true));
    }
}
