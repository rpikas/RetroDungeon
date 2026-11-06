using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Data.Spells;
using Adnd.Game.Viewer;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;
using ImageSharpPngEncoder = SixLabors.ImageSharp.Formats.Png.PngEncoder;

namespace Adnd.Game;
/// <summary>
/// see https://blogger.googleusercontent.com/img/b/R29vZ2xl/AVvXsEiHclQZzWYd5CxC1GJxaMxUrUUiIO2QZhSUQn1LHhER3leQJye41fZH4Dz6J1RVNUjlPB9kA-RBGfxwTba8qBFwf8Ol6DadLgyeLhfvnuPW3YmJzaStIxvTLoeH-926-PwVKgzCn4-qZwtp/s400/dmg-appendix-c.jpg
/// 
/// </summary>

public sealed class EncounterForm : Form
{
    private readonly string _monsterName;
    private readonly int _monsterCount;
    private readonly int _asleepMonsterCount;
    private readonly int _entangledMonsterCount;
    private readonly int _panickedMonsterCount;
    private readonly int _fearedMonsterCount;
    private readonly int _turnedMonsterCount;
    private readonly int _blindedMonsterCount;
    private readonly int _confusedMonsterCount;
    private readonly int _stunnedMonsterCount;
    private readonly int _slowedMonsterCount;
    private readonly int _paralyzedMonsterCount;
    private readonly int _unconsciousMonsterCount;
    private readonly int _roundNumber;
    private readonly List<Character> _party;
    private readonly CombatSession? _session;
    private readonly Monster? _singleMonsterTemplate;
    private readonly bool _multipleGroups;
    private readonly Dictionary<string, CombatAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    // Lets the fight be fought from the tabletop as well as the keyboard. While this form is up it
    // is the newest pump, so viewer commands come here and not to the maze underneath.
    private ViewerControlPump? _viewerControl;
    private readonly Image? _monsterImage;
    private readonly List<(string Name, Image? Image)> _monsterImages = new();
    private readonly SpellRepository _spellRepo = new("Data/Spells");

    public IReadOnlyDictionary<string, CombatAction> SelectedActions => _actions;

    private int _currentIndex;

    private readonly Label _headerLabel;
    private readonly Label _optionsTitleLabel;
    private readonly Label _optionsLegendLabel;
    private readonly Panel _monsterPanel;
    private readonly ListView _partyList;

