namespace Adnd.Data.Spells;

public class SpellJsonModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public string Description { get; set; } = "";
    public string RangeType { get; set; } = "Enemy";
    public string Targeting { get; set; } = "Single";
    public string TargetingScope { get; set; } = "SingleTarget";
    public string CastContext { get; set; } = "Both";
    public string EffectType { get; set; } = "Damage";
    public string EffectDescription { get; set; } = "";
}
