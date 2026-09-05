using Adnd.Core.Characters;

namespace Adnd.Core.Items;

public class Item
{
    public string Name { get; set; } = "";
    public ItemStatus Status { get; set; } = ItemStatus.Implemented;
    public ItemType Type { get; set; }
    public EquipmentSlot? Slot { get; set; } // null = cannot equip (potions etc.)
    public int Cost { get; set; } = 0;
    public int Weight { get; set; } = 0;
    public int ToHitBonus { get; set; } = 0;
    public bool IsShopBuyable { get; set; } = true;
    public int? StockQuantity { get; set; } = null; // null = unlimited

    // Combat stats
    public int ArmorClassBonus { get; set; } = 0;
    public string Damage { get; set; } = ""; // e.g. "1d6", "2d4"
    public string DamageVsLarge { get; set; } = ""; // e.g. "1d6", "2d4"
    public string DamageType { get; set; } = ""; // e.g. "slashing", "piercing", "bludgeoning". TBD: should be enum
    public int SpeedFactor { get; set; } = 0;
    public string WeaponLength { get; set; } = ""; // e.g. "short", "medium", "long". TBD: should be enum
    public bool IsTwoHanded { get; set; } = false;
    public string Range { get; set; } = ""; // e.g. for thrown or ranged weapons. Format: "10/20/30" for short/medium/long
    /*
Intervall	Avstånd	            Modifiering
Short Range	upp till 10 fot	+0  (ingen penalty)
Medium Range upp till 20 fot	–2 to hit
Long Range	upp till 30 fot	    –5 to hit*/

    public int MagicBonus { get; set; } = 0; // e.g. +1, +2, +3
    public List<string> SpecialAbilities { get; set; } = new(); // e.g. ["Flaming", "Frost", "Returning"]>
    public bool IsCursed { get; set; } = false;
    public List<CharacterClass> AllowedClasses { get; set; } = new();

    public RarityType Rarity { get; set; } = RarityType.Uncommon;
    public string Description { get; set; } = "";
    public string Source { get; set; } = ""; // e.g. "DMG", "PHB", "Homebrew"
    public string Version { get; set; } = ""; // e.g. "1e", "2e", "Custom"
    public override string ToString()
    {
        return $"{Name} ({Type})";
    }
}

/* some example items in JSON format:
{
    "Name": "Short Sword",
    "Type": "Weapon",
    "Slot": "MainHand",
    "Cost": 10,
    "Weight": 2,
    "ToHitBonus": 0,
    "IsShopBuyable": true,
    "StockQuantity": null,
    "ArmorClassBonus": 0,
    "Damage": "1d6",
    "DamageVsLarge": "1d4",
    "DamageType": "slashing",
    "SpeedFactor": 0,
    "WeaponLength": "short",
    "IsTwoHanded": false,
    "AllowedClasses": ["Fighter", "Rogue"]
}

some explanation of some of the fields:
1. Combat‑relevanta attribut (högt värde)
Dessa är nästan alltid nödvändiga i AD&D‑liknande system.

DamageType — "Bludgeoning", "Slashing", "Piercing"  
Viktigt för resistances och immunities.

SpeedFactor — AD&D‑specifikt initiativvärde för vapen
Club är t.ex. Speed Factor 4.

WeaponLength — påverkar “first strike” i tight spaces
Club är kort, polearms är långa.

AttackBonus — för magiska vapen (+1, +2, etc.)

IsTwoHanded — "true" / "false"

Range — för kastvapen eller distansvapen
Club kan kastas i vissa kampanjer.

🧙‍♂️ 2. Magi‑ och specialeffekter
Om du vill stödja magiska items:

MagicBonus — "0", "1", "2", "3"

SpecialAbilities — lista, t.ex. ["Flaming", "Frost", "Returning"]

IsCursed — "true" / "false"

CursedEffect — text

📦 3. Inventory‑relevanta attribut
Bra för UI och spelmekanik.

StackSize — för pilar, darts, etc.

Durability — om du vill ha slitage

IsConsumable — potions, scrolls

IsIdentified — AD&D har “unknown magic items”

🧩 4. AD&D‑specifika attribut
Dessa gör systemet mer troget originalreglerna.

SpeedFactor (nämnd ovan)

WeaponTypeCategory — "Small", "Medium", "Large"  
Påverkar vilka klasser som får använda vapnet.

RequiredStrength — vissa vapen kräver STR

RequiredDexterity — vissa ranged weapons kräver DEX

HitDiceVsLargeCreatures — AD&D har olika damage mot Large
Club: 1d6 vs Small/Medium, 1d3 vs Large (beroende på edition).

🛒 5. Shop‑ och ekonomi‑attribut
Du har redan IsShopBuyable, men du kan lägga till:

Rarity — "Common", "Uncommon", "Rare", "Very Rare"

SellValue — om du vill ha annan prislogik än Cost

IsIllegal — för black market items

🧰 6. Metadata
För utveckling och debugging.

Description — kort text

Source — "DMG", "PHB", "Homebrew"

Version — "1e", "2e", "Custom"
*/