    // Constructor for single group encounters (backward compatibility)
    /// <param name="session">
    /// The fight itself, when the caller has one. Optional and NOT the same thing as
    /// <c>_multipleGroups</c>: a single-group encounter still wants the session, because that is where the
    /// individual monsters live and the table offers them one by one as things to aim at. Without it the
    /// ordinary one-group fight -- which is most fights -- could only offer "Fight" and let the resolver
    /// pick, which is exactly the choice being added.
    /// </param>
    public EncounterForm(string monsterName, int monsterCount, int asleepMonsterCount, int paralyzedMonsterCountLegacy, int entangledMonsterCount, int panickedMonsterCount, int fearedMonsterCount, 
        int turnedMonsterCount, int blindedMonsterCount, int confusedMonsterCount, int stunnedMonsterCount, int slowedMonsterCount, int paralyzedMonsterCount, int unconsciousMonsterCount,
        List<Character> party, int roundNumber, int? dungeonLevel = null, Monster? monsterTemplate = null, CombatSession? session = null)
    {
        _monsterName = monsterName;
        _monsterCount = monsterCount;
        _asleepMonsterCount = asleepMonsterCount;
        _entangledMonsterCount = entangledMonsterCount;
        _panickedMonsterCount = panickedMonsterCount;
        _fearedMonsterCount = fearedMonsterCount;
        _turnedMonsterCount = turnedMonsterCount;
        _blindedMonsterCount = blindedMonsterCount;
        _confusedMonsterCount = confusedMonsterCount;
        _stunnedMonsterCount = stunnedMonsterCount;
        _slowedMonsterCount = slowedMonsterCount;
        _paralyzedMonsterCount = paralyzedMonsterCount;
        _unconsciousMonsterCount = unconsciousMonsterCount;
        _roundNumber = roundNumber;
        _party = party;
        _session = session;
        _singleMonsterTemplate = monsterTemplate;

        // Check if we should use Wizardry suffix
        bool useWizSuffix = monsterTemplate != null && ShouldUseWizardrySuffix(monsterTemplate);
        _monsterImage = TryLoadMonsterImage(monsterName, dungeonLevel, useWizSuffix);

        Text = "Encounter";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 900);//This was 620, but I increased it to 900 to accommodate the new monster status counts and made the monster panel taller to fit the image.
        BackColor = Color.Black;
        ForeColor = GameRulesProvider.Current.DefaultColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        _headerLabel = new Label
        {
            Left = 16,
            Top = 12,
            Width = 940,
            Height = 40,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _optionsTitleLabel = new Label
        {
            Left = 16,
            Top = 64,
            Width = 940,
            Height = 40,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _optionsLegendLabel = new Label
        {
            Left = 16,
            Top = 108,
            Width = 940,
            Height = 56,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 16f, FontStyle.Bold),
            Text = "F)IGHT   U)SE ITEM   R)UN\nS)PELL   P)ARRY      T)AKE BACK   I)NFO",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _monsterPanel = new Panel
        {
            Left = 16,
            Top = 172,
            Width = 940,
            Height = 480,//This was 180, but I increased it to 480 to accommodate the monster image. This is single group
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle
        };
        _monsterPanel.Paint += MonsterPanel_Paint;

        _partyList = new ListView
        {
            Left = 16,
            Top = 656,//More room so 6 party members fit without scrolling.
            Width = 940,
            Height = 224,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        _partyList.Columns.Add("#", 50);
        _partyList.Columns.Add("Character Name", 220);
        _partyList.Columns.Add("Class", 220);
        _partyList.Columns.Add("AC", 80);
        _partyList.Columns.Add("Hits", 100);
        _partyList.Columns.Add("Status", 320);//This was 280, but I increased it to 320 to accommodate the new status

        Controls.Add(_headerLabel);
        Controls.Add(_optionsTitleLabel);
        Controls.Add(_optionsLegendLabel);
        Controls.Add(_monsterPanel);
        Controls.Add(_partyList);

        KeyDown += EncounterForm_KeyDown;

        _currentIndex = FindNextActionableIndex(-1);
        UpdateHeader();
        UpdatePartyList();

        if (monsterTemplate != null && monsterTemplate.Source == Sources.Wizardry)
        {
            _wizardryStyle = true;
            ApplyWizardryLayout();
        }
    }

    // Constructor for multiple group encounters
    public EncounterForm(CombatSession session, int? dungeonLevel = null)
    {
        _session = session;
        _singleMonsterTemplate = null;
        _multipleGroups = true;
        _party = session.Party;
        _roundNumber = session.RoundNumber;

        var groups = session.GetDistinctGroupIds().ToList();

        // Första två grupper (eller färre)
        var groupDescriptions = groups.Take(2).Select(groupId =>
        {
            var monstersInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
            if (monstersInGroup.Count == 0)
                return null;

            var name = monstersInGroup.First().Name;
            var count = monstersInGroup.Count;
            var asleepCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Asleep));
            var entangledCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Entangled));
            var panickedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Panicked));
            var fearedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Feared));
            var turnedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.TurnedUndead));
            var blindedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Blinded));
            var confusedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Confused));
            var stunnedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Stunned));
            var slowedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Slowed));
            var paralyzedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
            var unconsciousCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Unconscious));

            bool useWizSuffix = ShouldUseWizardrySuffix(monstersInGroup.First().Template);
            var image = TryLoadMonsterImage(name, dungeonLevel, useWizSuffix);
            _monsterImages.Add((name, image));

            var statusBits = new List<string>();
            if (asleepCount > 0) statusBits.Add($"{asleepCount} asleep");
            if (unconsciousCount > 0) statusBits.Add($"{unconsciousCount} unconscious");
            if (entangledCount > 0) statusBits.Add($"{entangledCount} entangled");
            if (panickedCount > 0) statusBits.Add($"{panickedCount} panicked");
            if (fearedCount > 0) statusBits.Add($"{fearedCount} feared");
            if (turnedCount > 0) statusBits.Add($"{turnedCount} turned");
            if (blindedCount > 0) statusBits.Add($"{blindedCount} blinded");
            if (confusedCount > 0) statusBits.Add($"{confusedCount} confused");
            if (stunnedCount > 0) statusBits.Add($"{stunnedCount} stunned");
            if (slowedCount > 0) statusBits.Add($"{slowedCount} slowed");
            if (paralyzedCount > 0) statusBits.Add($"{paralyzedCount} paralyzed");

            var statusText = statusBits.Count > 0 ? $" ({string.Join(", ", statusBits)})" : string.Empty;
            var displayName = count > 1 ? name + "s" : name;
            return $"{count} {displayName}{statusText}";
        }).Where(d => d != null).ToList();

        // Om det finns 3 eller 4 grupper, skapa en separat lista för de senare
        List<string> groupDescriptions2 = new();
        if (groups.Count >= 3)
        {
            groupDescriptions2 = groups.Skip(2).Select(groupId =>
            {
                var monstersInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
                if (monstersInGroup.Count == 0)
                    return null;

                var name = monstersInGroup.First().Name;
                var count = monstersInGroup.Count;
                bool useWizSuffix = ShouldUseWizardrySuffix(monstersInGroup.First().Template);
                var image = TryLoadMonsterImage(name, dungeonLevel, useWizSuffix);
                _monsterImages.Add((name, image));

                var statusBits = new List<string>();
                var asleepCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Asleep));
                var paralyzedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
                var unconsciousCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Unconscious));
                if (asleepCount > 0) statusBits.Add($"{asleepCount} asleep");
                if (paralyzedCount > 0) statusBits.Add($"{paralyzedCount} paralyzed");
                if (unconsciousCount > 0) statusBits.Add($"{unconsciousCount} unconscious");

                var statusText = statusBits.Count > 0 ? $" ({string.Join(", ", statusBits)})" : string.Empty;
                var displayName = count > 1 ? name + "s" : name;
                return $"{count} {displayName}{statusText}";
            }).Where(d => d != null).ToList();
        }
        /*
        var groups = session.GetDistinctGroupIds().ToList();
        var groupDescriptions = groups.Select(groupId =>
        {
            var monstersInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
            if (monstersInGroup.Count == 0)
                return null;
            var name = monstersInGroup.First().Name;
            var count = monstersInGroup.Count;
            var asleepCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Asleep));
            var heldCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
            var entangledCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Entangled));
            var panickedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Panicked));
            var fearedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Feared));
            var turnedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.TurnedUndead));
            var blindedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Blinded));
            var confusedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Confused));
            var stunnedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Stunned));
            var slowedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Slowed));
            var paralyzedCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Paralyzed));
            var unconsciousCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Unconscious));

            // Check if we should use Wizardry suffix
            bool useWizSuffix = ShouldUseWizardrySuffix(monstersInGroup.First().Template);

            // Load image for this monster type
            var image = TryLoadMonsterImage(name, dungeonLevel, useWizSuffix);
            _monsterImages.Add((name, image));

            var statusBits = new List<string>();
            if (asleepCount > 0)
                statusBits.Add($"{asleepCount} asleep");
            if (heldCount > 0)
                statusBits.Add($"{heldCount} held");
            if (unconsciousCount > 0)
                statusBits.Add($"{unconsciousCount} unconscious");
            if (entangledCount > 0)
                statusBits.Add($"{entangledCount} entangled");
            if (panickedCount > 0)
                statusBits.Add($"{panickedCount} panicked");
            if (fearedCount > 0)
                statusBits.Add($"{fearedCount} feared");
            if (turnedCount > 0)
                statusBits.Add($"{turnedCount} turned");
            if (blindedCount > 0)
                statusBits.Add($"{blindedCount} blinded");
            if (confusedCount > 0)
                statusBits.Add($"{confusedCount} confused");
            if (stunnedCount > 0)
                statusBits.Add($"{stunnedCount} stunned");
            if (slowedCount > 0)
                statusBits.Add($"{slowedCount} slowed");
            if (paralyzedCount > 0)
                statusBits.Add($"{paralyzedCount} paralyzed");

                var statusText = statusBits.Count > 0 ? $" ({string.Join(", ", statusBits)})" : string.Empty;
            return $"{count} {name}{statusText}";
        }).Where(d => d != null).ToList();
        */
        var groupsLine1 = string.Join(" and ", groupDescriptions);
        var groupsLine2 = string.Join(" and ", groupDescriptions2);
        _monsterName = string.IsNullOrWhiteSpace(groupsLine2)
            ? groupsLine1
            : groupsLine1 + Environment.NewLine + groupsLine2;
        _monsterCount = session.AliveMonsters.Count();
        _asleepMonsterCount = session.AliveMonsters.Count(m => m.HasStatus(MonsterStatus.Asleep));
        //_unconsciousMonsterCount = session.AliveMonsters.Count(m => m.HasStatus(MonsterStatus.Unconscious));

        Text = "Encounter";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 900);//Multi encounter: This was 620, but I increased it to 900 to accommodate the new monster status counts and made the monster panel taller to fit the image.
        BackColor = Color.Black;
        ForeColor = GameRulesProvider.Current.DefaultColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        _headerLabel = new Label
        {
            Left = 16,
            Top = 12,
            Width = 940,
            Height = 96,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.TopLeft
        };

        _optionsTitleLabel = new Label//For exmaple ROBERT'S OPTIONS
        {
            Left = 16,
            Top = 114,//Used to be 64 moving it down to make room for the new monster status counts
            Width = 940,
            Height = 40,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _optionsLegendLabel = new Label//For example F)IGHT U)SE ITEM R)UN S)PELL P)ARRY T)AKE BACK G)ROUP (Select Target Group)
        {
            Left = 16,
            Top = 158,//Used to be 108, but I moved it down to make room for the new monster status counts
            Width = 940,
            Height = 56,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Black,
            Font = new Font("Consolas", 16f, FontStyle.Bold),
            Text = "F)IGHT   U)SE ITEM   R)UN\nS)PELL   P)ARRY      T)AKE BACK   G)ROUP   I)NFO",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _monsterPanel = new Panel
        {
            Left = 16,
            Top = 222,//Used to be 172, but I moved it down to make room for the new monster status counts
            Width = 940,
            Height = 430,//Slightly reduced so the party table can be taller without overlap.
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle
        };
        _monsterPanel.Paint += MonsterPanel_Paint;

        _partyList = new ListView
        {
            Left = 16,
            Top = 660,//Move up and enlarge so 6 party members fit without scrolling.
            Width = 940,
            Height = 220,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        /*
        _monsterPanel = new Panel
        {
            Left = 16,
            Top = 172,
            Width = 940,
            Height = 180,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle
        };
        _monsterPanel.Paint += MonsterPanel_Paint;

        _partyList = new ListView
        {
            Left = 16,
            Top = 362,
            Width = 940,
            Height = 236,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };
        */

        _partyList.Columns.Add("#", 50);
        _partyList.Columns.Add("Character Name", 220);
        _partyList.Columns.Add("Class", 160);
        _partyList.Columns.Add("AC", 80);
        _partyList.Columns.Add("Hits", 100);
        _partyList.Columns.Add("Status", 280);

        Controls.Add(_headerLabel);
        Controls.Add(_optionsTitleLabel);
        Controls.Add(_optionsLegendLabel);
        Controls.Add(_monsterPanel);
        Controls.Add(_partyList);

        KeyDown += EncounterForm_KeyDown;

        _currentIndex = FindNextActionableIndex(-1);
        UpdateHeader();
        UpdatePartyList();

        if (session.AliveMonsters.Any() && session.AliveMonsters.All(m => m.Template.Source == Sources.Wizardry))
        {
            _wizardryStyle = true;
            ApplyWizardryLayout();
        }
    }

    /// <summary>
    /// A command from the tabletop viewer, replayed as the key it stands for -- including the rank
    /// rules below, so choosing Fight for a back-rank character is refused the same way and with the
    /// same dialog as it is from the keyboard.
    /// </summary>
    internal void InjectViewerKey(Keys key) => EncounterForm_KeyDown(this, new KeyEventArgs(key));

    /// <summary>
    /// Viewer hooks live here rather than in a constructor for two reasons: this form has two
    /// constructors and both need them, and the first prompt has to go out AFTER whoever opened the
    /// dialog has had a chance to subscribe -- UpdateHeader already ran during construction, when
    /// there was still nobody listening.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_chestMode && _pickNames != null)
        {
            var vocab = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase) { ["back"] = Keys.B };
            if (_pickAllowLeave)
                vocab["leave"] = Keys.L;
            var pickOptions = new List<ViewerPromptOption>();
            for (var i = 0; i < _pickNames.Count; i++)
            {
                vocab[$"pick:{i + 1}"] = Keys.D1 + i;
                pickOptions.Add(new ViewerPromptOption($"pick:{i + 1}", _pickNames[i]));
            }

            pickOptions.Add(new ViewerPromptOption("back", "Back"));
            if (_pickAllowLeave)
                pickOptions.Add(new ViewerPromptOption("leave", "Leave"));

            _viewerControl = ViewerControlPump.Start(this, vocab, HandleChestKey);
            ViewerPromptChanged?.Invoke(new ViewerPrompt("choice", _pickQuestion, null, pickOptions));
            return;
        }

        if (_chestMode && _noticeMode)
        {
            _viewerControl = ViewerControlPump.Start(this,
                new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase) { ["continue"] = Keys.Enter }, HandleChestKey);
            ViewerPromptChanged?.Invoke(new ViewerPrompt("choice", "Treasure found.", null,
                new[] { new ViewerPromptOption("continue", "Continue") }));
            return;
        }

        if (_chestMode)
        {
            _viewerControl = ViewerControlPump.Start(this, Adnd.Game.Combat.LairTreasureChestDialog.Vocabulary, HandleChestKey);
            ViewerPromptChanged?.Invoke(new ViewerPrompt(
                "choice",
                "Treasure chest: O)pen, C)ast Find Traps, L)eave, I)nspect, B)ack",
                null,
                new[]
                {
                    new ViewerPromptOption("open", "Open"),
                    new ViewerPromptOption("cast", "Cast Find Traps"),
                    new ViewerPromptOption("leave", "Leave"),
                    new ViewerPromptOption("inspect", "Inspect"),
                    new ViewerPromptOption("back", "Back")
                }));
            return;
        }

        if (_resultLines != null)
        {
            StartResultPlayback();
            return;
        }

        if (_session != null && _session.RoundNumber == 1 && _session.PartySurprisedRound1)
        {
            if (!_wizardryStyle)
                ShowTimedSurpriseDialog(this, "SURPRISE", "PARTY SURPRISED", 3000);

            foreach (var member in _party)
            {
                if (IsActionable(member))
                    _actions[member.Name] = CombatAction.OfType(CombatActionType.Parry);
            }

            if (_wizardryStyle)
            {
                _resultLines = new List<string> { "The party is surprised!" };
                StartResultPlayback();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_session != null && _session.RoundNumber == 1 && _session.MonstersSurprisedRound1)
        {
            if (_wizardryStyle)
            {
                _resultLines = new List<string> { "The monsters are surprised!" };
                _afterResults = () =>
                {
                    _resultLines = null;
                    UpdateHeader();
                    StartViewerPump();
                };
                StartResultPlayback();
                return;
            }

            ShowTimedSurpriseDialog(this, "SURPRISE", "MONSTERS SURPRISED", 3000);
        }

        StartViewerPump();
    }

    private Action? _afterResults;

    private void StartViewerPump()
    {
        _viewerControl = ViewerControlPump.Start(this, ViewerCommands.Combat, InjectViewerKey, HandleViewerCommand);
        ViewerPromptChanged?.Invoke(BuildViewerPrompt());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the encounter before actions are completed is treated as a run attempt.
        // Ask for confirmation for Escape, title-bar close, Alt+F4, etc.
        if (!e.Cancel && DialogResult != DialogResult.OK)
        {
            var confirm = MessageBox.Show(
                this,
                "Closing combat now counts as trying to run away. Do you want to run away?",
                "Run away?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            DialogResult = DialogResult.Cancel;
        }

        base.OnFormClosing(e);
    }

    // Wizardry monsters get the Proving Grounds look: framed panels and a small monster over the maze view.
    private bool _wizardryStyle;
    private List<string>? _resultLines;
    private int _resultIndex;
    private System.Windows.Forms.Timer? _resultTimer;

    internal bool IsWizardryStyle => _wizardryStyle;

    /// <summary>Shows the round's results one line at a time inside this form: every second, or sooner on any key.</summary>
    internal void PlayResults(IWin32Window owner, IEnumerable<string> lines, int extraHoldOnLastLineMs = 0)
    {
        _lastLineExtraMs = extraHoldOnLastLineMs;
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        if (list.Count == 0)
            return;

        _resultLines = list;
        _resultIndex = 0;
        DialogResult = DialogResult.None;
        UpdatePartyList();
        ShowDialog(owner);
        _resultLines = null;
    }

    private int _lastLineExtraMs;

    private void ShowResultLine()
    {
        if (_resultLines == null)
            return;

        if (_resultIndex >= _resultLines.Count)
        {
            _resultTimer?.Stop();
            if (_afterResults != null)
            {
                var after = _afterResults;
                _afterResults = null;
                after();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _optionsTitleLabel.Text = string.Empty;
        _optionsLegendLabel.Text = _resultLines[_resultIndex++].ToUpperInvariant();
        UpdatePartyList();
        if (_resultTimer != null)
        {
            var baseDelay = Math.Max(1, GameRulesProvider.Current.DelayInbetweenCombatMessages);
            _resultTimer.Interval = _resultIndex >= _resultLines.Count ? baseDelay + Math.Max(0, _lastLineExtraMs) : baseDelay;
        }

        _resultTimer?.Stop();
        _resultTimer?.Start();
    }

    private void StartResultPlayback()
    {
        _optionsTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsLegendLabel.TextAlign = ContentAlignment.MiddleCenter;
        _resultTimer ??= new System.Windows.Forms.Timer();
        _resultTimer.Interval = Math.Max(1, GameRulesProvider.Current.DelayInbetweenCombatMessages);
        _resultTimer.Tick -= ResultTimer_Tick;
        _resultTimer.Tick += ResultTimer_Tick;
        ShowResultLine();
    }

    private void ResultTimer_Tick(object? sender, EventArgs e) => ShowResultLine();

    private sealed class WizardryFrame : Panel
    {
        public WizardryFrame()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            OldStyleFrame.Draw(e.Graphics, ClientRectangle, GameRulesProvider.Current.DefaultColor);
        }
    }

    private void ApplyWizardryLayout()
    {
        const int margin = 8;
        const int gap = 6;
        const int inner = 12;
        var width = 980;
        var frameWidth = width - 2 * margin;
        var y = margin;

        WizardryFrame NewFrame(int height)
        {
            var frame = new WizardryFrame { Left = margin, Top = y, Width = frameWidth, Height = height };
            Controls.Add(frame);
            y += height + gap;
            return frame;
        }

        void Place(Control control, Control parent, int left, int top, int w, int h)
        {
            parent.Controls.Add(control);
            control.SetBounds(left, top, w, h);
        }

        Controls.Clear();

        var headerFrame = NewFrame(_multipleGroups ? 110 : 64);
        _headerLabel.TextAlign = ContentAlignment.MiddleLeft;
        Place(_headerLabel, headerFrame, inner, 8, frameWidth - 2 * inner, headerFrame.Height - 16);

        var optionsFrame = NewFrame(128);
        Place(_optionsTitleLabel, optionsFrame, inner, 8, frameWidth - 2 * inner, 36);
        Place(_optionsLegendLabel, optionsFrame, inner, 48, frameWidth - 2 * inner, 72);

        var stripFrame = NewFrame(176);
        _monsterPanel.BorderStyle = BorderStyle.None;
        Place(_monsterPanel, stripFrame, OldStyleFrame.Thickness, OldStyleFrame.Thickness,
            frameWidth - 2 * OldStyleFrame.Thickness, stripFrame.Height - 2 * OldStyleFrame.Thickness);

        var partyFrame = NewFrame(236);
        Place(_partyList, partyFrame, inner, inner, frameWidth - 2 * inner, partyFrame.Height - 2 * inner);

        ClientSize = new Size(width, y + margin - gap);
    }

    // One corridor frame: the nearest wall seen straight on, like the Proving Grounds view.
    internal static void DrawCorridorFrame(Graphics g, Rectangle rect)
    {
        using var pen = new Pen(GameRulesProvider.Current.DefaultColor, 2f);
        var outer = new Rectangle(rect.Left + 6, rect.Top + 4, rect.Width - 12, rect.Height - 8);
        var insetX = outer.Width / 4;
        var insetY = outer.Height / 5;
        var back = new Rectangle(outer.Left + insetX, outer.Top + insetY, outer.Width - 2 * insetX, outer.Height - 2 * insetY);
        g.DrawRectangle(pen, back);
        g.DrawLine(pen, outer.Left, outer.Top, back.Left, back.Top);
        g.DrawLine(pen, outer.Right, outer.Top, back.Right, back.Top);
        g.DrawLine(pen, outer.Left, outer.Bottom, back.Left, back.Bottom);
        g.DrawLine(pen, outer.Right, outer.Bottom, back.Right, back.Bottom);
    }

    private void PaintWizardryStrip(Graphics g, Rectangle rect)
    {
        g.Clear(Color.Black);
        DrawCorridorFrame(g, rect);

        if (_chestMode || _heldImage != null)
        {
            var picture = _chestMode ? _chestImage : _heldImage;
            if (picture != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                var scale = Math.Min(64f / picture.Height, 160f / picture.Width);
                var w = (int)(picture.Width * scale);
                var h = (int)(picture.Height * scale);
                g.DrawImage(picture, new Rectangle(rect.Left + (rect.Width - w) / 2, rect.Top + (rect.Height - h) / 2 + 20, w, h));
            }

            return;
        }

        var images = _monsterImages.Count > 0
            ? _monsterImages.Select(m => m.Image).Where(i => i != null).Cast<Image>().ToList()
            : (_monsterImage != null ? new List<Image> { _monsterImage } : new List<Image>());
        if (images.Count == 0)
            return;

        const int maxHeight = 120;
        var slotWidth = rect.Width / images.Count;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var scale = Math.Min((slotWidth - 20) / (float)image.Width, Math.Min(maxHeight, rect.Height - 16) / (float)image.Height);
            var w = (int)(image.Width * scale);
            var h = (int)(image.Height * scale);
            var x = rect.Left + i * slotWidth + (slotWidth - w) / 2;
            var yPos = rect.Top + (rect.Height - h) / 2;
            g.DrawImage(image, new Rectangle(x, yPos, w, h));
        }
    }

    private static void ShowTimedSurpriseDialog(IWin32Window owner, string titleText, string messageText, int autoCloseMilliseconds)
    {
        using var form = new Form
        {
            Text = titleText,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(520, 120),
        };

        var framePanel = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = titleText,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 0,
            Top = 48,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = messageText,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var closeTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, autoCloseMilliseconds) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            form.Close();
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                closeTimer.Stop();
                form.Close();
            }
        };

        form.Shown += (_, _) => closeTimer.Start();
        form.FormClosed += (_, _) => closeTimer.Dispose();

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);
        form.ShowDialog(owner);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _viewerControl?.Dispose();
        _resultTimer?.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Raised whenever the fight starts waiting on a different choice, so the table can redraw. An
    /// observer, like the other viewer hooks: nothing here waits for it and nothing changes course if
    /// no one is listening. Null means there is nothing to ask -- every character is down.
    /// </summary>
    internal event Action<ViewerPrompt?>? ViewerPromptChanged;

    /// <summary>
    /// The open choice, as the viewer should present it. Uses the same rank test the on-screen legend
    /// uses, so the table offers exactly what this form would accept and no more.
    ///
    /// Enter and Escape are left out on purpose: Enter only duplicates Fight or Parry depending on
    /// rank, and Escape abandons the encounter, which is not something to put under a stray click.
    /// Both still work if a viewer sends them.
    /// </summary>
    /// <summary>Marks a command that names the monster to hit, e.g. "fightAt:default#3".</summary>
    private const string FightAtPrefix = "fightAt:";

    /// <summary>
    /// Commands the viewer sends that are not keys: aiming at a particular monster, and answering for this
    /// character automatically. Reached through the pump's raw handler -- see the note on its `handle`
    /// parameter for why a target cannot be a keystroke.
    ///
    /// Unknown words are ignored, exactly as the key map ignores them, so a newer viewer offering something
    /// this build has never heard of does nothing rather than throwing.
    /// </summary>
    internal void HandleViewerCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        if (_currentIndex >= 0 && _currentIndex < _party.Count && _session?.IsSwallowed(_party[_currentIndex].Name) == true
            && command != "info" && !command.StartsWith(FightAtPrefix, StringComparison.Ordinal))
            return;

        if (string.Equals(command, "info", StringComparison.OrdinalIgnoreCase))
        {
            ShowEncounterInfoWindow();
            return;
        }

        if (string.Equals(command, "monkBodyHeal", StringComparison.OrdinalIgnoreCase))
        {
            ChooseMonkBodyHealAction();
            return;
        }

        if (string.Equals(command, "bardCharm", StringComparison.OrdinalIgnoreCase))
        {
            ChooseBardCharmAction();
            return;
        }

        // Auto has no branch here on purpose: it answers to Enter, which this form's own key handler already
        // understands, so the pump injects the key and the game decides. See the note on Enter in
        // EncounterForm_KeyDown -- one path for both surfaces, rather than a table-only shortcut that could
        // drift away from what the keyboard does.

        if (command.StartsWith(FightAtPrefix, StringComparison.Ordinal))
            ChooseFight(monsterKey: command.Substring(FightAtPrefix.Length));
    }

    private ViewerPrompt? BuildViewerPrompt()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return null;

        var character = _party[_currentIndex];
        if (_session != null && _session.SwallowedCharacters.TryGetValue(character.Name, out var swallowed))
            return new ViewerPrompt("combat",
                $"{character.Name} is inside {swallowed.Worm.DisplayName}: {swallowed.RoundsUntilDeath(_session.RoundNumber)} round(s) until death; inner AC 9, -{swallowed.RoundsInside(_session.RoundNumber)} damage per attack.",
                ViewerIds.Character(character.Name),
                new[] { new ViewerPromptOption("fight", "Cut from inside"), new ViewerPromptOption("confirm", "Auto: cut from inside"),
                    new ViewerPromptOption("parry", "Wait"), new ViewerPromptOption("info", "Info"), new ViewerPromptOption("undo", "Back one character") });
        var isLycanthropeTransformed = character.HasStatus(CharacterStatus.LycanthropeTransformed);
        var rank = GetActionableRank(_currentIndex);
        var options = new List<ViewerPromptOption>();

        // Front rank can always fight. Back rank can fight only with equipped ranged setup.
        if (rank is >= 1 and <= 3 || (rank is >= 4 and <= 6 && CanUseRanged(character)))
        {
            options.Add(new ViewerPromptOption("fight", rank is >= 4 and <= 6 ? "Shoot" : "Fight"));

            // Every living monster, as something to POINT AT rather than a button to read. An option with a
            // Target makes the viewer light that figure up and take the click there, so choosing who to hit
            // is done by touching them -- which is how it works on a table and needs no key. The keyboard
            // keeps plain Fight, which leaves the choice to the resolver as it always did.
            //
            // Only offered where the id can be honoured: a monster named here is looked up when the round
            // resolves, and the swing falls on the group if it has died in the meantime.
            if (_session != null)
            {
                foreach (var monster in _session.AliveMonsters)
                {
                    options.Add(new ViewerPromptOption(
                        FightAtPrefix + CombatAction.MonsterKey(monster.GroupId, monster.Index),
                        "Attack " + monster.DisplayName,
                        ViewerIds.Monster(monster.GroupId, monster.Index)));
                }

                // Auto, as a thing to point at rather than a key you have to know. It is the id the keyboard
                // already answers on Enter, so pressing the button and pressing the key are the same event.
                //
                // Offered whenever anything is alive, NOT only when there are several: gating the one key you
                // can lean on made it die in exactly the round you most want it -- the mop-up -- which is what
                // made it look broken rather than finished. The label says which of the two it will be, since
                // "spread the blows" is a promise that cannot be kept to a single survivor.
                if (_session.AliveMonsters.Any())
                    options.Add(new ViewerPromptOption(
                        "confirm",
                        _session.AliveMonsters.Count() > 1 ? "Auto: spread the blows" : "Auto: fight"));
            }
        }

        options.Add(new ViewerPromptOption("parry", "Parry"));

        // The same button for the back three; if no ranged setup, they auto-parry.
        if (rank is < 1 or > 3)
            options.Add(new ViewerPromptOption("confirm", "Auto: parry"));

        if (!isLycanthropeTransformed)
            options.Add(new ViewerPromptOption("spell", "Cast a spell"));
        if (!isLycanthropeTransformed && CanUseDispellUndead(character))
            options.Add(new ViewerPromptOption("dispellUndead", "Dispell undead"));
        if (!isLycanthropeTransformed && character.IsPaladin())
            options.Add(new ViewerPromptOption("layOnHands", character.LayOnHandsUsedToday ? "Lay on Hands (used today)" : "Lay on Hands"));
        if (!isLycanthropeTransformed && CanUseMonkBodyHeal(character))
            options.Add(new ViewerPromptOption("monkBodyHeal", character.MonkBodyHealUsedToday ? "Body Heal (used today)" : "Body Heal"));
        if (!isLycanthropeTransformed)
            options.Add(new ViewerPromptOption("useItem", "Use an item"));
        options.Add(new ViewerPromptOption("run", "Run"));
        options.Add(new ViewerPromptOption("info", "Info"));

        if (_multipleGroups && _session != null)
            options.Add(new ViewerPromptOption("targetGroup", "Choose target group"));

        options.Add(new ViewerPromptOption("undo", "Back one character"));

        return new ViewerPrompt(
            "combat",
            $"{character.Name} is choosing.",
            ViewerIds.Character(character.Name),
            options);
    }

    private bool _chestMode;
    private Image? _chestImage;
    private Adnd.Game.Combat.LairChestChoice _chestChoice = Adnd.Game.Combat.LairChestChoice.Back;

    /// <summary>Shows a treasure chest in the monster strip with the chest options above it, and returns the pick.</summary>
    internal Adnd.Game.Combat.LairChestChoice ShowChestChoice(IWin32Window owner, Image? chestImage)
    {
        _chestMode = true;
        _chestImage = chestImage;
        _chestChoice = Adnd.Game.Combat.LairChestChoice.Back;
        _optionsTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsLegendLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsTitleLabel.Text = "A CHEST!  YOU MAY:";
        _optionsLegendLabel.Text = "O)PEN   C)AST FIND TRAPS   L)EAVE ALONE" + Environment.NewLine + "I)NSPECT   B)ACK";
        _monsterPanel.Invalidate();
        DialogResult = DialogResult.None;
        ShowDialog(owner);
        _chestMode = false;
        _chestImage = null;
        _monsterPanel.Invalidate();
        return _chestChoice;
    }

    private bool _noticeMode;
    private IReadOnlyList<string>? _pickNames;
    private bool _pickAllowLeave;
    private string _pickQuestion = string.Empty;
    private int _pickIndex = -1;
    private bool _pickLeave;

    /// <summary>Asks "who will ...?" over the chest picture. Number picks a member; Enter or L leaves (when allowed); B or Esc goes back.</summary>
    internal (int Index, bool Leave) ShowMemberPick(IWin32Window owner, Image? image, string verb, IReadOnlyList<string> names, bool allowLeave)
    {
        _chestMode = true;
        _chestImage = image;
        _pickNames = names;
        _pickAllowLeave = allowLeave;
        _pickIndex = -1;
        _pickLeave = false;
        _pickQuestion = $"Who (#) will {verb.ToLowerInvariant()}?";
        _optionsTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsLegendLabel.TextAlign = ContentAlignment.MiddleCenter;
        var savedHeader = _headerLabel.Text;
        _headerLabel.Text = string.Empty;
        _optionsTitleLabel.Text = string.Empty;
        _optionsLegendLabel.Text = $"WHO (#) WILL {verb.ToUpperInvariant()} ?" + Environment.NewLine + "↵ TO LEAVE";
        _monsterPanel.Invalidate();
        DialogResult = DialogResult.None;
        ShowDialog(owner);
        _headerLabel.Text = savedHeader;
        _pickNames = null;
        _chestMode = false;
        _chestImage = null;
        _monsterPanel.Invalidate();
        return (_pickIndex, _pickLeave);
    }

    /// <summary>Shows a picture (the treasure found) in the monster strip until Enter or Escape.</summary>
    internal void ShowPictureNotice(IWin32Window owner, Image? image, string title, string legend)
    {
        _noticeMode = true;
        _chestMode = true;
        _chestImage = image;
        _optionsTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsLegendLabel.TextAlign = ContentAlignment.MiddleCenter;
        _optionsTitleLabel.Text = title;
        _optionsLegendLabel.Text = legend;
        _monsterPanel.Invalidate();
        DialogResult = DialogResult.None;
        ShowDialog(owner);
        _noticeMode = false;
        _chestMode = false;
        // Keep the picture in the strip for the results that follow.
        _heldImage?.Dispose();
        _heldImage = image != null ? new Bitmap(image) : null;
        _chestImage = null;
        _monsterPanel.Invalidate();
    }

    private Image? _heldImage;

    private void HandleChestKey(Keys key)
    {
        if (_pickNames != null)
        {
            if (key >= Keys.D1 && key <= Keys.D9 && key - Keys.D1 < _pickNames.Count)
                _pickIndex = key - Keys.D1;
            else if (key == Keys.L && _pickAllowLeave || key == Keys.Enter && _pickAllowLeave)
                _pickLeave = true;
            else if (key is not (Keys.B or Keys.Escape or Keys.Enter))
                return;

            _viewerControl?.Dispose();
            _viewerControl = null;
            ViewerPromptChanged?.Invoke(null);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_noticeMode)
        {
            if (key is not (Keys.Enter or Keys.Escape or Keys.Space))
                return;

            _viewerControl?.Dispose();
            _viewerControl = null;
            ViewerPromptChanged?.Invoke(null);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        Adnd.Game.Combat.LairChestChoice? choice = key switch
        {
            Keys.O => Adnd.Game.Combat.LairChestChoice.Open,
            Keys.C => Adnd.Game.Combat.LairChestChoice.CastFindTraps,
            Keys.L => Adnd.Game.Combat.LairChestChoice.LeaveAlone,
            Keys.I => Adnd.Game.Combat.LairChestChoice.Inspect,
            Keys.B or Keys.Escape => Adnd.Game.Combat.LairChestChoice.Back,
            _ => null
        };

        if (choice is null)
            return;

        _chestChoice = choice.Value;
        _viewerControl?.Dispose();
        _viewerControl = null;
        ViewerPromptChanged?.Invoke(null);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void EncounterForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_chestMode)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            HandleChestKey(e.KeyCode);
            return;
        }

        if (_resultLines != null)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
                _resultIndex = _resultLines.Count;
            ShowResultLine();
            return;
        }

        if (_currentIndex >= 0 && _currentIndex < _party.Count && _session?.IsSwallowed(_party[_currentIndex].Name) == true)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.Enter or Keys.F)
                ChooseFight();
            else if (e.KeyCode == Keys.P)
                ChooseAction(CombatActionType.Parry);
            else if (e.KeyCode == Keys.T)
                StepBack();
            else if (e.KeyCode == Keys.I)
                ShowEncounterInfoWindow();
            else
                SayOnBoth("Swallowed", "Fight to cut the worm from inside, parry to wait, or rescue this character by killing the worm.");
            return;
        }
        switch (e.KeyCode)
        {
            case Keys.Escape:
                DialogResult = DialogResult.Cancel;
                Close();
                break;
            case Keys.Enter:
            {
                // Enter means Fight for chars 1-3, Parry for chars 4-6 -- the one key that always answers,
                // so a fight the party is winning can be played by holding it. It spends nothing: never an
                // item, never a memorised spell, since a spell held back is often why a party survives the
                // next round.
                //
                // Swinging goes through ChooseFight rather than ChooseAction so that Enter never STOPS TO
                // ASK. ChooseAction puts up a modal group picker in a fight against several groups, which
                // turned "the obvious action" into a question -- and a question the table could only answer
                // by accident, since the picker arrived unbidden at the start of a round. Spread rather than
                // plain, because sharing the blows around is what "get on with it" should mean with several
                // monsters standing; with one left there is nothing to spread and it simply hits that one.
                var rank = GetActionableRank(_currentIndex);
                if (rank is >= 1 and <= 3 || (rank is >= 4 and <= 6 && CanUseRanged(_party[_currentIndex])))
                {
                    ChooseFight(spread: true);
                }
                else if (rank is >= 4 and <= 6)
                {
                    ChooseAction(CombatActionType.Parry);
                }
                break;
            }
            case Keys.F:
            {
                var rank = GetActionableRank(_currentIndex);
                if (rank is >= 1 and <= 3 || (rank is >= 4 and <= 6 && CanUseRanged(_party[_currentIndex])))
                    ChooseAction(CombatActionType.Fight);
                else
                    SayOnBoth("Action not allowed", "Rows 4-6 need equipped ranged weapon and ammo to fight.");
                break;
            }
            case Keys.P:
                ChooseAction(CombatActionType.Parry);
                break;
            case Keys.T:
                StepBack();
                break;
            case Keys.U:
                ChooseUseItemAction();
                break;
            case Keys.C:
                ChooseBardCharmAction();
                break;
            case Keys.R:
                ChooseAction(CombatActionType.Run);
                break;
            case Keys.S:
                ChooseSpellAction();
                break;
            case Keys.I:
                ShowEncounterInfoWindow();
                break;
            case Keys.D:
                ChooseDispellUndeadAction();
                break;
            case Keys.L:
                ChooseLayOnHandsAction();
                break;
            case Keys.H:
                ChooseMonkBodyHealAction();
                break;
            case Keys.G:
                if (_multipleGroups && _session != null)
                    ChooseTargetGroup();
                break;
        }
    }

    /// <summary>
    /// Fight, with a monster named or the blows spread about.
    ///
    /// Deliberately NOT routed through <see cref="ChooseAction"/>, which for a fight against several groups
    /// stops and asks which group with a modal dialog. That question is already answered here: pointing at a
    /// monster says which group as surely as naming it does, and spreading means all of them on purpose. Going
    /// through ChooseAction put a picker on screen that the table cannot answer, so an attack sent from the
    /// viewer hung the fight behind a dialog -- the exact trap this whole exercise is about.
    ///
    /// What it does keep is the rank rule and the two lines that matter: store the action under the
    /// character's name, then move to the next actor.
    /// </summary>
    private void ChooseFight(string? monsterKey = null, bool spread = false)
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count) return;

        var character = _party[_currentIndex];
        if (!IsActionable(character)) return;
        if (_session != null && _session.SwallowedCharacters.TryGetValue(character.Name, out var swallowed))
        {
            _actions[character.Name] = new CombatAction { Type = CombatActionType.Fight,
                TargetGroupId = swallowed.Worm.GroupId, TargetMonsterId = CombatAction.MonsterKey(swallowed.Worm.GroupId, swallowed.Worm.Index) };
            AdvanceActor();
            return;
        }

        var rank = GetActionableRank(_currentIndex);
        if (rank is < 1 or > 3)
        {
            if (!CanUseRanged(character))
                return;
        }

        var action = CombatAction.OfType(CombatActionType.Fight);
        action.SpreadTargets = spread;

        if (!string.IsNullOrEmpty(monsterKey))
        {
            action.TargetMonsterId = monsterKey;

            // Keep the group in step with the monster, since the group is what the resolver falls back to if
            // this one is dead by the time the round plays out.
            var monster = _session?.FindMonster(monsterKey);
            if (monster != null) action.TargetGroupId = monster.GroupId;
        }

        // Spread leaves the group unset on purpose: the point is to share the blows around everything that
        // is still standing, not to share them around inside one group.

        _actions[character.Name] = action;
        AdvanceActor();
    }

    private static bool CanUseRanged(Character character)
    {
        if (!character.Equipment.TryGetValue(Adnd.Core.Items.EquipmentSlot.Range, out var ranged)
            || ranged == null)
            return false;

        var looksRanged = !string.IsNullOrWhiteSpace(ranged.Range)
                          || !string.IsNullOrWhiteSpace(ranged.FireRate)
                          || ranged.RequiresAmmo
                          || !string.IsNullOrWhiteSpace(ranged.AmmoType);
        if (!looksRanged)
            return false;

        if (!ranged.RequiresAmmo)
            return true;

        if (!character.Equipment.TryGetValue(Adnd.Core.Items.EquipmentSlot.Ammo, out var ammo)
            || ammo == null)
            return false;

        if (!string.IsNullOrWhiteSpace(ranged.AmmoType)
            && !string.Equals(ranged.AmmoType, ammo.AmmoType, StringComparison.OrdinalIgnoreCase))
            return false;

        return ammo.Quantity > 0;
    }

    private void ChooseDispellUndeadAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var caster = _party[_currentIndex];
        if (!IsActionable(caster))
            return;

        if (!CanUseDispellUndead(caster))
        {
            SayOnBoth("Dispell Undead", $"{caster.Name} cannot dispell undead.");
            return;
        }

        var combatAction = CombatAction.OfType(CombatActionType.DispellUndead);

        if (_multipleGroups && _session != null)
        {
            var groups = _session.GetDistinctGroupIds()
                .Where(g => _session.GetAliveCountByGroup(g) > 0)
                .ToList();

            if (groups.Count > 1)
            {
                var targetGroupId = PromptGroupSelection(caster);
                if (targetGroupId == null)
                    return;

                combatAction.TargetGroupId = targetGroupId;
            }
            else if (groups.Count == 1)
            {
                combatAction.TargetGroupId = groups[0];
            }
        }

        _actions[caster.Name] = combatAction;
        AdvanceActor();
    }

    private void ChooseBardCharmAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var bard = _party[_currentIndex];
        if (!IsActionable(bard))
            return;

        if (!bard.IsBard())
        {
            SayOnBoth("Bard Charm", $"{bard.Name} is not a bard.");
            return;
        }

        var combatAction = CombatAction.OfType(CombatActionType.BardCharm);

        if (_multipleGroups && _session != null)
        {
            var targetGroupId = PromptGroupSelection(bard);
            if (targetGroupId == null)
                return;
            combatAction.TargetGroupId = targetGroupId;
        }

        _actions[bard.Name] = combatAction;
        AdvanceActor();
    }

    private void ChooseUseItemAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var user = _party[_currentIndex];
        if (!IsActionable(user))
            return;

        var allSpells = _spellRepo.LoadAll();
        var usable = user.Inventory
            .Select((item, index) => new
            {
                item,
                index,
                spell = ResolveItemSpell(item, allSpells),
                grantsRegeneration = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Regeneration"),
                grantsHumanControlPotion = Adnd.Core.Items.ItemSpecialAbilityParser.HasCastsAbility(item, "Human Control"),
                grantsMammalControlRing = string.Equals(item.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase),
                grantsTalismanPureGood = Adnd.Core.Items.ItemSpecialAbilityParser.HasSpecialAbility(item, "Talisman of Pure Good"),
                grantsHornOfValhalla = Adnd.Core.Items.HornOfValhalla.IsHornOfValhalla(item),
                grantsWandOfFear = string.Equals(item.Name, "Wand of Fear", StringComparison.OrdinalIgnoreCase)
            })
            .Where(x => x.spell != null || x.grantsRegeneration || x.grantsHumanControlPotion || x.grantsMammalControlRing || x.grantsTalismanPureGood || x.grantsHornOfValhalla || x.grantsWandOfFear)
            .ToList();

        var hasEquippedMammalControlRing =
            (user.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1)
             && ring1 != null
             && string.Equals(ring1.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase))
            ||
            (user.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2)
             && ring2 != null
             && string.Equals(ring2.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase));

        if (usable.Count == 0 && !hasEquippedMammalControlRing)
        {
            SayOnBoth("Use Item", $"{user.Name} has no usable magical item.");
            return;
        }

        foreach (var entry in usable.Where(u => u.grantsHumanControlPotion))
            Adnd.Core.Items.PotionOfHumanControl.EnsureAssignedControlRoll(
                entry.item,
                sides => Random.Shared.Next(1, Math.Max(1, sides) + 1));

        var options = usable.Select(x => new
        {
            label = x.spell != null
                ? $"{x.item.Name} (casts {x.spell!.Name})"
                : x.grantsWandOfFear
                    ? $"{x.item.Name} (panics monsters)"
                : x.grantsTalismanPureGood
                    ? $"{x.item.Name} (banish evil cleric)"
                : x.grantsHumanControlPotion
                    ? $"{x.item.Name} (controls {Adnd.Core.Items.PotionOfHumanControl.GetDisplayDescriptor(x.item)})"
                : x.grantsHornOfValhalla
                    ? $"{x.item.Name} (summon berserkers)"
                : x.grantsMammalControlRing
                    ? $"{x.item.Name} (controls mammals)"
                    : $"{x.item.Name} (grants regeneration)",
            payload = x,
            equippedRing = false
        }).ToList();

        if (hasEquippedMammalControlRing)
        {
            options.Add(new
            {
                label = "Ring of Mammal Control (equipped; controls mammals)",
                payload = usable.FirstOrDefault(),
                equippedRing = true
            });
        }

        var lines = string.Join(Environment.NewLine, options.Select((x, i) => $"{i + 1}. {x.label}"));
        var selected = PromptForNumber("Use Item", $"{user.Name} - choose item:{Environment.NewLine}{Environment.NewLine}{lines}", 1, options.Count);
        if (!selected.HasValue)
            return;

        var picked = options[selected.Value - 1];
        if (picked.equippedRing)
        {
            _actions[user.Name] = new CombatAction
            {
                Type = CombatActionType.UseItem,
                SpellId = "__ring_mammal_control__",
                Target = null
            };

            AdvanceActor();
            return;
        }

        var chosen = picked.payload!;
        var spell = chosen.spell;

        SpellCastTarget? target;
        if (chosen.item.Type == ItemType.Potion || (spell != null && spell.RangeType == SpellRangeType.Self))
        {
            target = SpellCastTarget.Ally(user);
        }
        else if (spell != null && spell.RangeType == SpellRangeType.Ally)
        {
            var ally = PromptAllyTarget(spell);
            if (ally == null)
                return;
            target = SpellCastTarget.Ally(ally);
        }
        else if (chosen.grantsTalismanPureGood || chosen.grantsWandOfFear)
        {
            if (_multipleGroups && _session != null)
            {
                var targetGroupId = PromptGroupSelection(user);
                if (targetGroupId == null)
                    return;

                target = SpellCastTarget.EnemyGroup(targetGroupId);
            }
            else
            {
                target = SpellCastTarget.EnemyGroup("default");
            }
        }
        else if (spell == null)
        {
            target = null;
        }
        else
        {
            if (spell.TargetingScope == SpellTargetingScope.SingleTarget)
            {
                if (_multipleGroups && _session != null)
                {
                    var targetGroupId = PromptGroupSelection(user);
                    if (targetGroupId == null)
                        return;
                    target = SpellCastTarget.EnemyGroup(targetGroupId);
                }
                else
                {
                    target = SpellCastTarget.EnemyGroup("default");
                }
            }
            else if (spell.TargetingScope == SpellTargetingScope.SingleGroup)
            {
                if (_multipleGroups && _session != null)
                {
                    var groups = _session.GetDistinctGroupIds()
                        .Where(g => _session.GetAliveCountByGroup(g) > 0)
                        .ToList();

                    if (groups.Count > 1)
                    {
                        var targetGroupId = PromptGroupSelection(user);
                        if (targetGroupId == null)
                            return;
                        target = SpellCastTarget.EnemyGroup(targetGroupId);
                    }
                    else
                    {
                        target = SpellCastTarget.EnemyGroup(groups.FirstOrDefault() ?? "default");
                    }
                }
                else
                {
                    target = SpellCastTarget.EnemyGroup("default");
                }
            }
            else
            {
                target = null;
            }
        }

        _actions[user.Name] = new CombatAction
        {
            Type = CombatActionType.UseItem,
            ItemInventoryIndex = chosen.index,
            SpellId = chosen.grantsMammalControlRing
                ? "__ring_mammal_control__"
                : chosen.grantsWandOfFear
                    ? "__wand_of_fear__"
                : chosen.grantsTalismanPureGood
                    ? "__talisman_pure_good__"
                    : chosen.grantsHornOfValhalla
                        ? "__horn_of_valhalla__"
                    : spell?.Id,
            Target = target
        };

        AdvanceActor();
    }

    private static Spell? ResolveItemSpell(Adnd.Core.Items.Item item, List<Spell> allSpells)
    {
        if (Adnd.Core.Items.ItemSpecialAbilityParser.IsPotionOfDelusion(item))
            return null;

        if (!Adnd.Core.Items.ItemSpecialAbilityParser.TryGetCastedSpellName(item, out var spellName))
        {
            spellName = InferSpellNameFromItemName(item);
            if (string.IsNullOrWhiteSpace(spellName))
                return null;
        }

        return allSpells.FirstOrDefault(s => string.Equals(s.Name, spellName, StringComparison.OrdinalIgnoreCase));
    }

    private static string InferSpellNameFromItemName(Adnd.Core.Items.Item item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name))
            return string.Empty;

        if (string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase))
            return "Cure Light Wounds";

        if (string.Equals(item.Name, "Potion of Extra Healing", StringComparison.OrdinalIgnoreCase))
            return "Cure Serious Wounds";

        if (string.Equals(item.Name, "Potion of Cure Poison", StringComparison.OrdinalIgnoreCase))
            return "Neutralize Poison";

        switch (item.Name.ToLowerInvariant())
        {
            case "potion of latumofis": return "Neutralize Poison";
            case "potion of dios": return "Cure Light Wounds";
            case "potion of dial": return "Cure Serious Wounds";
            case "potion of sopic": return "Invisibility";
            case "scroll of katino": return "Sleep";
            case "scroll of badios": return "Cause Light Wounds";
            case "scroll of halito": return "Magic Missile";
            case "scroll of lomilwa": return "Light";
            case "scroll of dilto": return "Bless";
            case "scroll of badial": return "Cause Serious Wounds";
        }

        if (item.Name.StartsWith("Potion of ", StringComparison.OrdinalIgnoreCase))
            return item.Name[10..].Trim();

        if (item.Name.StartsWith("Scroll of ", StringComparison.OrdinalIgnoreCase))
            return item.Name[10..].Trim();

        return string.Empty;
    }

    private void ChooseAction(CombatActionType action)
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        if (!IsActionable(_party[_currentIndex]))
        {
            _currentIndex = FindNextActionableIndex(_currentIndex);
            if (_currentIndex < 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            UpdateHeader();
            UpdatePartyList();
            return;
        }

        var character = _party[_currentIndex];
        if (_session?.IsSwallowed(character.Name) == true)
        {
            if (action == CombatActionType.Fight)
                ChooseFight();
            else if (action == CombatActionType.Parry)
            {
                _actions[character.Name] = CombatAction.OfType(action);
                AdvanceActor();
            }
            else
                SayOnBoth("Swallowed", "Only cutting attacks from inside or waiting are available.");
            return;
        }
        var combatAction = CombatAction.OfType(action);

        // If Fight action and multiple groups exist, prompt for target group
        if (action == CombatActionType.Fight && _multipleGroups && _session != null)
        {
            var groups = _session.GetDistinctGroupIds()
                .Where(g => _session.GetAliveCountByGroup(g) > 0)
                .ToList();

            if (groups.Count > 1)
            {
                var targetGroupId = PromptGroupSelection(character);
                if (targetGroupId == null)
                    return; // User cancelled

                combatAction.TargetGroupId = targetGroupId;
            }
            else if (groups.Count == 1)
            {
                // Only one group, automatically target it
                combatAction.TargetGroupId = groups[0];
            }
        }

        _actions[character.Name] = combatAction;
        AdvanceActor();
    }

    private void ChooseSpellAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var caster = _party[_currentIndex];
        if (!IsActionable(caster))
            return;

        var castable = GetCastableCombatSpells(caster);
        if (castable.Count == 0)
        {
            SayOnBoth("Spell", $"{caster.Name} has no castable spells.");
            return;
        }

        var spell = castable.Count == 1 ? castable[0] : PromptSpellSelection(caster, castable);
        if (spell == null)
            return;

        SpellCastTarget? target;
        if (string.Equals(spell.Id, "raise_dead", StringComparison.OrdinalIgnoreCase))
        {
            target = PromptRaiseDeadCombatTarget(spell);
            if (target == null)
                return;
        }
        else if (spell.RangeType == SpellRangeType.Self)
        {
            target = SpellCastTarget.Ally(caster);
        }
        else if (spell.RangeType == SpellRangeType.Ally)
        {
            var ally = PromptAllyTarget(spell);
            if (ally == null)
                return;
            target = SpellCastTarget.Ally(ally);
        }
        else // Enemy spells
        {
            // Determine targeting based on TargetingScope
            if (spell.TargetingScope == SpellTargetingScope.SingleTarget)
            {
                // SingleTarget: pick a random target within a group
                // If multiple groups exist, ask which group to target
                if (_multipleGroups && _session != null)
                {
                    var targetGroupId = PromptGroupSelection(caster);
                    if (targetGroupId == null)
                        return;
                    target = SpellCastTarget.EnemyGroup(targetGroupId);
                }
                else
                {
                    // Single group or no session - use default targeting (random will be picked by handler)
                    target = SpellCastTarget.EnemyGroup("default");
                }
            }
            else if (spell.TargetingScope == SpellTargetingScope.SingleGroup)
            {
                // SingleGroup: affects all monsters in one group
                // If multiple groups exist, ask which group to target
                if (_multipleGroups && _session != null)
                {
                    var groups = _session.GetDistinctGroupIds()
                        .Where(g => _session.GetAliveCountByGroup(g) > 0)
                        .ToList();

                    if (groups.Count > 1)
                    {
                        var targetGroupId = PromptGroupSelection(caster);
                        if (targetGroupId == null)
                            return;
                        target = SpellCastTarget.EnemyGroup(targetGroupId);
                    }
                    else
                    {
                        // Only one group left
                        target = SpellCastTarget.EnemyGroup(groups.FirstOrDefault() ?? "default");
                    }
                }
                else
                {
                    // Single group encounter
                    target = SpellCastTarget.EnemyGroup("default");
                }
            }
            else // AllGroups
            {
                // AllGroups: affects all monsters in all groups, no targeting needed
                target = null;
            }
        }

        _actions[caster.Name] = new CombatAction
        {
            Type = CombatActionType.CastSpell,
            SpellId = spell.Id,
            Target = target
        };

        AdvanceActor();
    }

    private Spell? PromptSpellSelection(Character caster, List<Spell> castable)
    {
        var spellLines = string.Join(Environment.NewLine, castable.Select((s, i) => $"{i + 1}. L{s.Level} {s.Name}"));
        var selected = PromptForNumber(
            "Choose Spell",
            $"{caster.Name} - choose spell to cast:{Environment.NewLine}{Environment.NewLine}{spellLines}",
            1,
            castable.Count);

        if (!selected.HasValue)
            return null;

        return castable[selected.Value - 1];
    }

    private SpellCastTarget? PromptRaiseDeadCombatTarget(Spell spell)
    {
        var deadAllies = _party
            .Where(c => c.HasStatus(CharacterStatus.Dead) || c.CurrentHitPoints <= 0)
            .Where(a => !a.HasStatus(CharacterStatus.Swallowed))
            .ToList();

        var mummyTargets = _session == null
            ? new List<MonsterInstance>()
            : _session.AliveMonsters.Where(IsMummyForRaiseDead).ToList();

        if (deadAllies.Count == 0 && mummyTargets.Count == 0)
        {
            SayOnBoth("Raise Dead", "No valid Raise Dead target (dead ally or living mummy).");
            return null;
        }

        if (deadAllies.Count == 0 && mummyTargets.Count == 1)
            return SpellCastTarget.Enemy(mummyTargets[0].Index);

        if (mummyTargets.Count == 0 && deadAllies.Count == 1)
            return SpellCastTarget.Ally(deadAllies[0]);

        var options = new List<(string Label, SpellCastTarget Target)>();

        options.AddRange(deadAllies.Select(ally =>
            ($"ALLY  {ally.Name} (HP {ally.CurrentHitPoints}/{ally.MaxHitPoints}, Status: {FormatStatus(ally)})",
                SpellCastTarget.Ally(ally))));

        options.AddRange(mummyTargets.Select(monster =>
            ($"ENEMY {monster.DisplayNameWithGroup} (HP {monster.CurrentHitPoints}/{monster.MaxHitPoints})",
                SpellCastTarget.Enemy(monster.Index))));

        var lines = string.Join(Environment.NewLine, options.Select((o, i) => $"{i + 1}. {o.Label}"));
        var prompt = $"Casting: {spell.Name}{Environment.NewLine}{Environment.NewLine}{lines}";
        var selected = PromptForNumber("Choose Raise Dead Target", prompt, 1, options.Count);
        return selected.HasValue ? options[selected.Value - 1].Target : null;
    }

    private static bool IsMummyForRaiseDead(MonsterInstance monster)
    {
        var name = monster.Name?.Trim() ?? string.Empty;
        return string.Equals(name, "Mummy", StringComparison.OrdinalIgnoreCase)
               || name.Contains("mummy", StringComparison.OrdinalIgnoreCase);
    }

    private Character? PromptAllyTarget(Spell spell)
    {
        var allies = string.Equals(spell.Id, "raise_dead", StringComparison.OrdinalIgnoreCase)
            ? _party.Where(c => c.HasStatus(CharacterStatus.Dead) || c.CurrentHitPoints <= 0).ToList()
            : string.Equals(spell.Id, "resurrection", StringComparison.OrdinalIgnoreCase)
                ? _party.Where(c => c.HasStatus(CharacterStatus.Ashes)).ToList()
                : _party.Where(c => c.CurrentHitPoints > 0
                                    && !c.HasStatus(CharacterStatus.Dead)
                                    && !c.HasStatus(CharacterStatus.Ashes)
                                    && !c.HasStatus(CharacterStatus.Lost)).ToList();

        if (allies.Count == 0)
            return null;

        allies = allies.Where(a => !a.HasStatus(CharacterStatus.Swallowed)).ToList();
        if (allies.Count == 0)
            return null;
        var allyLines = string.Join(Environment.NewLine, allies.Select((a, i) =>
            $"{i + 1}. {a.Name} (HP {a.CurrentHitPoints}/{a.MaxHitPoints}, Status: {FormatStatus(a)})"));
        var prompt = $"Casting: {spell.Name}{Environment.NewLine}{Environment.NewLine}{allyLines}";
        var selected = PromptForNumber("Choose Ally Target", prompt, 1, allies.Count);
        return selected.HasValue ? allies[selected.Value - 1] : null;
    }

    private void ChooseLayOnHandsAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var paladin = _party[_currentIndex];
        if (!IsActionable(paladin))
            return;

        if (!paladin.IsPaladin())
        {
            SayOnBoth("Lay on Hands", $"{paladin.Name} is not a paladin.");
            return;
        }

        if (paladin.LayOnHandsUsedToday)
        {
            SayOnBoth("Lay on Hands", $"{paladin.Name} has already used Lay on Hands today.");
            return;
        }

        var target = PromptLayOnHandsTarget(paladin);
        if (target == null)
            return;

        _actions[paladin.Name] = new CombatAction
        {
            Type = CombatActionType.LayOnHands,
            Target = SpellCastTarget.Ally(target)
        };

        AdvanceActor();
    }

    private Character? PromptLayOnHandsTarget(Character paladin)
    {
        var allies = _party.Where(c => !c.HasStatus(CharacterStatus.Dead)
                                       && !c.HasStatus(CharacterStatus.Ashes)
                                       && !c.HasStatus(CharacterStatus.Lost)
                                       && !c.HasStatus(CharacterStatus.Swallowed))
            .ToList();

        if (allies.Count == 0)
            return null;

        var allyLines = string.Join(Environment.NewLine, allies.Select((a, i) =>
            $"{i + 1}. {a.Name} (HP {a.CurrentHitPoints}/{a.MaxHitPoints}, Status: {FormatStatus(a)})"));
        var prompt = $"Lay on Hands ({Math.Max(0, paladin.GetPaladinLevel()) * 2} HP){Environment.NewLine}{Environment.NewLine}{allyLines}";
        var selected = PromptForNumber("Choose Lay on Hands Target", prompt, 1, allies.Count);
        return selected.HasValue ? allies[selected.Value - 1] : null;
    }

    private static bool CanUseLayOnHands(Character c) => c.IsPaladin() && !c.LayOnHandsUsedToday;

    private static bool CanUseMonkBodyHeal(Character c) => c.IsMonk() && c.GetMonkLevel() >= 7 && !c.MonkBodyHealUsedToday;

    private void ChooseMonkBodyHealAction()
    {
        if (_currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var monk = _party[_currentIndex];
        if (!IsActionable(monk))
            return;

        if (!monk.IsMonk() || monk.GetMonkLevel() < 7)
        {
            SayOnBoth("Body Heal", $"{monk.Name} cannot use Body Heal yet.");
            return;
        }

        if (monk.MonkBodyHealUsedToday)
        {
            SayOnBoth("Body Heal", $"{monk.Name} has already used Body Heal today.");
            return;
        }

        _actions[monk.Name] = CombatAction.OfType(CombatActionType.MonkBodyHeal);
        AdvanceActor();
    }

    private static bool CanUseDispellUndead(Character c)
    {
        var clericLevel = c.Classes.Contains(CharacterClass.Cleric) ? c.GetClassLevel(CharacterClass.Cleric) : 0;
        var paladinLevel = c.Classes.Contains(CharacterClass.Paladin) ? c.GetClassLevel(CharacterClass.Paladin) : 0;
        var paladinAsCleric = paladinLevel >= 3 ? paladinLevel - 2 : 0;
        return clericLevel >= 1 || paladinAsCleric >= 1;
    }

    private static string FormatStatus(Character c)
    {
        var statuses = new List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        if (c.HasStatus(CharacterStatus.Blind)) statuses.Add("Blind");
        if (c.HasStatus(CharacterStatus.Diseased)) statuses.Add("Diseased");
        if (c.HasStatus(CharacterStatus.Feeblemind)) statuses.Add("Feeblemind");
        if (c.HasStatus(CharacterStatus.Slowed)) statuses.Add("Slowed");
        if (c.HasStatus(CharacterStatus.Swallowed)) statuses.Add("Swallowed");

        return statuses.Count == 0 ? "-" : string.Join(", ", statuses);
    }

    private int? PromptEnemyTarget()
    {
        var enemyLines = string.Join(Environment.NewLine, Enumerable.Range(1, _monsterCount).Select(i => $"{i}. {_monsterName} #{i}"));
        return PromptForNumber("Choose Enemy Target", enemyLines, 1, _monsterCount);
    }

    private string? PromptGroupSelection(Character caster)
    {
        if (_session == null)
            return "default";

        var groups = _session.GetDistinctGroupIds()
            .Where(groupId => _session.GetAliveCountByGroup(groupId) > 0)
            .ToList();

        if (groups.Count <= 1)
            return groups.FirstOrDefault() ?? "default";

        var groupDescriptions = new List<string>();
        for (int i = 0; i < groups.Count; i++)
        {
            var groupId = groups[i];
            var monstersInGroup = _session.GetAliveMonstersByGroup(groupId).ToList();
            var name = monstersInGroup.First().Name;
            var count = monstersInGroup.Count;
            var asleepCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Asleep));
            groupDescriptions.Add($"{i + 1}. {groupId}: {count} {name}" + (asleepCount > 0 ? $" ({asleepCount} asleep)" : ""));
        }

        var groupLines = string.Join(Environment.NewLine, groupDescriptions);
        var separator = GameRulesProvider.Current.UIOldStyle ? Environment.NewLine : Environment.NewLine + Environment.NewLine;
        var selected = PromptForNumber("Select Target Group", $"Which group should {caster.Name} target?{separator}{groupLines}", 1, groups.Count);

        if (!selected.HasValue)
            return null;

        return groups[selected.Value - 1];
    }

    private void ChooseTargetGroup()
    {
        if (_session == null || _currentIndex < 0 || _currentIndex >= _party.Count)
            return;

        var character = _party[_currentIndex];
        if (!IsActionable(character))
            return;

        var groups = _session.GetDistinctGroupIds()
            .Where(groupId => _session.GetAliveCountByGroup(groupId) > 0)
            .ToList();

        if (groups.Count <= 1)
        {
            SayOnBoth("Group Selection", "Only one group remains.");
            return;
        }

        var groupDescriptions = new List<string>();
        for (int i = 0; i < groups.Count; i++)
        {
            var groupId = groups[i];
            var monstersInGroup = _session.GetAliveMonstersByGroup(groupId).ToList();
            var name = monstersInGroup.First().Name;
            var count = monstersInGroup.Count;
            var asleepCount = monstersInGroup.Count(m => m.HasStatus(MonsterStatus.Asleep));
            groupDescriptions.Add($"{i + 1}. {groupId}: {count} {name}" + (asleepCount > 0 ? $" ({asleepCount} asleep)" : ""));
        }

        var groupLines = string.Join(Environment.NewLine, groupDescriptions);
        var separator = GameRulesProvider.Current.UIOldStyle ? Environment.NewLine : Environment.NewLine + Environment.NewLine;
        var selected = PromptForNumber("Select Target Group", $"Which group should {character.Name} target?{separator}{groupLines}", 1, groups.Count);

        if (selected.HasValue)
        {
            var selectedGroupId = groups[selected.Value - 1];
            SayOnBoth("Group Selected", $"{character.Name} will target {selectedGroupId}");
            // Store group preference (for now, just show message - actual targeting handled in combat resolver)
        }
    }

    private List<Spell> GetCastableCombatSpells(Character caster)
    {
        var allSpells = _spellRepo.LoadAll();
        var result = new List<Spell>();

        foreach (var state in caster.Spellcasting)
        {
            var byClass = allSpells
                .Where(s => s.SpellClass == state.SpellClass && (s.CastContext == SpellCastContext.Both || s.CastContext == SpellCastContext.Combat))
                .Where(s => _roundNumber == 1 || !string.Equals(s.Id, "bless", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var spell in byClass)
            {
                var levelIndex = spell.Level - 1;
                if (levelIndex < 0 || levelIndex >= state.SlotsPerDay.Count)
                    continue;

                if (state.SlotsPerDay[levelIndex] <= 0)
                    continue;

                var used = levelIndex < state.SlotsUsed.Count ? state.SlotsUsed[levelIndex] : 0;
                if (used >= state.SlotsPerDay[levelIndex])
                    continue;

                var isDivine = state.SpellClass is SpellClass.Cleric or SpellClass.Druid;
                if (!isDivine)
                {
                    if (!state.KnownSpellIds.Contains(spell.Id, StringComparer.OrdinalIgnoreCase))
                        continue;
                }

                result.Add(spell);
            }
        }

        return result
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Asks for a number, on the keyboard and on the table at once.
    ///
    /// Every choice inside a fight that is not one of the six action keys funnels through here -- which spell,
    /// which ally, which enemy, which group -- so making THIS answerable from the viewer makes all of them
    /// answerable, and doing it anywhere else would have meant four near-identical dialogs. Casting a spell
    /// from the table used to open this box and stop: the table had nothing to click and the game would not go
    /// on until someone typed a number into a window they might not even be looking at.
    ///
    /// The numbered lines are already in the prompt text, so the options are those numbers. The typed box
    /// stays exactly as it was -- both ends live, whichever answers first -- and Cancel still means cancel.
    /// </summary>

    /// <summary>
    /// Says something the player has to acknowledge, on the keyboard AND on the table.
    ///
    /// These were plain MessageBoxes, which the viewer can neither read nor dismiss: a fight driven from the
    /// table simply stopped -- most easily on "Only the first three may choose Fight", which is a thing a
    /// player does by accident several times a fight. Afterwards the fight's own question goes back up, so the
    /// table never sits showing a message that has already been answered.
    /// </summary>
    private void SayOnBoth(string title, string text)
    {
        ViewerPromptChanged?.Invoke(ViewerMessage.Prompt(text));
        ViewerMessage.Show(this, title, text);
        UpdateHeader();
    }

    private int? PromptForNumber(string title, string prompt, int min, int max)
    {
        try
        {
            return AskForNumber(title, prompt, min, max);
        }
        finally
        {
            // However it ended, the fight's own question is the one on the table again -- never a choice that
            // has already been made.
            UpdateHeader();
        }
    }

    private int? AskForNumber(string title, string prompt, int min, int max)
    {
        using var form = new Form();
        form.Text = title;
        form.StartPosition = FormStartPosition.CenterParent;
        var isChooseSpellDialog = string.Equals(title, "Choose Spell", StringComparison.OrdinalIgnoreCase);
        form.FormBorderStyle = isChooseSpellDialog ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
        form.ClientSize = isChooseSpellDialog
            ? new Size(620, 420)
            : new Size(520, 340);
        if (isChooseSpellDialog)
            form.MinimumSize = new Size(560, 340);
        form.MinimizeBox = false;
        form.MaximizeBox = false;

        Control promptControl;
        if (isChooseSpellDialog || GameRulesProvider.Current.UIOldStyle)
        {
            promptControl = new TextBox
            {
                Left = 12,
                Top = 12,
                Width = 596,
                Height = 338,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Text = GameRulesProvider.Current.UIOldStyle ? prompt.ReplaceLineEndings(Environment.NewLine) : prompt,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
        }
        else
        {
            promptControl = new Label
            {
                Left = 12,
                Top = 12,
                Width = 496,
                Height = 250,
                AutoSize = false,
                Text = prompt,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
        }

        var input = new TextBox
        {
            Left = 12,
            Top = isChooseSpellDialog ? 360 : 270,
            Width = isChooseSpellDialog ? 340 : 320,
            Anchor = isChooseSpellDialog
                ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
                : AnchorStyles.Top | AnchorStyles.Left
        };

        var ok = new Button
        {
            Text = "OK",
            Left = isChooseSpellDialog ? 446 : 352,
            Width = 75,
            Top = isChooseSpellDialog ? 358 : 268,
            DialogResult = DialogResult.OK,
            Anchor = isChooseSpellDialog
                ? AnchorStyles.Right | AnchorStyles.Bottom
                : AnchorStyles.Top | AnchorStyles.Left
        };
        var cancel = new Button
        {
            Text = "Cancel",
            Left = isChooseSpellDialog ? 527 : 433,
            Width = 75,
            Top = isChooseSpellDialog ? 358 : 268,
            DialogResult = DialogResult.Cancel,
            Anchor = isChooseSpellDialog
                ? AnchorStyles.Right | AnchorStyles.Bottom
                : AnchorStyles.Top | AnchorStyles.Left
        };

        form.Controls.Add(promptControl);
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        OldStyleDialog.Apply(form, promptControl, input);
        if (GameRulesProvider.Current.UIOldStyle && isChooseSpellDialog)
        {
            var extraSpellRows = Math.Max(0, max - min + 1 - 11);
            var maximumHeight = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height);
            form.Height = Math.Min(maximumHeight, form.Height + extraSpellRows * promptControl.Font.Height);
        }

        form.Shown += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        // The table's copy of this question, and its answer. One implementation of "publish the options, take an
        // answer from either end" lives in ViewerDialog and every dialog in the game now goes through it.
        var outcome = ViewerDialog.RunPick(form, this, title, Asking(), Labels(prompt, min, max), Publish);

        if (outcome.Picked.HasValue)
            return min + outcome.Picked.Value;

        if (outcome.Result != DialogResult.OK)
            return null;

        if (!int.TryParse(input.Text.Trim(), out var selected))
            return null;

        if (selected < min || selected > max)
            return null;

        return selected;
    }

    /// <summary>Where this fight's questions go so the table can answer them.</summary>
    private void Publish(ViewerPrompt? prompt) => ViewerPromptChanged?.Invoke(prompt);

    /// <summary>
    /// Whoever is acting, so the table asks over that figure as a bubble rather than laying a panel flat across
    /// the board -- at the fight camera a flat panel is sheared and half of it hangs off the screen.
    /// </summary>
    private string? Asking() => _currentIndex >= 0 && _currentIndex < _party.Count
        ? ViewerIds.Character(_party[_currentIndex].Name)
        : null;

    /// <summary>One label per number, read out of the list the dialog is already showing.</summary>
    private static List<string> Labels(string prompt, int min, int max)
    {
        var labels = new List<string>();
        for (var n = min; n <= max; n++)
            labels.Add(ViewerDialog.LabelFor(prompt, n));

        return labels;
    }

    private void StepBack()
    {
        var previous = FindPreviousActionableIndex(_currentIndex < 0 ? _party.Count : _currentIndex);
        if (previous < 0)
            return;

        _currentIndex = previous;
        _actions.Remove(_party[_currentIndex].Name);
        UpdateHeader();
        UpdatePartyList();
    }

    private void AdvanceActor()
    {
        _currentIndex = FindNextActionableIndex(_currentIndex);

        if (_currentIndex < 0)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        UpdateHeader();
        UpdatePartyList();
    }

    private void UpdateHeader()
    {
        var aliveMonsters = _session?.AliveMonsters.ToList();
        var asleepCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Asleep)) ?? _asleepMonsterCount;
        var entangledCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Entangled)) ?? _entangledMonsterCount;
        var panickedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Panicked)) ?? _panickedMonsterCount;
        var fearedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Feared)) ?? _fearedMonsterCount;
        var turnedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.TurnedUndead)) ?? _turnedMonsterCount;
        var blindCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Blinded)) ?? _blindedMonsterCount;
        var confusedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Confused)) ?? _confusedMonsterCount;
        var stunnedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Stunned)) ?? _stunnedMonsterCount;
        var slowedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Slowed)) ?? _slowedMonsterCount;
        var paralyzedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Paralyzed)) ?? _paralyzedMonsterCount;
        var unconsciousCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Unconscious)) ?? _unconsciousMonsterCount;
        var mazedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Mazed)) ?? 0;
        var charmedCount = aliveMonsters?.Count(m => m.HasStatus(MonsterStatus.Charmed)) ?? 0;

        var asleepText = !_multipleGroups && asleepCount > 0
            ? $"  ({asleepCount} ASLEEP)"
            : string.Empty;
        var entangledText = !_multipleGroups && entangledCount > 0
            ? $"  ({entangledCount} ENTANGLED)"
            : string.Empty;
        var panickedText = !_multipleGroups && panickedCount > 0
            ? $"  ({panickedCount} PANICKED)"
            : string.Empty;
        var fearedText = !_multipleGroups && fearedCount > 0
            ? $"  ({fearedCount} FEARED)"
            : string.Empty;
        var turnedText = !_multipleGroups && turnedCount > 0
            ? $"  ({turnedCount} TURNED)"
            : string.Empty;
        var blindText = !_multipleGroups && blindCount > 0
            ? $"  ({blindCount} BLINDED)"
            : string.Empty;
        var confusedText = !_multipleGroups && confusedCount > 0
            ? $"  ({confusedCount} CONFUSED)"
            : string.Empty;
        var stunnedText = !_multipleGroups && stunnedCount > 0
            ? $"  ({stunnedCount} STUNNED)"
            : string.Empty;
        var slowedText = !_multipleGroups && slowedCount > 0
            ? $"  ({slowedCount} SLOWED)"
            : string.Empty;
        var paralyzedText = !_multipleGroups && paralyzedCount > 0
            ? $"  ({paralyzedCount} PARALYZED)"
            : string.Empty;
        var unconsciousText = !_multipleGroups && unconsciousCount > 0
            ? $"  ({unconsciousCount} UNCONSCIOUS)"
            : string.Empty;
        var mazedText = !_multipleGroups && mazedCount > 0
            ? $"  ({mazedCount} MAZED)"
            : string.Empty;
        var charmedText = !_multipleGroups && charmedCount > 0
            ? $"  ({charmedCount} CHARMED)"
            : string.Empty;
        var surpriseText = _session != null
            && _session.RoundNumber == 1
            && !string.IsNullOrWhiteSpace(_session.SurpriseSummary)
        //    ? $"  (SURPRISE: {_session.SurpriseSummary!.ToUpperInvariant()})"
            ? $"  ({_session.SurpriseSummary!.ToUpperInvariant()})"
            : string.Empty;
        var headerMonsterName = (!_multipleGroups && _monsterCount > 1)
            ? _monsterName.ToUpperInvariant() + "S"
            : _monsterName.ToUpperInvariant();

        _headerLabel.Text = $"1)  {_monsterCount}  {headerMonsterName}{asleepText}{entangledText}{panickedText}{fearedText}{turnedText}{blindText}{confusedText}{stunnedText}{slowedText}{paralyzedText}{unconsciousText}{mazedText}{charmedText}{surpriseText}";

        if (_currentIndex >= 0 && _currentIndex < _party.Count)
        {
            _optionsTitleLabel.Text = $"{_party[_currentIndex].Name.ToUpperInvariant()}'S OPTIONS";
            UpdateOptionsLegend();
        }
        else
        {
            _optionsTitleLabel.Text = "NO ACTIONS (ALL CHARACTERS DOWN)";
        }

        // The header changes exactly when the open choice changes, so this is the one place that has
        // to tell the table. Fire-and-forget, like every other viewer hook here.
        ViewerPromptChanged?.Invoke(BuildViewerPrompt());
    }

    private void UpdateOptionsLegend()
    {
        if (_currentIndex >= 0 && _currentIndex < _party.Count && _session != null
            && _session.SwallowedCharacters.TryGetValue(_party[_currentIndex].Name, out var swallowed))
        {
            _optionsTitleLabel.Text = $"{swallowed.Victim.Name.ToUpperInvariant()} IS SWALLOWED";
            _optionsLegendLabel.Text = $"F↵IGHT FROM INSIDE   P)ARRY/WAIT   T)AKE BACK   I)NFO\nINNER AC 9   DAMAGE -{swallowed.RoundsInside(_session.RoundNumber)}   {swallowed.RoundsUntilDeath(_session.RoundNumber)} ROUND(S) UNTIL DEATH";
            return;
        }
        var rank = GetActionableRank(_currentIndex);
        var showLayOnHands = _currentIndex >= 0
                             && _currentIndex < _party.Count
                             && _party[_currentIndex].IsPaladin();
        var showMonkBodyHeal = _currentIndex >= 0
                               && _currentIndex < _party.Count
                               && _party[_currentIndex].IsMonk()
                               && _party[_currentIndex].GetMonkLevel() >= 7;
        var showBardCharm = _currentIndex >= 0
                            && _currentIndex < _party.Count
                            && _party[_currentIndex].IsBard();

        // Determine which action is mapped to Enter
        string fightText, parryText;
        if (rank is >= 1 and <= 3)
        {
            fightText = "F↵IGHT";  // Enter for Fight
            parryText = "P)ARRY";
        }
        else if (rank is >= 4 and <= 6)
        {
            fightText = "F)IGHT";
            parryText = "P↵ARRY";  // Enter for Parry
        }
        else
        {
            fightText = "F)IGHT";
            parryText = "P)ARRY";
        }

        var secondLine = showLayOnHands
            ? $"S)PELL   L)AY HANDS  {parryText}      T)AKE BACK"
            : $"S)PELL   {parryText}      T)AKE BACK";

        if (showMonkBodyHeal)
            secondLine = $"S)PELL   H)EAL BODY  {parryText}      T)AKE BACK";

        if (showBardCharm)
            secondLine = $"S)PELL   C)HARM      {parryText}      T)AKE BACK";

        if (_currentIndex >= 0 && _currentIndex < _party.Count && CanUseDispellUndead(_party[_currentIndex]))
            secondLine = $"S)PELL   D)ISPELL UNDEAD  {parryText}      T)AKE BACK";

        if (_currentIndex >= 0 && _currentIndex < _party.Count && showBardCharm && CanUseDispellUndead(_party[_currentIndex]))
            secondLine = $"S)PELL   C)HARM  D)ISPELL UNDEAD  {parryText}";

        if (_currentIndex >= 0 && _currentIndex < _party.Count && showLayOnHands && CanUseDispellUndead(_party[_currentIndex]))
            secondLine = $"S)PELL   D)ISPELL UNDEAD  L)AY HANDS  {parryText}";

        if (_currentIndex >= 0 && _currentIndex < _party.Count && showMonkBodyHeal && CanUseDispellUndead(_party[_currentIndex]))
            secondLine = $"S)PELL   D)ISPELL UNDEAD  H)EAL BODY  {parryText}";

        if (_multipleGroups)
        {
            _optionsLegendLabel.Text = $"{fightText}   U)SE ITEM   R)UN\n{secondLine}   G)ROUP   I)NFO";
        }
        else
        {
            _optionsLegendLabel.Text = $"{fightText}   U)SE ITEM   R)UN\n{secondLine}   I)NFO";
        }
    }

    private void ShowEncounterInfoWindow()
    {
        var text = BuildEncounterInfoText();
        var mmEntries = new List<(string ButtonText, string MonsterName, string ImagePath)>();

        if (_session != null)
        {
            var groups = _session.Monsters
                .GroupBy(m => m.GroupId)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                var templateName = group.FirstOrDefault()?.Template?.Name;
                if (string.IsNullOrWhiteSpace(templateName))
                    continue;

                var imagePath = TryFindMonsterMmImagePath(templateName);
                if (string.IsNullOrWhiteSpace(imagePath))
                    continue;

                mmEntries.Add(($"{templateName} info", templateName, imagePath));
            }
        }
        else
        {
            var mmMonsterName = ResolveEncounterMonsterNameForMmArt();
            var mmImagePath = TryFindMonsterMmImagePath(mmMonsterName);
            if (!string.IsNullOrWhiteSpace(mmImagePath))
                mmEntries.Add(($"{mmMonsterName} info", mmMonsterName, mmImagePath));
        }

        using var form = new Form
        {
            Text = "Encounter Info",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            ClientSize = new Size(820, 620),
            KeyPreview = true,
            MinimizeBox = false,
            MaximizeBox = true
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10f),
            Text = text
        };

        var close = new Button
        {
            Text = "Close",
            Dock = DockStyle.Right,
            Width = 90,
            DialogResult = DialogResult.OK
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true
        };

        foreach (var entry in mmEntries)
        {
            var showMmPicture = new Button
            {
                Text = entry.ButtonText,
                Width = 170,
                Height = 28,
                Margin = new Padding(0, 0, 8, 8)
            };

            showMmPicture.Click += (_, _) =>
            {
                using var image = TryLoadImageFromPath(entry.ImagePath);
                if (image == null)
                {
                    ViewerMessage.Say(form, "Monster Image", "Could not load MM picture.", null);
                    return;
                }

                using var imageForm = new Form
                {
                    Text = $"MM Picture - {entry.MonsterName}",
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.Sizable,
                    ClientSize = new Size(820, 620),
                    KeyPreview = true,
                    MinimizeBox = false,
                    MaximizeBox = true
                };

                imageForm.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                    {
                        imageForm.DialogResult = DialogResult.OK;
                        imageForm.Close();
                    }
                };

                var picture = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = (Image)image.Clone(),
                    BackColor = Color.Black
                };

                imageForm.Controls.Add(picture);
                imageForm.ShowDialog(form);
                picture.Image?.Dispose();
            };

            buttonsPanel.Controls.Add(showMmPicture);
        }

        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8)
        };
        panel.Controls.Add(buttonsPanel);
        panel.Controls.Add(close);

        form.Controls.Add(details);
        form.Controls.Add(panel);
        form.AcceptButton = close;
        form.CancelButton = close;

        form.ShowDialog(this);
        UpdateHeader();
    }

    private string ResolveEncounterMonsterNameForMmArt()
    {
        if (_session != null)
        {
            var firstName = _session.Monsters
                .GroupBy(m => m.GroupId)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.FirstOrDefault()?.Template?.Name)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            if (!string.IsNullOrWhiteSpace(firstName))
                return firstName!;
        }

        if (!string.IsNullOrWhiteSpace(_singleMonsterTemplate?.Name))
            return _singleMonsterTemplate.Name;

        return _monsterName;
    }

    private static string? TryFindMonsterMmImagePath(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return null;

        var trimmedName = monsterName.Trim();
        var slug = trimmedName.ToLowerInvariant().Replace(" ", "_");
        var camelCase = trimmedName.Replace(" ", "");
        var noComma = trimmedName.Replace(",", string.Empty).Trim();
        var noCommaSlug = noComma.ToLowerInvariant().Replace(" ", "_");
        var noCommaCamelCase = noComma.Replace(" ", "");

        var baseNames = new[]
        {
            trimmedName,
            slug,
            camelCase,
            noComma,
            noCommaSlug,
            noCommaCamelCase
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
        var folders = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Monsters", "MM1"),
            Path.Combine("Adnd.Game", "Assets", "Monsters", "MM1"),
            Path.Combine("Assets", "Monsters", "MM1")
        };

        foreach (var folder in folders)
        {
            foreach (var baseName in baseNames)
            {
                foreach (var ext in exts)
                {
                    var withMm = Path.Combine(folder, baseName + "MM" + ext);
                    if (File.Exists(withMm))
                        return withMm;

                    var withUnderscoreMm = Path.Combine(folder, baseName + "_MM" + ext);
                    if (File.Exists(withUnderscoreMm))
                        return withUnderscoreMm;
                }
            }
        }

        return null;
    }

    private static Image? TryLoadImageFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var imageSharp = ImageSharpImage.Load<ImageSharpRgba32>(path);
            using var ms = new MemoryStream();
            imageSharp.Save(ms, new ImageSharpPngEncoder());
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private string BuildEncounterInfoText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Encounter Monster Info");
        sb.AppendLine();

        if (_session == null)
        {
            if (_singleMonsterTemplate != null)
            {
                AppendMonsterTemplateInfo(sb, "default", _singleMonsterTemplate, _monsterCount, _monsterCount);
            }
            else
            {
                sb.AppendLine($"Name: {_monsterName}");
                sb.AppendLine($"Count: {_monsterCount}");
                sb.AppendLine("No template details available.");
            }

            return sb.ToString();
        }

        var groups = _session.Monsters
            .GroupBy(m => m.GroupId)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var list = group.ToList();
            if (list.Count == 0)
                continue;

            var template = list[0].Template;
            var total = list.Count;
            var alive = list.Count(m => m.IsAlive);
            AppendMonsterTemplateInfo(sb, group.Key, template, total, alive);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendMonsterTemplateInfo(StringBuilder sb, string groupId, Monster template, int totalCount, int aliveCount)
    {
        var attacks = template.Attacks.Count == 0
            ? "None"
            : string.Join(", ", template.Attacks.Select(a => $"{a.Name} {a.Damage} x{Math.Max(1, a.NumberOfAttacks)}"));

        sb.AppendLine($"Group: {groupId}");
        sb.AppendLine($"  Name: {template.Name}");
        sb.AppendLine($"  Type: {template.TypeName}");
        sb.AppendLine($"  Frequency: {DisplayOrUnknown(template.Frequency)}");
        sb.AppendLine($"  NumberOfAppearancesMin: {template.NumberOfAppearancesMin}");
        sb.AppendLine($"  NumberOfAppearancesMax: {template.NumberOfAppearancesMax}");
        sb.AppendLine($"  CountInEncounter: {aliveCount}/{totalCount} alive");
        sb.AppendLine($"  ArmorClass: {template.ArmorClass}");
        sb.AppendLine($"  Movement: {GetMovementDisplay(template)}");
        sb.AppendLine($"  HitDice: {template.HitDice}");
        sb.AppendLine($"  HitDiceType: {(template.HitDiceType <= 0 ? 8 : template.HitDiceType)}");
        sb.AppendLine($"  ExtraHitPoints: {template.ExtraHitPoints}");
        sb.AppendLine($"  THAC0: {template.THAC0}");
        sb.AppendLine($"  NumberOfAttacks: {template.NumberOfAttacks}");
        sb.AppendLine($"  %InLair: {template.InLairPercent}");
        sb.AppendLine($"  TreasureType: {DisplayOrUnknown(template.TreasureType)}");
        sb.AppendLine($"  IndividualTreasure: {DisplayOrUnknown(template.IndividualTreasure)}");
        sb.AppendLine($"  Attacks: {attacks}");
        if (template.SpecialAttacks.Count > 0)
            sb.AppendLine($"  SpecialAttacks: {string.Join(", ", template.SpecialAttacks.Select(a => string.IsNullOrWhiteSpace(a.Description) ? a.Name : $"{a.Name} ({a.Description})"))}");
        if (template.SpecialDefenses.Count > 0)
            sb.AppendLine($"  SpecialDefenses: {string.Join(", ", template.SpecialDefenses.Select(d => string.IsNullOrWhiteSpace(d.Description) ? d.Name : $"{d.Name} ({d.Description})"))}");
        if (template.SpecialAbilities.Count > 0)
            sb.AppendLine($"  SpecialAbilities: {string.Join(", ", template.SpecialAbilities.Select(a => string.IsNullOrWhiteSpace(a.Description) ? a.Name : $"{a.Name} ({a.Description})"))}");
        sb.AppendLine($"  MagicResistance: {DisplayOrUnknown(template.MagicResistance)}");
        sb.AppendLine($"  Intelligence: {DisplayOrUnknown(template.Intelligence)}");
        sb.AppendLine($"  Size: {template.Size}");
        sb.AppendLine($"  BaseXPValue: {template.BaseXPValue}");
        sb.AppendLine($"  XPValuePerHitPoint: {template.XPValuePerHitPoint}");
        sb.AppendLine($"  Alignment: {DisplayOrUnknown(template.Alignment)}");
        sb.AppendLine($"  Source: {template.Source}");
        sb.AppendLine($"  DungeonLevel: {template.DungeonLevel}");
    }

    private static string DisplayOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private static string GetMovementDisplay(Monster template)
    {
        if (!string.IsNullOrWhiteSpace(template.MovementRate))
            return template.MovementRate;

        var parts = new List<string>();
        if (template.Movement.Walk > 0) parts.Add($"{template.Movement.Walk}");
     //   if (template.Movement.Walk > 0) parts.Add($"Walk {template.Movement.Walk}");
        if (template.Movement.Fly > 0) parts.Add($"Fly {template.Movement.Fly}");
        if (template.Movement.Swim > 0) parts.Add($"Swim {template.Movement.Swim}");
        if (template.Movement.Burrow > 0) parts.Add($"Burrow {template.Movement.Burrow}");
        if (template.Movement.Climb > 0) parts.Add($"Climb {template.Movement.Climb}");

        if (parts.Count > 0)
            return string.Join(", ", parts);

        return "Unknown";
    }

    private void UpdatePartyList()
    {
        _partyList.Items.Clear();

        for (int i = 0; i < _party.Count; i++)
        {
            var c = _party[i];
            string action;
            var status = GetEncounterStatusLabel(c);

            if (!string.IsNullOrEmpty(status))
            {
                action = status;
            }
            else if (_actions.TryGetValue(c.Name, out var a))
            {
                action = GetDetailedActionStatus(a);
            }
            else
            {
                action = "??????";
            }

            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(c.Name);
            item.SubItems.Add(GetClassCode(c));
            item.SubItems.Add(c.ArmorClass.ToString());
            item.SubItems.Add(c.CurrentHitPoints.ToString()+ "/"+c.MaxHitPoints.ToString()); ;// Add max hit points as a separate subitem
            item.SubItems.Add(action);

            _partyList.Items.Add(item);
        }

        if (_currentIndex >= 0 && _currentIndex < _partyList.Items.Count)
            _partyList.Items[_currentIndex].Selected = true;
    }

    private string? GetEncounterStatusLabel(Character c)
    {
        var statuses = new List<string>();

        if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead)) statuses.Add("DEAD");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("ASHES");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("LOST");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("PARALYZED");
        if (statuses.Count == 0 && _session is { RoundNumber: 1, PartySurprisedRound1: true })
            statuses.Add("SURPRISED");
        if (c.HasStatus(CharacterStatus.Asleep))
        {
            var rounds = _session?.GetPartyAsleepRounds(c.Name) ?? 0;
            statuses.Add(rounds > 0 ? $"ASLEEP ({rounds} rounds)" : "ASLEEP");
        }

        if (_session?.IsFrozenByTimeStop(c) == true && statuses.Count == 0) statuses.Add("TIME STOPPED");

        return statuses.Count == 0 ? null : string.Join(", ", statuses);
    }

    private string GetDetailedActionStatus(CombatAction action)
    {
        var baseAction = action.Type.ToString();

        if (action.Type == CombatActionType.LayOnHands && !string.IsNullOrWhiteSpace(action.Target?.CharacterName))
            baseAction = $"LayOnHands: {action.Target.CharacterName}";

        if (action.Type == CombatActionType.UseItem)
        {
            if (!string.IsNullOrWhiteSpace(action.SpellId))
            {
                var allSpells = _spellRepo.LoadAll();
                var spell = allSpells.FirstOrDefault(s => s.Id == action.SpellId);
                if (spell != null)
                    baseAction = $"UseItem: {spell.Name}";
            }
            else
            {
                baseAction = "UseItem";
            }
        }

        // If casting a spell, show the spell name
        if ((action.Type == CombatActionType.Spell || action.Type == CombatActionType.CastSpell) && !string.IsNullOrEmpty(action.SpellId))
        {
            var allSpells = _spellRepo.LoadAll();
            var spell = allSpells.FirstOrDefault(s => s.Id == action.SpellId);
            if (spell != null)
            {
                baseAction = $"Spell: {spell.Name}";
            }
        }

        // If fighting or targeting a specific group, show the group
        if (!string.IsNullOrEmpty(action.TargetGroupId) && _multipleGroups)
        {
            // Get the monster name from the target group
            if (_session != null)
            {
                var groupMonsters = _session.GetMonstersByGroup(action.TargetGroupId).ToList();
                if (groupMonsters.Any())
                {
                    var monsterName = groupMonsters.First().Name;
                    baseAction += $" -> {monsterName}";
                }
            }
        }

        return baseAction;
    }

    private int FindNextActionableIndex(int fromIndex)
    {
        for (int i = fromIndex + 1; i < _party.Count; i++)
        {
            if (IsActionable(_party[i]))
                return i;
        }

        return -1;
    }

    private int FindPreviousActionableIndex(int fromIndex)
    {
        for (int i = fromIndex - 1; i >= 0; i--)
        {
            if (IsActionable(_party[i]))
                return i;
        }

        return -1;
    }

    private bool IsActionable(Character c) => CombatFormation.CanFight(c, _session) && _session?.IsFrozenByTimeStop(c) != true;

    private int GetActionableRank(int partyIndex)
    {
        if (partyIndex < 0 || partyIndex >= _party.Count)
            return -1;

        return CombatFormation.GetFightingRank(_party, _party[partyIndex], _session);
    }

    private static string GetClassCode(Character c)
    {
        var classes = (c.Classes != null && c.Classes.Count > 0)
            ? c.Classes
            : new List<CharacterClass> { c.Class };

        static string ToAbbrev(CharacterClass cls)
        {
            var name = cls.ToDisplayString();
            return name.Length >= 3 ? name[..3].ToUpperInvariant() : name.ToUpperInvariant();
        }

        string clsCode;
        if (c.IsDualClassed && c.DualClassOriginalClass.HasValue)
        {
            var current = c.DualClass.HasValue ? ToAbbrev(c.DualClass.Value) : ToAbbrev(classes[0]);
            var former = ToAbbrev(c.DualClassOriginalClass.Value);
            clsCode = $"{current}/({former})";
        }
        else
        {
            clsCode = string.Join("/", classes.Select(ToAbbrev));
        }

        var raceCode = c.Race.ToDisplayString();
        raceCode = raceCode.Length > 0 ? raceCode[..1].ToUpperInvariant() : "?";
        return $"{raceCode}-{clsCode}";
    }

    private void MonsterPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

        var rect = _monsterPanel.ClientRectangle;

        if (_wizardryStyle)
        {
            PaintWizardryStrip(g, rect);
            return;
        }

        if (_monsterImages.Count > 0)
        {
            DrawMultipleMonsterImages(g, rect, _monsterImages);
            return;
        }

        // Handle single monster image
        if (_monsterImage is not null)
        {
            DrawMonsterImage(g, rect, _monsterImage);
            return;
        }

        using var pen = new Pen(GameRulesProvider.Current.DefaultColor, 2f);
        var cx = rect.Width / 2f;
        var cy = rect.Height / 2f + 8f;

        switch (_monsterName.ToLowerInvariant())
        {
            case "skeleton":
                DrawSkeleton(g, pen, cx, cy);
                break;
            case "goblin":
                DrawHumanoid(g, pen, cx, cy);
                break;
            default:
                DrawRat(g, pen, cx, cy);
                break;
        }
    }

    private static void DrawMultipleMonsterImages(Graphics g, Rectangle bounds, List<(string Name, Image? Image)> monsterImages)
    {
        const int padding = 10;
        const int spacing = 15;

        if (monsterImages.Count == 0)
            return;

        // Calculate available space for each image/slot
        var totalSpacing = spacing * (monsterImages.Count - 1);
        var availableWidth = bounds.Width - (2 * padding) - totalSpacing;
        var slotWidth = availableWidth / monsterImages.Count;

        // Draw each monster in its slot
        for (int i = 0; i < monsterImages.Count; i++)
        {
            var image = monsterImages[i].Image;
            var name = monsterImages[i].Name;

            // Calculate the slot bounds
            var slotX = bounds.X + padding + (i * (slotWidth + spacing));
            var slotBounds = new Rectangle(slotX, bounds.Y + padding, slotWidth, bounds.Height - (2 * padding));

            if (image != null)
            {
                // Draw image if available
                var scale = Math.Min(slotBounds.Width / (float)image.Width, (slotBounds.Height - 20) / (float)image.Height);
                var drawWidth = (int)(image.Width * scale);
                var drawHeight = (int)(image.Height * scale);
                var drawX = slotBounds.X + ((slotBounds.Width - drawWidth) / 2);
                var drawY = slotBounds.Y + ((slotBounds.Height - 20 - drawHeight) / 2);

                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(image, new Rectangle(drawX, drawY, drawWidth, drawHeight));
            }
            else
            {
                // Draw simple placeholder if no image
                using var pen = new Pen(GameRulesProvider.Current.DefaultColor, 2f);
                var cx = slotBounds.X + (slotBounds.Width / 2f);
                var cy = slotBounds.Y + (slotBounds.Height / 2f);

                // Draw a simple creature shape based on name
                if (name.ToLowerInvariant().Contains("skeleton"))
                    DrawSkeleton(g, pen, cx, cy);
                else if (name.ToLowerInvariant().Contains("goblin"))
                    DrawHumanoid(g, pen, cx, cy);
                else
                    DrawRat(g, pen, cx, cy);
            }

            // Draw monster name label below
            using var font = new Font("Consolas", 10f, FontStyle.Bold);
            using var brush = new SolidBrush(GameRulesProvider.Current.DefaultColor);
            var textSize = g.MeasureString(name, font);
            var textX = slotBounds.X + ((slotBounds.Width - textSize.Width) / 2);
            var textY = slotBounds.Bottom - textSize.Height;
            g.DrawString(name, font, brush, textX, textY);
        }
    }

    private static void DrawMonsterImage(Graphics g, Rectangle bounds, Image image)
    {
        const int padding = 10;
        var target = Rectangle.Inflate(bounds, -padding, -padding);

        var scale = Math.Min(target.Width / (float)image.Width, target.Height / (float)image.Height);
        var drawWidth = (int)(image.Width * scale);
        var drawHeight = (int)(image.Height * scale);
        var drawX = target.X + ((target.Width - drawWidth) / 2);
        var drawY = target.Y + ((target.Height - drawHeight) / 2);

        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(image, new Rectangle(drawX, drawY, drawWidth, drawHeight));
    }

    /// <summary>
    /// The old UI's artwork for a monster, or null when there is none.
    ///
    /// Finding the file is now <see cref="MonsterArt"/>'s job, because the tabletop viewer needs the same
    /// answer and two searches of the same folders would drift apart. This still does the loading, since
    /// only this window wants a System.Drawing bitmap.
    /// </summary>
    private static System.Drawing.Image? TryLoadMonsterImage(string monsterName, int? dungeonLevel = null, bool useWizardrySuffix = false)
    {
        var path = MonsterArt.FindPath(monsterName, dungeonLevel, useWizardrySuffix);
        if (path is null)
            return null;

        try
        {
            // Use ImageSharp to load the image (supports WebP)
            using var imageSharp = ImageSharpImage.Load<ImageSharpRgba32>(path);

            // Convert ImageSharp image to System.Drawing.Bitmap
            using var ms = new MemoryStream();
            imageSharp.Save(ms, new ImageSharpPngEncoder());
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSkeleton(Graphics g, Pen pen, float cx, float cy)
    {
        g.DrawEllipse(pen, cx - 18, cy - 82, 36, 36);
        g.DrawLine(pen, cx, cy - 46, cx, cy + 16);
        g.DrawLine(pen, cx - 24, cy - 20, cx + 24, cy - 20);
        g.DrawLine(pen, cx - 18, cy + 0, cx + 18, cy + 0);
        g.DrawLine(pen, cx, cy + 16, cx - 16, cy + 52);
        g.DrawLine(pen, cx, cy + 16, cx + 16, cy + 52);
    }

    private static void DrawHumanoid(Graphics g, Pen pen, float cx, float cy)
    {
        g.DrawEllipse(pen, cx - 16, cy - 76, 32, 32);
        g.DrawLine(pen, cx, cy - 44, cx, cy + 18);
        g.DrawLine(pen, cx - 22, cy - 18, cx + 22, cy - 18);
        g.DrawLine(pen, cx, cy + 18, cx - 14, cy + 54);
        g.DrawLine(pen, cx, cy + 18, cx + 14, cy + 54);
    }

    private static void DrawRat(Graphics g, Pen pen, float cx, float cy)
    {
        g.DrawEllipse(pen, cx - 34, cy - 26, 68, 36);
        g.DrawEllipse(pen, cx + 24, cy - 22, 22, 18);
        g.DrawLine(pen, cx - 36, cy - 8, cx - 72, cy - 24);
    }

    /// <summary>See <see cref="MonsterArt.WizardryFirst"/> -- kept as a local name for the call sites.</summary>
    private static bool ShouldUseWizardrySuffix(Monster monster) => MonsterArt.WizardryFirst(monster);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monsterImage?.Dispose();
        }

        base.Dispose(disposing);
    }
}
