using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Events2Code
{
    /// <summary>
    /// Minimal JavaScript colouriser for the generated-code preview. The preview only ever shows
    /// output from <see cref="Logic.JsCodeGenerator"/> (a few dozen lines), so a single-pass regex
    /// over the whole buffer is plenty and keeps the plugin dependency-free.
    /// </summary>
    internal static class JsSyntaxHighlighter
    {
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly Color CommentColor = Color.FromArgb(0, 128, 0);
        private static readonly Color StringColor = Color.FromArgb(163, 21, 21);
        private static readonly Color KeywordColor = Color.FromArgb(0, 0, 255);
        private static readonly Color NumberColor = Color.FromArgb(9, 134, 88);
        private static readonly Color CallColor = Color.FromArgb(121, 94, 38);
        private static readonly Color DefaultColor = Color.Black;

        // Order matters: comments and strings are matched first so keywords inside them stay plain.
        private static readonly Regex Tokens = new Regex(
            @"(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*'|`(?:\\.|[^`\\])*`)" +
            @"|(?<number>\b\d+(?:\.\d+)?\b)" +
            @"|(?<keyword>\b(?:var|let|const|function|return|if|else|for|while|do|switch|case|break|" +
            @"continue|new|delete|typeof|instanceof|this|null|undefined|true|false|try|catch|finally|" +
            @"throw|in|of|void|class|extends|super|async|await|yield)\b)" +
            @"|(?<call>\b[A-Za-z_$][A-Za-z0-9_$]*(?=\s*\())",
            RegexOptions.Compiled);

        public static void Apply(RichTextBox box, string code)
        {
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                box.Clear();
                box.Text = code ?? "";
                box.SelectAll();
                box.SelectionColor = DefaultColor;

                foreach (Match match in Tokens.Matches(box.Text))
                {
                    var color = ColorFor(match);
                    if (!color.HasValue) continue;

                    box.Select(match.Index, match.Length);
                    box.SelectionColor = color.Value;
                }

                box.Select(0, 0);
            }
            finally
            {
                SendMessage(box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                box.Invalidate();
            }
        }

        private static Color? ColorFor(Match match)
        {
            if (match.Groups["comment"].Success) return CommentColor;
            if (match.Groups["string"].Success) return StringColor;
            if (match.Groups["number"].Success) return NumberColor;
            if (match.Groups["keyword"].Success) return KeywordColor;
            if (match.Groups["call"].Success) return CallColor;
            return null;
        }
    }
}
