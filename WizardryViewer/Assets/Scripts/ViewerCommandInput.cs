using System.Collections.Generic;
using UnityEngine;
using WizardryViewer.Protocol;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// Turns keys pressed in the VIEWER's window into commands for the game.
    ///
    /// This is the crude first surface for playing from the table -- a second keyboard, which is
    /// enough to prove the round trip. Buttons on the table and, on a headset, a pointer at the
    /// figure you mean, queue the same ids through the same queue; only this file is replaced.
    ///
    /// The viewer sends what it detects without caring what the game is doing, because it cannot
    /// know: a fight may have opened since the last snapshot. It does not need to. Each form on the
    /// game side understands its own vocabulary and ignores the rest, so "forward" during a fight
    /// and "parry" in a corridor are both simply dropped. That is why no state is tracked here.
    /// </summary>
    public static class ViewerCommandInput
    {
        // Deliberately mirrors the game's own keys so muscle memory carries across the two windows.
        // Tab is left alone: the viewer already uses it to switch camera framing.
        //
        // One table rather than parallel arrays, so a binding cannot drift out of step with its key.
        private static readonly (KeyCode Key, string Option)[] Bindings =
        {
            // In the maze
            (KeyCode.W, "forward"),      (KeyCode.UpArrow, "forward"),
            (KeyCode.A, "turnLeft"),     (KeyCode.LeftArrow, "turnLeft"),
            (KeyCode.D, "turnRight"),    (KeyCode.RightArrow, "turnRight"),
            (KeyCode.O, "party"),
            (KeyCode.C, "camp"),
            (KeyCode.I, "inspect"),

            // In a fight
            (KeyCode.F, "fight"),
            (KeyCode.P, "parry"),
            // S is the one key that means two things -- status in the maze, cast a spell in a fight.
            // Rather than guess, both ids are sent: the maze knows only "status", a fight knows only
            // "spell", and each drops the one it has never heard of. Same trick would serve any
            // future collision.
            (KeyCode.S, "status"),
            (KeyCode.S, "spell"),
            (KeyCode.U, "useItem"),
            (KeyCode.R, "run"),
            (KeyCode.G, "targetGroup"),
            (KeyCode.Return, "confirm"), (KeyCode.KeypadEnter, "confirm"),
            (KeyCode.T, "undo"),

            // In town
            (KeyCode.N, "next"),         // walk on to the next place
            (KeyCode.M, "maze"),         // down through the gate

            // Dismissing something the game has stopped to say. Return also means "confirm" in a fight, so
            // both ids are listed -- but they only BOTH go out for a question that does not name Return
            // itself. A message does name it, which is what keeps the pair apart; see the note on claimed
            // keys in Poll for why that matters.
            (KeyCode.Return, "continue"), (KeyCode.KeypadEnter, "continue"),
            (KeyCode.Space, "continue"),

            // Either
            (KeyCode.Escape, "back"),
        };

        // What has already gone out this frame, so one key cannot send the same id twice. Reused rather than
        // allocated per frame, since Poll runs every frame and this is otherwise garbage for the collector.
        private static readonly HashSet<string> SentThisFrame = new HashSet<string>();

        // Keys the question on the table has already answered this frame. Same reuse, same reason.
        private static readonly HashSet<KeyCode> ClaimedThisFrame = new HashSet<KeyCode>();

        /// <summary>Main thread, once per frame. Queues a command per key pressed this frame.</summary>
        public static void Poll(ViewerCommandQueue queue, Prompt prompt = null)
        {
            if (queue == null) return;

            SentThisFrame.Clear();
            ClaimedThisFrame.Clear();

            // What the GAME says answers this question, first. Every option it sends may carry the key it also
            // responds to, so honouring those needs no table here at all -- a new screen with new keys works in
            // this window the moment the game labels it, and the two can never drift apart.
            if (prompt != null && prompt.Options != null)
            {
                foreach (var option in prompt.Options)
                {
                    if (option == null || string.IsNullOrEmpty(option.Id) || string.IsNullOrEmpty(option.Key)) continue;

                    KeyCode code;
                    if (KeyFor(option.Key, out code) && Input.GetKeyDown(code))
                    {
                        Send(queue, option.Id);
                        ClaimedThisFrame.Add(code);
                    }
                }
            }

            // Then the standing bindings, for the keys the old UI answers to whether or not the current prompt
            // mentions them -- and for the two ids that share a key, where sending both and letting each end
            // drop what it does not know is simpler than guessing which screen is up.
            //
            // EXCEPT for a key the question above has just answered. Sending both was safe only while the
            // spare id was one nothing on screen had heard of; it is not safe when the id lands on the NEXT
            // question instead. Return is the case that bit: a round's message names Return, so dismissing it
            // sent "continue" AND "confirm", the queue handed them out one poll apart, and the "confirm"
            // arrived after the message had gone and the first character's choice was up -- spending that
            // character's turn on the default before the player could choose. Sixty-six messages, sixty-six
            // stray confirms, and the front-rank leader lost his turn in every round after the first.
            //
            // A key the table has answered is therefore spent. What the question names, the question owns.
            foreach (var binding in Bindings)
            {
                if (ClaimedThisFrame.Contains(binding.Key)) continue;

                if (Input.GetKeyDown(binding.Key))
                    Send(queue, binding.Option);
            }
        }

        /// <summary>
        /// Queues an id unless this frame has already sent it.
        ///
        /// The two loops above overlap by design, and where they agree they used to agree twice: the camp screen
        /// answers to "inspect" and so does the standing binding for I, so one press sent it two times. The
        /// first closed the screen and the second went to whatever took over -- the maze underneath, which knows
        /// "inspect" too and opened its own. A press meaning one thing has to arrive once.
        ///
        /// Only WITHIN a frame. Pressing the same key twice is a player buying two daggers, and that must work.
        /// </summary>
        private static void Send(ViewerCommandQueue queue, string option)
        {
            if (SentThisFrame.Add(option)) queue.Enqueue(option);
        }

        /// <summary>
        /// The key a hint stands for: "F", "1", "Up", "Esc", "Enter", "Space".
        ///
        /// Written the way it is SHOWN rather than as a code, because the hint's first job is to be printed on a
        /// button -- see PromptOption.Key. Anything unrecognised simply has no key, which is the same position a
        /// headset with no keyboard is in.
        /// </summary>
        private static bool KeyFor(string hint, out KeyCode code)
        {
            code = KeyCode.None;
            if (string.IsNullOrEmpty(hint)) return false;

            switch (hint)
            {
                case "Up":    code = KeyCode.UpArrow;    return true;
                case "Down":  code = KeyCode.DownArrow;  return true;
                case "Left":  code = KeyCode.LeftArrow;  return true;
                case "Right": code = KeyCode.RightArrow; return true;
                case "Esc":   code = KeyCode.Escape;     return true;
                case "Enter": code = KeyCode.Return;     return true;
                case "Space": code = KeyCode.Space;      return true;
            }

            if (hint.Length != 1) return false;

            var c = char.ToUpperInvariant(hint[0]);
            if (c >= 'A' && c <= 'Z') { code = KeyCode.A + (c - 'A'); return true; }
            if (c >= '0' && c <= '9') { code = KeyCode.Alpha0 + (c - '0'); return true; }

            return false;
        }
    }
}
