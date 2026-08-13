using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// Desktop transport. Accepts POSTs and answers 204 immediately, before doing anything
    /// with the body, so a slow viewer can never stall the game.
    ///
    /// Not for Quest: System.Net.HttpListener is unreliable under IL2CPP on Android. When
    /// that day comes, add TcpSnapshotTransport implementing the same interface — and bind
    /// to 0.0.0.0 rather than loopback, since the game will then be on another machine.
    /// </summary>
    public sealed class HttpSnapshotTransport : ISnapshotTransport
    {
        private readonly string _prefix;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public event Action<string> Received;

        public string Endpoint => _prefix + "state";
        public string CommandEndpoint => _prefix + "command";
        public bool IsListening => _running;

        public Func<string> NextCommand { get; set; }

        public HttpSnapshotTransport(int port, bool loopbackOnly = true)
        {
            var host = loopbackOnly ? "127.0.0.1" : "+";
            _prefix = $"http://{host}:{port}/";
        }

        public void Start()
        {
            if (_running) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix);
            _listener.Start();

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "WizardryViewer transport" };
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    return; // stopped
                }

                // The game polls for queued commands on a sibling path. Answered here rather than
                // through Received, because this one owes the caller a body.
                if (IsCommandPoll(ctx.Request))
                {
                    ServeCommand(ctx);
                    continue;
                }

                string body = null;
                try
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();
                }
                catch
                {
                    // fall through: still answer, still don't block the sender
                }

                try
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                }
                catch
                {
                    // sender already gave up on us; that is explicitly allowed
                }

                if (!string.IsNullOrEmpty(body))
                    Received?.Invoke(body);
            }
        }

        private static bool IsCommandPoll(HttpListenerRequest request)
        {
            return request != null
                && request.Url != null
                && string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                && request.Url.AbsolutePath.EndsWith("/command", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Hands over one queued command, or 204 when there is nothing to do -- which is the answer
        /// most of the time, since the game polls far faster than a player clicks.
        ///
        /// The command is taken from the queue before the response is known to have arrived, so a
        /// game that dies mid-poll loses it. That is the same cost as a keypress landing as the
        /// window closes, and cheaper than the bookkeeping to make it exactly-once.
        /// </summary>
        private void ServeCommand(HttpListenerContext ctx)
        {
            string payload = null;

            var supplier = NextCommand;
            if (supplier != null)
            {
                try { payload = supplier(); }
                catch { /* the host's queue is not allowed to break the listener */ }
            }

            try
            {
                if (string.IsNullOrEmpty(payload))
                {
                    ctx.Response.StatusCode = 204;
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }

                ctx.Response.Close();
            }
            catch
            {
                // Caller gave up, or the socket died. Allowed, same as for snapshots.
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
        }

        public void Dispose() => Stop();
    }
}
