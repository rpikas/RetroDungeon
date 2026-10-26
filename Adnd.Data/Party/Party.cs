
using System.Collections.Generic;
using System.Text.Json.Serialization;
// Character objects are stored in the roster; party stores identifiers only.

namespace Adnd.Data.Party;

public class Party
{
    // Store party members as a list of character names (identifiers). The actual Character data
    // is stored in the roster (Data/Characters). This keeps a single source of truth.
    // Use a custom converter to tolerate older JSON where members were stored as objects
    // (Character objects) instead of string identifiers.
    [JsonConverter(typeof(PartyMembersConverter))]
    public List<string> Members { get; set; } = new();
    // Index of the member currently shopping. -1 means none selected.
    public int CurrentShopperIndex { get; set; } = -1;
    // Number of dungeon days completed (incremented when leaving the dungeon).
    public int DungeonDaysElapsed { get; set; } = 0;

    // Potion of Treasure Finding state.
    public bool TreasureFindingActive { get; set; } = false;
    public int TreasureFindingStepsRemaining { get; set; } = 0;
    public int TreasureFindingTargetLevel { get; set; } = 0;
    public int TreasureFindingTargetX { get; set; } = -1;
    public int TreasureFindingTargetY { get; set; } = -1;
    public int TreasureFindingCopperPieces { get; set; } = 0;
    public int TreasureFindingGemCount { get; set; } = 0;

    // Light spell: steps left during which the party sees one step further.
    public int LightStepsRemaining { get; set; } = 0;

    // Continual Light: the party sees two steps further until dispelled or until it enters magical darkness.
    public bool ContinualLightActive { get; set; } = false;

    /// <summary>Stores what a Light, Continual Light or Dispel Magic cast outside combat did to the party's light.</summary>
    public void ApplyLightSpellResult(Adnd.Core.Spells.Casting.SpellCastResult result)
    {
        if (result.LightSteps > 0)
            LightStepsRemaining = Math.Max(LightStepsRemaining, result.LightSteps);

        if (result.ContinualLight)
            ContinualLightActive = true;

        if (result.DispelsLight)
        {
            LightStepsRemaining = 0;
            ContinualLightActive = false;
        }
    }
}

