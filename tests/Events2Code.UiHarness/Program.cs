using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Events2Code;
using Events2Code.Harness;
using Events2Code.Logic;

namespace Events2Code.UiHarness
{
    /// <summary>
    /// Hosts the tool's control in a bare form, fills it with sample rows and screenshots it, so
    /// layout work can be checked without XrmToolBox and without a Dataverse connection. Run it
    /// through ui.ps1 rather than directly.
    ///
    ///   uiharness.exe &lt;width&gt; &lt;height&gt; &lt;output.png&gt; [scene] [window title]
    ///
    /// The control is driven the way XrmToolBox drives it - construct, dock, show - so anything
    /// the real tool does on Load and Resize happens here too. Sample data goes in through
    /// reflection: the lists and their backing fields are private, and exposing them for a test
    /// harness would be a worse trade than this file knowing their names.
    ///
    /// Scenes decide which sample form is loaded. "layout" is the small one layout work is checked
    /// against; "showcase" is a form with one of every convertible event on it, which is what the
    /// screenshots in docs are taken from. The code pane is filled by the real generator either
    /// way, so a screenshot cannot drift away from what the tool actually emits.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: uiharness.exe <width> <height> <output.png> [scene] [window title]");
                return 2;
            }

            var width = int.Parse(args[0]);
            var height = int.Parse(args[1]);
            var outPath = args[2];
            var scene = args.Length > 3 && args[3].Length > 0 ? args[3] : "layout";
            var title = args.Length > 4 && args[4].Length > 0 ? args[4] : "Events2Code UI harness";

            // Without this a layout mistake surfaces as a modal error dialog on a machine nobody
            // is looking at, and the run just hangs until it is killed.
            var failures = new List<string>();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => failures.Add(e.Exception.ToString());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var control = new Events2CodeControl { Dock = DockStyle.Fill };
            var form = new QuietForm
            {
                Text = title,
                ClientSize = new Size(width, height)
            };
            form.Controls.Add(control);

            form.Shown += (s, e) =>
            {
                try { Populate(control, scene); }
                catch (Exception ex) { failures.Add(ex.ToString()); }

                // Let the form settle before grabbing it: the lists redistribute their columns on
                // resize and the syntax highlighter repaints asynchronously.
                var settle = new Timer { Interval = 700 };
                settle.Tick += (s2, e2) =>
                {
                    settle.Stop();
                    try { Capture.Of(form, outPath); }
                    catch (Exception ex) { failures.Add(ex.ToString()); }
                    form.Close();
                };
                settle.Start();
            };

            Application.Run(form);

            if (failures.Count == 0) return 0;

            var log = outPath + ".error.txt";
            File.WriteAllText(log, string.Join("\n----\n", failures));
            Console.Error.WriteLine("failed, see " + log);
            return 1;
        }

        private static T Field<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, Priv);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void Populate(Events2CodeControl control, string scene)
        {
            var handlers = scene == "showcase" ? Sample.ShowcaseHandlers() : Sample.LayoutHandlers();
            var formName = scene == "showcase" ? "Contact" : "Portal Contact (Enhanced)";

            FillList(Field<ListView>(control, "_lvEntities"),
                Sample.Entities().Select(e => new[] { e.DisplayName, e.LogicalName }));

            FillList(Field<ListView>(control, "_lvForms"),
                Sample.Forms("contact").Select(f => new[] { f.Name, f.Type == 2 ? "Main" : "Quick Create" }));

            control.GetType().GetField("_handlers", Priv).SetValue(control, handlers);
            control.GetType().GetMethod("FillHandlersGrid", Priv).Invoke(control, null);
            // The grid counts what it drew but leaves the saying of it to the tool's one status
            // line, which knows whether anything is still on its way. Nothing is, here.
            control.GetType().GetMethod("UpdateState", Priv).Invoke(control, null);

            // Through the real generator rather than a canned string, so a screenshot of the code
            // pane is the tool's actual output for the rows above it.
            var code = JsCodeGenerator.Generate(handlers, "MyOrg.FormEvents.onLoad", "contact", formName);
            Highlight(Field<RichTextBox>(control, "_txtCode"), code);

            // Code in the pane and the buttons still greyed is a state the tool is never in, so
            // put them where a real Generate would have left them.
            foreach (var button in new[] { "_btnGenerate", "_btnCopy", "_btnSave", "_btnUnregister" })
                Field<Button>(control, button).Enabled = true;
        }

        /// <summary>
        /// Rows go in without a Tag on purpose: the real SelectedIndexChanged handlers read it and
        /// then go to Dataverse, which the harness has no connection for. Nothing is selected here
        /// for the same reason.
        /// </summary>
        private static void FillList(ListView list, IEnumerable<string[]> rows)
        {
            list.BeginUpdate();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row[0]);
                item.SubItems.Add(row[1]);
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        private static void Highlight(RichTextBox box, string code)
        {
            typeof(Events2CodeControl).Assembly
                .GetType("Events2Code.JsSyntaxHighlighter")
                .GetMethod("Apply", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { box, code });
        }
    }
}
