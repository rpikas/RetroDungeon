using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Adnd.Game;

/// <summary>
/// A console that lives inside the RetroDungeon window. The city menus keep calling Console.WriteLine and
/// Console.ReadKey as before (see the global alias in GlobalUsings.cs); the text lands in the window's text
/// area and keys come from the window. Without a window it falls back to the real console.
/// </summary>
internal static class TermConsole
{
    public const int Columns = 120;
    private const int MaxLines = 2000;

    private static readonly List<string> Lines = new();
    private static readonly StringBuilder Current = new();
    private static readonly Queue<ConsoleKeyInfo> Pending = new();
    private static Keys _lastKeyCode;
    private static MazeForm? _host;

    /// <summary>The RetroDungeon window the city runs in, if there is one.</summary>
    public static MazeForm? Host => _host is { IsDisposed: false } ? _host : null;

    public static int Scrolled { get; set; }

    /// <summary>Text rows that fit in the terminal the last time it was drawn (0 until the first draw).</summary>
    public static int VisibleRows { get; set; }

    /// <summary>Lines drawn under the city picture (the shop's menu); cleared when the picture is removed.</summary>
    public static string[]? SideLines { get; set; }

    /// <summary>File name in Assets\ScenPictures shown beside the text, or null for no picture.</summary>
    public static string? CityPicture
    {
        get => _cityPicture;
        set
        {
            _cityPicture = value;
            if (value == null) SideLines = null;
            if (_host is { IsDisposed: false, IsHandleCreated: true } host)
            {
                host.Invalidate();
                host.Update();
            }
        }
    }

    private static string? _cityPicture;

    /// <summary>When true the window shows the two clickable ruleset pictures (AD&D and Wizardry) instead of CityPicture.</summary>
    public static bool RulesetPictures
    {
        get => _rulesetPictures;
        set
        {
            _rulesetPictures = value;
            _host?.Invalidate();
        }
    }

    private static bool _rulesetPictures;

    /// <summary>Types text into the window as if it came from the keyboard, without pressing Enter.</summary>
    public static void EnqueueText(string text)
    {
        foreach (var c in text)
            Pending.Enqueue(new ConsoleKeyInfo(c, KeyFromChar(c), char.IsUpper(c), false, false));
        Scrolled = 0;
    }

    /// <summary>
    /// While a "Choose #" list is on screen, the number of characters its reader takes before it submits by
    /// itself (0 = double-clicking a numbered row does nothing).
    /// </summary>
    public static int ClickableNumberDigits { get; set; }

    /// <summary>While a "Choose letter" list is on screen, double-clicking a row ("B. Item") types its letter.</summary>
    public static bool ClickableLetters { get; set; }

    /// <summary>Types text (and Enter) into the window as if it came from the keyboard.</summary>
    public static void EnqueueLine(string text)
    {
        foreach (var c in text + "\r")
            Pending.Enqueue(new ConsoleKeyInfo(c, c == '\r' ? ConsoleKey.Enter : KeyFromChar(c), char.IsUpper(c), false, false));
        Scrolled = 0;
    }

    public static void Attach(MazeForm host)
    {
        _host = host;
        host.FormClosed += (_, _) => _host = null;
    }

    private static bool Hosted => _host is { IsDisposed: false };

    // -- Input from the window --------------------------------------------------------------------------

    private static readonly HashSet<Keys> NonCharKeys = new()
    {
        System.Windows.Forms.Keys.Up, System.Windows.Forms.Keys.Down, System.Windows.Forms.Keys.Left, System.Windows.Forms.Keys.Right,
        System.Windows.Forms.Keys.Home, System.Windows.Forms.Keys.End, System.Windows.Forms.Keys.PageUp, System.Windows.Forms.Keys.PageDown,
        System.Windows.Forms.Keys.Insert, System.Windows.Forms.Keys.Delete,
    };

