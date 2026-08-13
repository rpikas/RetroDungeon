// Lets the tabletop viewer drive the game, as a second keyboard rather than a second brain.
//
// The whole design rests on one observation: every decision the player makes in the maze and in a
// fight already arrives as a key event, and those handlers are plain switches that end by
// publishing a fresh snapshot. So a viewer command does not need its own path through the game --
// it only needs to arrive as the key that means the same thing. Movement, turning, dialogs, combat
// choices and the snapshot publish then all follow the ordinary route, and the rest of the game
// cannot tell the difference. That is what keeps this isolated: no rules live here.
//
// Three properties are deliberate:
//   1. The keyboard never stops working. Both inputs are live at once and whichever arrives first
//      is acted on; there is no arbitration, no locking, no turn ownership. Driving both at the
//      same time will produce nonsense, which is the player's business.
//   2. Nothing here can stall or break the game. Polling runs on a background task, every exception
//      is swallowed, and a viewer that is absent, slow or wedged costs one idle task.
//   3. The viewer sends MEANING, not keystrokes ("forward", not "W"). The mapping to keys lives
//      here on the game side, so the viewer needs no idea which keys the game happens to use --
//      which is also what lets a VR build offer a pointer or a hand gesture later.
//
// ONE poller serves the whole process, dispatching to whichever form is on top. That is not an
// optimisation: a command is removed from the viewer's queue by the act of collecting it, so two
// pollers racing for the same queue means the loser's form swallows commands meant for the winner's.
// A fight opening over the maze is exactly that case, and the symptom would be viewer clicks going
// nowhere -- silently, and only while a fight is up.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Adnd.Game.Viewer;

/// <summary>
/// Replays commands queued in the viewer as key events on the form currently accepting input.
/// One instance per form; the instance is a registration handle, and disposing it deregisters.
/// </summary>
public sealed class ViewerControlPump : IDisposable
{
    // Short enough that clicking in the viewer feels immediate, long enough to be free. A crawl is
    // turn-based, so this is nowhere near a hot path.
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(80);

