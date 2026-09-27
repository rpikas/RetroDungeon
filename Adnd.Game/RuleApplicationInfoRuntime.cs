using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Adnd.Game;

internal static class RuleApplicationInfoRuntime
{
    private static readonly object _sync = new();
    private static RuleApplicationInfoForm? _form;
    private static Action<string>? _handler;
    private static SynchronizationContext? _uiContext;
    private static Thread? _uiThread;
    private static ManualResetEventSlim? _ready;

    public static void StartIfEnabled()
    {
        if (!GameRulesProvider.Current.ShowDiceRollAndRuleApplicationInfo)
            return;

        lock (_sync)
        {
            if (_uiThread != null && _uiThread.IsAlive)
                return;

            _ready = new ManualResetEventSlim(false);

            _uiThread = new Thread(() =>
            {
                try
                {
                    var form = new RuleApplicationInfoForm();
                    _form = form;

                    form.HandleCreated += (_, _) =>
                    {
                        _uiContext = SynchronizationContext.Current;
                        _ready?.Set();
                    };

                    _handler = message =>
                    {
                        var ctx = _uiContext;
                        if (ctx == null || form.IsDisposed)
                            return;

                        ctx.Post(_ =>
                        {
                            if (!form.IsDisposed)
                                form.AppendInfo(message);
                        }, null);
                    };
                    RuleApplicationInfo.InfoPublished += _handler;

                    Application.Run(form);
                }
                finally
                {
                    if (_handler != null)
                        RuleApplicationInfo.InfoPublished -= _handler;

                    _handler = null;
                    _uiContext = null;
                    _form = null;
                }
            })
            {
                IsBackground = true,
                Name = "RuleApplicationInfoRuntime",
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
        }

        _ready?.Wait(2000);
        RuleApplicationInfo.Publish("Rule/dice diagnostics enabled.");
    }

    public static void Stop()
    {
        RuleApplicationInfoForm? formToClose;
        Thread? threadToJoin;

        lock (_sync)
        {
            formToClose = _form;
            threadToJoin = _uiThread;
            _uiThread = null;
        }

        if (formToClose != null && !formToClose.IsDisposed)
        {
            try
            {
                if (formToClose.InvokeRequired)
                    formToClose.BeginInvoke(new Action(formToClose.Close));
                else
                    formToClose.Close();
            }
            catch
            {
                // Ignore teardown races during shutdown.
            }
        }

        if (threadToJoin != null && threadToJoin.IsAlive)
            threadToJoin.Join(2000);

        _ready?.Dispose();
        _ready = null;
    }
}