    public static void FeedKeyDown(KeyEventArgs e)
    {
        _lastKeyCode = e.KeyCode;
        var isFunctionKey = e.KeyCode >= System.Windows.Forms.Keys.F1 && e.KeyCode <= System.Windows.Forms.Keys.F24;
        if (!NonCharKeys.Contains(e.KeyCode) && !isFunctionKey)
            return;

        Pending.Enqueue(new ConsoleKeyInfo('\0', (ConsoleKey)(int)e.KeyCode, e.Shift, e.Alt, e.Control));
        Scrolled = 0;
    }

    public static void FeedKeyPress(char c)
    {
        if (char.IsControl(c) && c is not ('\r' or '\u001b' or '\b' or '\t'))
            return;

        var key = _lastKeyCode == 0 ? KeyFromChar(c) : (ConsoleKey)(int)_lastKeyCode;
        Pending.Enqueue(new ConsoleKeyInfo(c, key, char.IsUpper(c), false, false));
        Scrolled = 0;
    }

    private static ConsoleKey KeyFromChar(char c) => char.IsLetter(c)
        ? (ConsoleKey)char.ToUpperInvariant(c)
        : char.IsDigit(c) ? (ConsoleKey)c : ConsoleKey.Spacebar;

    // -- Console surface --------------------------------------------------------------------------------

    public static bool KeyAvailable
    {
        get
        {
            if (!Hosted)
                return Console_.KeyAvailable;

            Application.DoEvents();
            return Pending.Count > 0;
        }
    }

    public static ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        if (!Hosted)
            return Console_.ReadKey(intercept);

        while (Pending.Count == 0)
        {
            if (!Hosted)
                throw new OperationCanceledException("The RetroDungeon window was closed.");

            Application.DoEvents();
            Thread.Sleep(5);
        }

        var key = Pending.Dequeue();
        if (!intercept && key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            Write(key.KeyChar.ToString());

        return key;
    }

    public static string? ReadLine()
    {
        if (!Hosted)
            return Console_.ReadLine();

        var text = new StringBuilder();
        while (true)
        {
            var key = ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                    if (Current.Length > 0)
                        Current.Length--;
                    _host?.Invalidate();
                }

                continue;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
                Write(key.KeyChar.ToString());
            }
        }
    }

    public static void Clear()
    {
        if (!Hosted)
        {
            Console_.Clear();
            return;
        }

        Lines.Clear();
        Current.Clear();
        Scrolled = 0;
        SideLines = null;
        _host?.Invalidate();
    }

    public static void Write(string? text)
    {
        if (!Hosted)
        {
            Console_.Write(text);
            return;
        }

        foreach (var ch in text ?? string.Empty)
        {
            if (ch == '\n')
            {
                EndLine();
            }
            else if (ch != '\r')
            {
                Current.Append(ch == '\t' ? ' ' : ch);
                if (Current.Length >= Columns)
                    EndLine();
            }
        }

        _host?.Invalidate();
    }

    public static void Write(object? value) => Write(value?.ToString());

    public static void Write(char value) => Write(value.ToString());

    public static void Write(string format, params object?[] args) => Write(string.Format(format, args));

    public static void WriteLine() => Write("\n");

    public static void WriteLine(string? text) => Write((text ?? string.Empty) + "\n");

    public static void WriteLine(object? value) => WriteLine(value?.ToString());

    public static void WriteLine(char value) => WriteLine(value.ToString());

    public static void WriteLine(string format, params object?[] args) => WriteLine(string.Format(format, args));

    private static void EndLine()
    {
        Lines.Add(Current.ToString());
        Current.Clear();
        if (Lines.Count > MaxLines)
            Lines.RemoveRange(0, Lines.Count - MaxLines);
    }

    /// <summary>Everything written since the last Clear, the unfinished line last.</summary>
    public static List<string> Snapshot()
    {
        var all = new List<string>(Lines);
        all.Add(Current.ToString());
        return all;
    }

    private static class Console_
    {
        public static bool KeyAvailable => System.Console.KeyAvailable;
        public static ConsoleKeyInfo ReadKey(bool intercept) => System.Console.ReadKey(intercept);
        public static string? ReadLine() => System.Console.ReadLine();
        public static void Clear() => System.Console.Clear();
        public static void Write(string? text) => System.Console.Write(text);
    }
}
