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
            case "exit":                Close();
                break;
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
            _combat ??= new CombatCoordinator();
            var outcome = _combat.StartEncounter(this, found.Name, count, party, _characterRepository);
            Log($"  Encounter ended: {outcome}.");
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
