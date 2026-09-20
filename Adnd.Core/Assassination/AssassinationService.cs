using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Adnd.Core.Assassination
{
    public class AssassinationEntry
    {
        public int VictimLevel { get; set; }
        public int ChancePercent { get; set; }
    }

    public class AssassinationTableModel
    {
        public List<AssassinationEntry> AssassinationTable { get; set; }
    }

    public class AssassinationService
    {
        private readonly List<AssassinationEntry> _table;

        public AssassinationService(string dataPath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, dataPath, "AssassinationTable.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"Warning: Assassination table not found at {path}. Assassination disabled.");
                _table = new List<AssassinationEntry>();
                return;
            }

            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<AssassinationTableModel>(json);


            if (model == null || model.AssassinationTable == null || model.AssassinationTable.Count == 0)
                throw new Exception("AssassinationTable.json is missing or invalid.");

            _table = model.AssassinationTable;
        }

        /// <summary>
        /// Returns the assassination chance (%) for a given victim level.
        /// </summary>
        public int GetAssassinationChance(int victimLevel)
        {
            if (victimLevel < 1)
                victimLevel = 1;

            if (victimLevel > 20)
                victimLevel = 20;

            var entry = _table.FirstOrDefault(e => e.VictimLevel == victimLevel);

            if (entry == null)
                throw new Exception($"No assassination entry found for victim level {victimLevel}.");

            return entry.ChancePercent;
        }

        /// <summary>
        /// Performs the assassination roll (1d100) and returns true if successful.
        /// </summary>
        public bool TryAssassinate(int victimLevel, Random rng)
        {
            int chance = GetAssassinationChance(victimLevel);
            int roll = rng.Next(1, 101); // 1–100

            return roll <= chance;
        }
    }
}
