#nullable enable
using System;
using System.Collections.Generic;
using WizardryViewer.Protocol;

namespace WizardryViewer.Playback
{

    /// <summary>
    /// Decides what the table should be doing right now. Engine-agnostic: no Unity types, no
    /// clock of its own — the host calls <see cref="Tick"/> with a delta.
    ///
    /// Contract (docs/viewer-protocol.md):
    ///   * snapshots are truth, log entries are theatre
    ///   * a snapshot's log describes the transition INTO that snapshot, so beats play first
    ///     and <see cref="Reconcile"/> fires at the end of the step
    ///   * falling two or more snapshots behind abandons the queue and reconciles to newest
    /// </summary>
    public sealed class PlaybackController
    {
        public float BeatSeconds { get; set; } = 0.80f;
        public float CompressedBeatSeconds { get; set; } = 0.25f;

        /// <summary>Play this log entry. Fires in order, at most one at a time.</summary>
        public event Action<LogEntry, Snapshot>? Beat;

        /// <summary>Make the table match this snapshot exactly. Always safe to apply.</summary>
        public event Action<Snapshot>? Reconcile;

        /// <summary>Beats were dropped to catch up. Purely informational (debug overlay).</summary>
        public event Action<int>? Skipped;

        private readonly object _gate = new();
        private readonly Queue<Snapshot> _pending = new();

        private Snapshot? _playing;
        private int _beatIndex;
        private float _beatTimer;

        public Snapshot? Showing { get; private set; }
        public long LastSeqReceived { get; private set; } = -1;

        /// <summary>Which run the sequence numbers above belong to. See <see cref="Snapshot.Run"/>.</summary>
        private string? _run;

        public int PendingCount { get { lock (_gate) return _pending.Count; } }

        /// <summary>Called from the network thread. Never blocks the caller for long.</summary>
        public void Receive(Snapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (_gate)
            {
                // A different run means the game restarted and started counting again, so its sequence
                // numbers say nothing about ours: take it and start following the new run's count.
                if (snapshot.Run != _run)
                {
                    _run = snapshot.Run;
                    LastSeqReceived = -1;
                }

                // Out-of-order arrivals are dropped; the newer one already supersedes them.
                if (snapshot.Seq <= LastSeqReceived && LastSeqReceived >= 0)
                    return;

                LastSeqReceived = snapshot.Seq;
                _pending.Enqueue(snapshot);
            }
        }

        /// <summary>Called from the render thread once per frame.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_playing != null)
            {
                _beatTimer -= deltaSeconds;
                if (_beatTimer > 0f)
                    return;

                AdvanceBeat();
                return;
            }

            StartNextSnapshot();
        }

        private void StartNextSnapshot()
        {
            Snapshot? next;
            int behind;

            lock (_gate)
            {
                if (_pending.Count == 0)
                    return;

                behind = _pending.Count;

                if (behind >= 2)
                {
                    // Too far behind for theatre. Drain to the newest and jump.
                    int dropped = 0;
                    while (_pending.Count > 1)
                    {
                        var skippedSnapshot = _pending.Dequeue();
                        dropped += skippedSnapshot.Log?.Count ?? 0;
                    }

                    next = _pending.Dequeue();
                    Showing = next;
                    Reconcile?.Invoke(next);
                    if (dropped > 0) Skipped?.Invoke(dropped);
                    return;
                }

                next = _pending.Dequeue();
            }

            if (next.Log == null || next.Log.Count == 0)
            {
                Showing = next;
                Reconcile?.Invoke(next);
                return;
            }

            _playing = next;
            _beatIndex = -1;
            _beatTimer = 0f;
            AdvanceBeat();
        }

        private void AdvanceBeat()
        {
            var playing = _playing;
            if (playing == null)
                return;

            _beatIndex++;

            if (_beatIndex >= playing.Log.Count)
            {
                _playing = null;
                Showing = playing;
                Reconcile?.Invoke(playing);
                return;
            }

            _beatTimer = PendingCount >= 1 ? CompressedBeatSeconds : BeatSeconds;
            Beat?.Invoke(playing.Log[_beatIndex], playing);
        }
    }

}
