
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
}