    // When nothing is listening there is no point asking 12 times a second forever.
    private static readonly TimeSpan AbsentDelay = TimeSpan.FromMilliseconds(1500);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(750) };

    // Newest registration wins. Deliberately NOT Form.ActiveForm: while the player is clicking in
    // the viewer's window this process has no active form at all, and every command would be lost.
    private static readonly List<ViewerControlPump> Registered = new();
    private static readonly object Gate = new();
    private static CancellationTokenSource? _pollerCts;

    private readonly Control _owner;
    private readonly IReadOnlyDictionary<string, Keys> _map;
    private readonly Action<Keys> _inject;
    private readonly Action<string>? _handle;

    private ViewerControlPump(Control owner, IReadOnlyDictionary<string, Keys> map, Action<Keys> inject,
                              Action<string>? handle)
    {
        _owner = owner;
        _map = map;
        _inject = inject;
        _handle = handle;
    }

    /// <summary>
    /// Registers <paramref name="owner"/> as the target for viewer commands and starts polling if it
    /// is not already running. Returns null when no viewer is configured, so a caller can assign the
    /// result unconditionally and dispose it with <c>?.</c> without a branch for the disabled case.
    /// </summary>
    /// <param name="map">Command vocabulary this form understands; see <see cref="ViewerCommands"/>.</param>
    /// <param name="inject">Hands a key to the form's own KeyDown handler. Called on the UI thread.</param>
    /// <param name="handle">
    /// Optional. Takes commands this form's key map has no key for, verbatim and on the UI thread.
    ///
    /// The key map is the right answer for a choice that IS a key -- Fight, Parry, forward -- because then a
    /// viewer click and a keypress travel the same path and the game cannot tell them apart. It has nothing
    /// to say about a choice that names a THING: "attack that monster" carries an id, and no keyboard has a
    /// key for the third slime. Rather than invent a key per monster, such a command is handed over as it
    /// arrived, and the form that asked the question does the reading.
    /// </param>
    public static ViewerControlPump? Start(
        Control owner,
        IReadOnlyDictionary<string, Keys> map,
        Action<Keys> inject,
        TabletopViewerBridge bridge,
        Action<string>? handle = null)
    {
        if (owner is null || map is null || inject is null || bridge is null) return null;
        if (!bridge.Enabled) return null;

        var pump = new ViewerControlPump(owner, map, inject, handle);

        lock (Gate)
        {
            Registered.Add(pump);

            if (_pollerCts is null)
            {
                _pollerCts = new CancellationTokenSource();
                var token = _pollerCts.Token;
                var endpoint = bridge.CommandEndpoint;
                _ = Task.Run(() => Poll(endpoint, token));
            }
        }

        return pump;
    }

    /// <summary>
    /// For forms that hold no bridge of their own, such as a fight. Constructing one costs a couple
    /// of environment reads, and the sequence counter it shares with the publishing bridge is static,
    /// so an extra instance cannot make the two disagree.
    /// </summary>
    public static ViewerControlPump? Start(Control owner, IReadOnlyDictionary<string, Keys> map, Action<Keys> inject,
                                          Action<string>? handle = null)
        => Start(owner, map, inject, new TabletopViewerBridge(), handle);

    /// <summary>The single poll loop for the process. Runs while at least one form is registered.</summary>
    private static async Task Poll(string endpoint, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                using var response = await Http.GetAsync(endpoint, token).ConfigureAwait(false);

                // 204 is the ordinary answer: the player has not clicked anything.
                if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent)
                {
                    var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    Dispatch(body);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;   // the last form deregistered: we are meant to stop
            }
            catch
            {
                // No viewer, or it went away mid-poll. Explicitly fine -- back off and keep trying,
                // so starting the viewer later in a session works without restarting the game.
                //
                // The token check above is load-bearing: HttpClient signals its own timeout by throwing
                // TaskCanceledException, which IS an OperationCanceledException, so catching that
                // unconditionally treated a 750ms timeout as "shut down" and returned. The poller then
                // died for good -- and because _pollerCts was still set, nothing ever started another --
                // so one slow or interrupted poll silently ended the table's ability to drive the game for
                // the rest of the session. Restarting the viewer did it every time.
                delay = AbsentDelay;
            }

            try { await Task.Delay(delay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Background thread. Decides nothing: reads the command and hands the top form a key.</summary>
    private static void Dispatch(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        string? option;
        try
        {
            using var doc = JsonDocument.Parse(body);
            option = doc.RootElement.TryGetProperty("option", out var value) ? value.GetString() : null;
        }
        catch
        {
            return;   // garbage on the wire is the viewer's problem, never the game's
        }

        if (string.IsNullOrEmpty(option)) return;

        ViewerControlPump? target;
        lock (Gate) target = Registered.Count > 0 ? Registered[Registered.Count - 1] : null;

        target?.Apply(option!);
    }

    private void Apply(string option)
    {
        // An unknown command is normal, not an error, and is NOT passed down to the form underneath:
        // a fight ignoring "forward" is the point, since the party must not walk off mid-encounter.
        // It also lets a newer viewer offer choices this build has never heard of.
        Action work;

        if (_map.TryGetValue(option, out var key))
        {
            work = () => _inject(key);
        }
        else if (_handle != null)
        {
            // No key for it, but this form may still know what it means -- see the note on `handle`.
            work = () => _handle(option);
        }
        else
        {
            return;
        }

        // Marshal onto the UI thread: the handler touches controls and runs game logic.
        try
        {
            if (_owner.IsDisposed || !_owner.IsHandleCreated) return;
            _owner.BeginInvoke(new Action(() =>
            {
                try { work(); }
                catch { /* a command must never take the game down */ }
            }));
        }
        catch
        {
            // Form closed between the checks above and the call. Nothing to do.
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? stopping = null;

        lock (Gate)
        {
            Registered.Remove(this);

            // Last form out turns off the polling.
            if (Registered.Count == 0)
            {
                stopping = _pollerCts;
                _pollerCts = null;
            }
        }

        if (stopping is null) return;

        try { stopping.Cancel(); } catch { }
        stopping.Dispose();
    }
}
