using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using WizardryViewer.Playback;
using WizardryViewer.Presentation;
using WizardryViewer.Protocol;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// Owns the transport and the playback clock. Snapshots arrive on a background thread,
    /// get queued, and are drained on the main thread where Unity objects can be touched.
    /// </summary>
    public sealed class ViewerReceiver : MonoBehaviour
    {
        [Header("Transport")]
        [SerializeField] private int port = 8787;
        [Tooltip("Loopback only. A Quest build would clear this, since the game is elsewhere.")]
        [SerializeField] private bool loopbackOnly = true;

        [Header("Pacing")]
        [SerializeField] private float beatSeconds = 0.80f;
        [SerializeField] private float compressedBeatSeconds = 0.25f;

        [Header("Presentation")]
        [SerializeField] private bool swedish;
        [SerializeField] private TableRenderer table;
        [SerializeField] private DmSubtitle subtitle;

        public PlaybackController Playback { get; private set; }
        public string Endpoint => _transport != null ? _transport.Endpoint : "(not started)";
        public bool Connected => _transport != null && _transport.IsListening;

        private ISnapshotTransport _transport;
        private Narrator _narrator;

        private readonly Queue<Snapshot> _inbox = new Queue<Snapshot>();
        private readonly object _inboxGate = new object();

        // The return path: what the player has asked for from this end, waiting for the game to
        // collect it. Never applied here -- the viewer proposes, the game decides.
        private readonly ViewerCommandQueue _outbox = new ViewerCommandQueue();
        private readonly Dictionary<string, string> _displayNames = new Dictionary<string, string>();

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore, // tolerate fields we predate
        };

        private void Awake()
        {
            EnsureInitialised();
        }

        /// <summary>
        /// Recompiling while in play mode reloads the domain: serialised fields survive, but
        /// anything built in Awake comes back null and Awake is NOT called again. Update would
        /// then throw every frame and the transport would be gone, so initialisation has to be
        /// re-entrant and driven from Update rather than assumed to have happened once.
        /// </summary>
        private void EnsureInitialised()
        {
            if (Playback != null) return;

            Playback = new PlaybackController
            {
                BeatSeconds = beatSeconds,
                CompressedBeatSeconds = compressedBeatSeconds,
            };

            _narrator = new Narrator(
                swedish ? new SwedishVocabulary() : new Vocabulary(),
                id => _displayNames.TryGetValue(id, out var n) ? n : id);

            Playback.Beat += OnBeat;
            Playback.Reconcile += OnReconcile;
            Playback.Skipped += n => Debug.Log($"[viewer] caught up, dropped {n} beats");

            _transport = new HttpSnapshotTransport(port, loopbackOnly);
            _transport.Received += OnPayload;
            _transport.NextCommand = _outbox.Next;   // called on the transport thread; queue is locked

            try
            {
                _transport.Start();
                Debug.Log($"[viewer] listening on {_transport.Endpoint}");
            }
            catch (Exception ex)
            {
                // Almost always an accept thread from a previous domain still holding the port.
                // It cannot be collected — the running thread roots it — so the only cure once
                // it exists is restarting Unity. beforeAssemblyReload below prevents it.
                Debug.LogError($"[viewer] could not start transport on port {port}: {ex.Message}. " +
                               "A listener from a previous domain is probably still bound; " +
                               "restart the editor or use another port.");
            }

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ShutdownTransport;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ShutdownTransport;
#endif
        }

        /// <summary>Background thread. Parse and queue; never touch Unity objects here.</summary>
        private void OnPayload(string json)
        {
            try
            {
                var snapshot = JsonConvert.DeserializeObject<Snapshot>(json, JsonSettings);
                if (snapshot == null) return;
                lock (_inboxGate) _inbox.Enqueue(snapshot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[viewer] undecodable payload: {ex.Message}");
            }
        }

        /// <summary>
        /// Queues a command for the game, by protocol id ("forward", "fight"). Public so a table
        /// button or, later, a VR pointer can call it without knowing anything about the transport.
        /// Safe to call when no game is running: unclaimed commands expire.
        /// </summary>
        public void Send(string option) => _outbox.Enqueue(option);

        private void Update()
        {
            EnsureInitialised();   // cheap null check; see the note on domain reloads

            // Playing FROM the table. Keys pressed in this window become commands the game collects
            // on its next poll; the game's own keyboard goes on working at the same time.
            ViewerCommandInput.Poll(_outbox, CurrentPrompt);

            while (true)
            {
                Snapshot next;
                lock (_inboxGate)
                {
                    if (_inbox.Count == 0) break;
                    next = _inbox.Dequeue();
                }

                CacheDisplayNames(next);
                Playback.Receive(next);
            }

            Playback.Tick(Time.deltaTime);
        }

        /// <summary>
        /// Ids are stable but not readable. Mapping them to words is a viewer concern, so it
        /// lives here and not in the protocol.
        /// </summary>
        private void CacheDisplayNames(Snapshot s)
        {
            foreach (var p in s.Party)
                _displayNames[p.Id] = p.Name;

            if (s.Encounter == null) return;

            foreach (var g in s.Encounter.Groups)
            {
                _displayNames[Ids.Group(g.GroupId)] = g.MonsterId;
                foreach (var m in g.Members)
                    _displayNames[m.Id] = $"{g.MonsterId} #{m.Index}";
            }
        }

        private void OnBeat(LogEntry entry, Snapshot context)
        {
            var line = _narrator.Describe(entry);   // null is normal: not everything needs words
            if (line != null && subtitle != null) subtitle.Say(line);
            if (table != null) table.PlayBeat(entry, context, line);
        }

        private void OnReconcile(Snapshot snapshot)
        {
            if (table != null) table.Reconcile(snapshot);

            // Deliberately here and not when the snapshot arrives: beats play first, and offering the
            // next choice while the previous blow is still landing would let the table run ahead of
            // what the player can see. Reconcile is the moment the table IS the snapshot.
            UpdatePrompt(snapshot.Prompt);
        }

        /// <summary>
        /// Raised when the game starts waiting on a different choice, or stops waiting. Whatever draws
        /// buttons subscribes here; the argument is null when there is nothing to offer.
        /// </summary>
        public event Action<Prompt> PromptChanged;

        /// <summary>The choice the game is waiting on right now, or null. Read on the main thread.</summary>
        public Prompt CurrentPrompt { get; private set; }

        /// <summary>
        /// Whether two prompts are the same QUESTION -- same kind, same options in the same order.
        ///
        /// Checked alongside the id because an id is only unique to the process that minted it, and the
        /// viewer outlives any number of those: restart the game while the table is up and its counter
        /// starts at 1 again, so the first thing the new session asks carries the id of the last thing the
        /// old one asked. Trusting the id alone, the viewer answered that by keeping the buttons it already
        /// had -- the table went on offering the hub's Next place while the game was asking a fight's
        /// Fight or Parry, and only the line above the buttons changed.
        /// </summary>
        private static bool SameQuestion(Prompt was, Prompt now)
        {
            if (was == null || now == null) return ReferenceEquals(was, now);
            if (was.Kind != now.Kind) return false;

            var before = was.Options;
            var after = now.Options;
            if (before == null || after == null) return before == after;
            if (before.Count != after.Count) return false;

            for (int i = 0; i < before.Count; i++)
            {
                if (before[i].Id != after[i].Id) return false;
            }

            return true;
        }

        private void UpdatePrompt(Prompt prompt)
        {
            // Ids identify the question, so an unchanged id means the same choice restated and there is
            // nothing for a listener to redraw.
            var wasId = CurrentPrompt != null ? CurrentPrompt.Id : 0L;
            var isId = prompt != null ? prompt.Id : 0L;

            if (wasId == isId && SameQuestion(CurrentPrompt, prompt))
            {
                // ...with one exception: the same question can be asked about a changed state, and the
                // prompt's TEXT is that state rather than part of the question. The town asks nothing but
                // "Next place" from the first pad to the last, while the line above the button names where
                // the party is standing -- so dropping the restatement outright left the dialog insisting
                // they were at the gate while they stood at the training ground. Listeners keep the buttons
                // they already built, and with them any click in flight; only the words are replaced.
                var wasText = CurrentPrompt != null ? CurrentPrompt.Text : null;
                var isText = prompt != null ? prompt.Text : null;
                if (wasText == isText) return;
            }

            CurrentPrompt = prompt;

            if (prompt != null)
                Debug.Log($"[viewer] prompt {prompt.Id} ({prompt.Kind}): {prompt.Text} -> " +
                          string.Join(", ", prompt.Options.ConvertAll(o => o.Id)));

            var handler = PromptChanged;
            if (handler != null) handler(prompt);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ShutdownTransport;
#endif
            ShutdownTransport();
        }

        /// <summary>
        /// Releases the port and stops the accept thread. Must run BEFORE an assembly reload:
        /// a domain reload does not stop background threads, so a listener left running would
        /// keep the port bound and go on answering 204 to POSTs it queues into an object the
        /// reload has already orphaned — the sender sees success while the table never updates.
        /// </summary>
        private void ShutdownTransport()
        {
            if (_transport == null) return;
            _transport.Received -= OnPayload;
            _transport.Dispose();
            _transport = null;
        }
    }
}
