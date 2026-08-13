using System;
using System.Collections.Generic;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// Player commands waiting for the game to collect them.
    ///
    /// Filled on Unity's main thread (a keypress, a button, later a VR pointer) and drained on the
    /// transport thread when the game polls, so every access is behind the same lock.
    ///
    /// Two deliberate limits, because the game may not be running at all:
    ///   * the queue is capped, and the OLDEST command is discarded when it overflows
    ///   * a command older than <see cref="MaxAge"/> is dropped rather than delivered
    /// Between them, starting the game an hour after clicking around in the viewer does not replay
    /// an hour of stale moves at the party.
    /// </summary>
    public sealed class ViewerCommandQueue
    {
        private const int Capacity = 8;
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);

        private readonly Queue<Entry> _pending = new Queue<Entry>();
        private readonly object _gate = new object();
        private long _seq;

        private struct Entry
        {
            public string Option;
            public DateTime QueuedUtc;
        }

        public int Count
        {
            get { lock (_gate) return _pending.Count; }
        }

        /// <summary>Main thread. Queues one command by its protocol id, e.g. "forward".</summary>
        public void Enqueue(string option)
        {
            if (!IsWellFormed(option)) return;

            lock (_gate)
            {
                while (_pending.Count >= Capacity) _pending.Dequeue();
                _pending.Enqueue(new Entry { Option = option, QueuedUtc = DateTime.UtcNow });
            }
        }

        /// <summary>
        /// Transport thread. Returns the next command as JSON, or null when there is nothing fresh
        /// to hand over. Assign this to <see cref="ISnapshotTransport.NextCommand"/>.
        /// </summary>
        public string Next()
        {
            lock (_gate)
            {
                while (_pending.Count > 0)
                {
                    var entry = _pending.Dequeue();
                    if (DateTime.UtcNow - entry.QueuedUtc > MaxAge) continue;   // stale; skip it

                    _seq++;
                    return "{\"seq\":" + _seq + ",\"option\":\"" + Escape(entry.Option) + "\"}";
                }

                return null;
            }
        }

        /// <summary>
        /// Rejects nonsense, and nothing more.
        ///
        /// This used to allow letters only, which was load-bearing: the JSON above is assembled by hand,
        /// and a closed alphabet meant nothing needed escaping. It also silently dropped every command the
        /// tavern sends, because those name a character -- "toggle:Bran" -- and a colon is not a letter.
        /// The command simply vanished, with the viewer looking like it had ignored the click.
        ///
        /// Escaping is now done properly in <see cref="Escape"/>, which is where that job belonged, so ids
        /// are free to carry a player-entered name. Control characters still go: they cannot appear in a
        /// real id, and an id is not a place to be relaxed about surprises.
        /// </summary>
        private static bool IsWellFormed(string option)
        {
            if (string.IsNullOrEmpty(option) || option.Length > 64) return false;

            for (int i = 0; i < option.Length; i++)
            {
                if (char.IsControl(option[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Minimal JSON string escaping, for the one string this class puts on the wire.
        ///
        /// Hand-rolled because the alternative is dragging a serialiser into a class that emits exactly one
        /// two-field object, and because the previous answer -- forbidding every character that would need
        /// escaping -- turned out to forbid characters the game legitimately sends.
        /// </summary>
        private static string Escape(string option)
        {
            var builder = new System.Text.StringBuilder(option.Length + 8);

            for (int i = 0; i < option.Length; i++)
            {
                var c = option[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    default:
                        if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
