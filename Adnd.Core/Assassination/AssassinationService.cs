using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Adnd.Core.Assassination
{
    public class AssassinationRow
    {
        public int AssassinLevel { get; set; }
        public List<int> TargetBandChances { get; set; } = new();
    }

    public class AssassinationTableModel
    {
        public List<string> TargetBands { get; set; } = new();
        public List<AssassinationRow> AssassinationTable { get; set; } = new();
    }

    public class AssassinationService
    {
        private readonly List<AssassinationRow> _table;

        public AssassinationService(string dataPath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, dataPath, "AssassinationTable.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"Warning: Assassination table not found at {path}. Assassination disabled.");
                _table = new List<AssassinationRow>();
                return;
            }

            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<AssassinationTableModel>(json);


            if (model == null || model.AssassinationTable == null || model.AssassinationTable.Count == 0)
                throw new Exception("AssassinationTable.json is missing or invalid.");

            _table = model.AssassinationTable;
        }

        /// <summary>
        /// Returns the assassination chance (%) for assassin level and victim level/hit dice.
        /// </summary>
        public int GetAssassinationChance(int assassinLevel, int victimLevel)
        {
            assassinLevel = Math.Clamp(assassinLevel, 1, 15);

            if (victimLevel < 1)
                victimLevel = 1;

            var bandIndex = GetVictimBandIndex(victimLevel);
            var entry = _table.FirstOrDefault(e => e.AssassinLevel == assassinLevel);

            if (entry == null)
                throw new Exception($"No assassination entry found for assassin level {assassinLevel}.");

            if (entry.TargetBandChances == null || entry.TargetBandChances.Count <= bandIndex)
                throw new Exception($"No assassination band entry found for assassin level {assassinLevel}, band index {bandIndex}.");

            var chance = entry.TargetBandChances[bandIndex];
            return Math.Max(0, chance);
        }

        private static int GetVictimBandIndex(int victimLevel)
        {
            if (victimLevel <= 1) return 0;
            if (victimLevel <= 3) return 1;
            if (victimLevel <= 5) return 2;
            if (victimLevel <= 7) return 3;
            if (victimLevel <= 9) return 4;
            if (victimLevel <= 11) return 5;
            if (victimLevel <= 13) return 6;
            if (victimLevel <= 15) return 7;
            if (victimLevel <= 17) return 8;
            return 9;
        }

        /// <summary>
        /// Performs the assassination roll (1d100) and returns detailed result.
        /// </summary>
        public (int ChancePercent, int Roll, bool Success) RollAssassination(int assassinLevel, int victimLevel, Random rng)
        {
            int chance = GetAssassinationChance(assassinLevel, victimLevel);
            int roll = rng.Next(1, 101); // 1–100

            return (chance, roll, roll <= chance);
        }

        /// <summary>
        /// Performs the assassination roll (1d100) and returns true if successful.
        /// </summary>
        public bool TryAssassinate(int assassinLevel, int victimLevel, Random rng)
        {
            var result = RollAssassination(assassinLevel, victimLevel, rng);
            return result.Success;
        }
    }
}
