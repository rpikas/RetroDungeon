using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private sealed class RoundState(
        CombatSession session,
        List<CombatEvent> events,
        IReadOnlyDictionary<string, CombatAction> partyActions,
        HashSet<string> parrying)
    {
        public CombatSession Session { get; } = session;
        public List<CombatEvent> Events { get; } = events;
        public IReadOnlyDictionary<string, CombatAction> PartyActions { get; } = partyActions;
        public HashSet<string> Parrying { get; } = parrying;
        public bool ChantActiveAtRoundStart { get; set; }
        public bool PrayerActiveAtRoundStart { get; set; }
        public bool PartyAttemptedRun { get; set; }
        public bool TimeStopExtraRound { get; set; }
        public Character? TimeStopCaster { get; set; }
    }
}
