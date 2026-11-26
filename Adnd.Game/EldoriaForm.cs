using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Data.Characters;
using Adnd.Data.Monsters;
using Adnd.Game.Combat;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Adnd.Game;

/// <summary>
/// Eldoria: the AD&D overland, drawn by three.js inside WebView2. The page owns movement and scenery; the
/// game rules stay in C#, so the page only reports (ready, position, exit) and is told who is in the party.
/// </summary>
public sealed class EldoriaForm : Form
{
    private const string Host = "eldoria.local";
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly IReadOnlyList<Character> _members;
    private readonly int _seed;
    private readonly Random _random = new();
    private readonly CharacterRepository _characterRepository = new("Data/Characters");
    private CombatCoordinator? _combat;
    private OutdoorEncounterTable? _table;

    // Chance in percent that a check (made every 20 units walked) leads to an encounter.
    private static readonly Dictionary<string, int> CheckChance = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plain"] = 20, ["Scrub"] = 20, ["Forest"] = 30, ["Rough"] = 25,
        ["Desert"] = 15, ["Hills"] = 25, ["Mountains"] = 25, ["Marsh"] = 30
    };

    private const int MaxRerolls = 20;
    public EldoriaForm(IReadOnlyList<Character> members, int seed = 1)
    {
        _members = members;
        _seed = seed;
        Text = "Eldoria";
        BackColor = System.Drawing.Color.Black;
        KeyPreview = false;
        Controls.Add(_web);
        Load += async (_, _) => await InitializeAsync();
    }

    public System.Drawing.PointF LastPosition { get; private set; }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(Path.GetTempPath(), "RetroDungeon.WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _web.EnsureCoreWebView2Async(env);

            var folder = Path.Combine(AppContext.BaseDirectory, "Eldoria", "web");
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.Allow);
            _web.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.CoreWebView2.Navigate($"https://{Host}/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Eldoria needs the Microsoft Edge WebView2 runtime.\n\n" + ex.Message,
                "Eldoria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                SendParty();
                break;
            case "position":
                LastPosition = new System.Drawing.PointF(
                    root.GetProperty("x").GetSingle(), root.GetProperty("z").GetSingle());
                break;
            case "encounterCheck":
                var terrain = root.GetProperty("terrain").GetString() ?? "Plain";
                BeginInvoke(() => HandleEncounterCheck(terrain));
                break;
            case "roundDone":
                _roundDone = true;
                break;
            case "exit":                Close();
                break;
        }
    }

    private bool _roundDone;

    private static readonly System.Text.RegularExpressions.Regex AttackVerb = new(
        @"\b(?<verb>hits|misses)\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex CastVerb = new(
        @"\bcasts (?<spell>[A-Za-z' \-]+?)(?=[!.,:(]|$| but\b| on\b| at\b)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex DamageText = new(
        @"for (?<dmg>\d+) damage", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex HealText = new(
        @"healed for (?<n>\d+) HP", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex HpChange = new(
        @"HP (?<a>\d+)->(?<b>\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private sealed class SpellStep
    {
        public string t { get; init; } = "spell";
        public string spell { get; init; } = "";
        public string fs { get; init; } = "";
        public int fi { get; init; }
        public string ts { get; init; } = "";
        public int ti { get; init; }
        public int heal { get; set; }
        public int power { get; set; }
    }

    // How much healing or damage a message reports; it sets the size of the spell effect.
    private static (int heal, int power) Magnitude(string message)
    {
        var heal = HealText.Match(message);
        if (heal.Success)
            return (int.Parse(heal.Groups["n"].Value), int.Parse(heal.Groups["n"].Value));
        var hp = HpChange.Match(message);
        if (hp.Success)
            return (0, Math.Max(0, int.Parse(hp.Groups["a"].Value) - int.Parse(hp.Groups["b"].Value)));
        var damage = DamageText.Match(message);
        return (0, damage.Success ? int.Parse(damage.Groups["dmg"].Value) : 0);
    }

    private static readonly string[] KnownSpellWords =
        { "Insect Plague", "Summon Insects", "Wall of Fire", "Fire Wall", "Blade Barrier" };

    // Finds the longest known name in the text: ("p"|"m", index) or null.
    private static (string side, int index)? FindName(string text, IReadOnlyList<string> party, IReadOnlyList<string> monsters)
    {
        (string side, int index)? best = null;
        var bestLength = 0;
        for (var i = 0; i < party.Count; i++)
            if (party[i].Length > bestLength && text.Contains(party[i], StringComparison.Ordinal))
                (best, bestLength) = (("p", i), party[i].Length);
        for (var i = 0; i < monsters.Count; i++)
            if (monsters[i].Length > bestLength && text.Contains(monsters[i], StringComparison.Ordinal))
                (best, bestLength) = (("m", i), monsters[i].Length);
        return best;
    }

    // Turns the round's text events into animation steps and waits while the page plays them.
    private void OnRoundNarrated(CombatSession session, IReadOnlyList<Adnd.Core.Combat.Events.CombatEvent> events)
    {
        if (_web.CoreWebView2 == null || IsDisposed)
            return;

        var party = _members.Select(m => m.Name).ToList();
        var monsters = session.Monsters.Select(m => m.DisplayName).ToList();

        // Spells that last several rounds keep showing until the game says they have ended.
        var lingering = new (string Kind, Adnd.Core.Combat.Sessions.MonsterStatus Status)[]
        {
            ("fire", Adnd.Core.Combat.Sessions.MonsterStatus.WallOfFire),
            ("insects", Adnd.Core.Combat.Sessions.MonsterStatus.SummonInsects),
            ("fog", Adnd.Core.Combat.Sessions.MonsterStatus.DeathFog),
            ("inferno", Adnd.Core.Combat.Sessions.MonsterStatus.IncendiaryCloud),
        }
        .Select(l => new
        {
            kind = l.Kind,
            monsters = session.Monsters.Select((m, i) => (m, i))
                .Where(x => x.m.IsAlive && x.m.HasStatus(l.Status)).Select(x => x.i).ToArray()
        })
        .Where(l => l.monsters.Length > 0)
        .ToList();
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "linger", list = lingering }));

        var steps = new List<object>();
        var seconds = 0.0;
        SpellStep? lastSpell = null;

        foreach (var message in events.Select(e => e.Message))
        {
            var cast = CastVerb.Match(message);
            var keyword = KnownSpellWords.FirstOrDefault(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (cast.Success || keyword != null)
            {
                var spell = keyword ?? cast.Groups["spell"].Value.Trim();
                var caster = FindName(cast.Success ? message[..cast.Index] : message, party, monsters);
                if (caster == null)
                    continue;
                var rest = cast.Success ? message[cast.Index..] : message;
                var target = FindName(rest, party, monsters);
                seconds += 3;
                var spellStep = new SpellStep
                {
                    spell = spell,
                    fs = caster.Value.side,
                    fi = caster.Value.index,
                    ts = target?.side ?? (caster.Value.side == "p" ? "m" : "p"),
                    ti = target?.index ?? -1
                };
                var (healed, power) = Magnitude(message);
                spellStep.heal = healed;
                spellStep.power = power;
                steps.Add(spellStep);
                lastSpell = spellStep;
                continue;
            }

            // Damage lines that follow a cast add to the size of that spell's effect.
            if (lastSpell != null && !AttackVerb.IsMatch(message))
                lastSpell.power += Magnitude(message).power;
            else
                lastSpell = null;

            var verb = AttackVerb.Match(message);
            if (!verb.Success)
                continue;
            var from = FindName(message[..verb.Index], party, monsters);
            var to = FindName(message[verb.Index..], party, monsters);
            if (from == null || to == null)
                continue;
            var damage = DamageText.Match(message);
            seconds += 1.2;
            steps.Add(new
            {
                t = verb.Groups["verb"].Value == "hits" ? "hit" : "miss",
                fs = from.Value.side, fi = from.Value.index,
                ts = to.Value.side, ti = to.Value.index,
                dmg = damage.Success ? int.Parse(damage.Groups["dmg"].Value) : 0
            });
        }

        if (steps.Count == 0)
            return;

        _roundDone = false;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "round",
            steps,
            monstersAlive = session.Monsters.Select(m => m.IsAlive).ToArray()
        }));

        // The dialog is modal, so keep the message loop running while the animation plays.
        var deadline = DateTime.UtcNow.AddSeconds(Math.Min(600, 10 + seconds * 4));
        while (!_roundDone && !IsDisposed && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(15);
        }
    }

    private void Log(string text) =>
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "log", text }));

    private void Resume() =>
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "resume" }));

    private async void HandleEncounterCheck(string terrain)
    {
        try
        {
            var chance = CheckChance.GetValueOrDefault(terrain, 8);
            var check = _random.Next(1, 101);
            if (check > chance)
            {
                Resume();
                return;
            }

            Log($"[{terrain}] Encounter check d100={check} (<= {chance}): something approaches.");
            _table ??= OutdoorEncounterTableLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Rules", "OutdoorEncounter.xls"));
            var monsters = new MonsterRepository().GetAll().Where(m => m.Source != Sources.Wizardry).ToList();

            Monster? found = null;
            for (var attempt = 1; attempt <= MaxRerolls && found == null; attempt++)
            {
                var roll = _random.Next(1, 101);
                var creature = _table.Lookup(terrain, roll);
                if (creature == null)
                {
                    Log($"  Roll {attempt}: d100={roll} on {terrain} - no entry, reroll.");
                    continue;
                }

                found = MazeForm.FindMonsterByName(monsters, creature);
                Log(found == null
                    ? $"  Roll {attempt}: d100={roll} on {terrain} = {creature} - no such monster in the monster files, reroll."
                    : $"  Roll {attempt}: d100={roll} on {terrain} = {creature} -> {found.Name}.");
            }

            if (found == null)
            {
                Log($"  No usable monster after {MaxRerolls} rolls; the way is clear.");
                Resume();
                return;
            }

            var min = Math.Max(1, found.NumberOfAppearancesMin);
            var max = Math.Max(min, found.NumberOfAppearancesMax);
            var count = _random.Next(min, max + 1);
            Log($"  {count} x {found.Name} (group size {min}-{max}).");

            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "monsters", name = found.Name, count }));
            await System.Threading.Tasks.Task.Delay(2000);

            var party = _members.Where(m => !m.HasStatus(CharacterStatus.Dead) && !m.HasStatus(CharacterStatus.Ashes)).ToList();
            _combat ??= CreateCombat();
            var outcome = _combat.StartEncounter(this, found.Name, count, party, _characterRepository);
            Log($"  Encounter ended: {outcome}.");
            SendStatus();
            var exit = _combat.RequestedExit;
            _combat.RequestedExit = EncounterExit.None;
            if (exit == EncounterExit.QuitGame)
                Environment.Exit(0);
            if (exit == EncounterExit.TeleportToTown)
            {
                Close();
                return;
            }

            if (outcome == CombatOutcome.Defeat)
            {
                MessageBox.Show(this, "The party has fallen in Eldoria.", "Eldoria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }
        }
        catch (Exception ex)
        {
            Log("Encounter error: " + ex.Message);
        }

        Resume();
    }

    private CombatCoordinator CreateCombat()
    {
        var combat = new CombatCoordinator();
        combat.RoundNarrated += OnRoundNarrated;
        return combat;
    }

    private static bool IsDown(Character m) =>
        m.HasStatus(CharacterStatus.Dead) || m.HasStatus(CharacterStatus.Ashes) || m.CurrentHitPoints <= 0;

    private void SendStatus() =>
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "status", dead = _members.Select(IsDown).ToList() }));

    private void SendParty()
    {
        var members = _members.Select(m =>
        {
            string? Worn(EquipmentSlot slot) => m.Equipment.TryGetValue(slot, out var item) ? item?.Name : null;
            var offHand = m.Equipment.TryGetValue(EquipmentSlot.OffHand, out var off) ? off : null;
            return new
            {
                name = m.Name,
                @class = string.Join("/", m.Classes),
                level = m.Level,
                race = m.Race.ToString(),
                gender = m.Gender.ToString(),
                dead = IsDown(m),
                equipment = new
                {
                    mainHand = Worn(EquipmentSlot.MainHand),
                    shield = offHand?.Type == ItemType.Shield ? offHand.Name : null,
                    body = Worn(EquipmentSlot.Body),
                    head = Worn(EquipmentSlot.Head),
                    back = Worn(EquipmentSlot.Back)
                }
            };
        }).ToList();
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "party", seed = _seed, members }));
    }
}
