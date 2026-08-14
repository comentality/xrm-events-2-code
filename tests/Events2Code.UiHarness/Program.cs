using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Events2Code;
using Events2Code.Logic;

namespace Events2Code.UiHarness
{
    /// <summary>
    /// Hosts the tool's control in a bare form, fills it with sample rows and screenshots it, so
    /// layout work can be checked without XrmToolBox and without a Dataverse connection. Run it
    /// through ui.ps1 rather than directly.
    ///
    ///   uiharness.exe &lt;width&gt; &lt;height&gt; &lt;output.png&gt;
    ///
    /// The control is driven the way XrmToolBox drives it - construct, dock, show - so anything
    /// the real tool does on Load and Resize happens here too. Sample data goes in through
    /// reflection: the lists and their backing fields are private, and exposing them for a test
    /// harness would be a worse trade than this file knowing their names.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: uiharness.exe <width> <height> <output.png>");
                return 2;
            }

            var width = int.Parse(args[0]);
            var height = int.Parse(args[1]);
            var outPath = args[2];

            // Without this a layout mistake surfaces as a modal error dialog on a machine nobody
            // is looking at, and the run just hangs until it is killed.
            var failures = new List<string>();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => failures.Add(e.Exception.ToString());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var control = new Events2CodeControl { Dock = DockStyle.Fill };
            var form = new Form
            {
                Text = "Events2Code UI harness",
                ClientSize = new Size(width, height),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                TopMost = true      // CopyFromScreen grabs whatever is on screen, so stay on top of it
            };
            form.Controls.Add(control);

            form.Shown += (s, e) =>
            {
                try { Populate(control); }
                catch (Exception ex) { failures.Add(ex.ToString()); }

                // Let the form settle before grabbing it: the lists redistribute their columns on
                // resize and the syntax highlighter repaints asynchronously.
                var settle = new Timer { Interval = 700 };
                settle.Tick += (s2, e2) =>
                {
                    settle.Stop();
                    try { Capture(form, outPath); }
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

        private static void Capture(Form form, string outPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var bmp = new Bitmap(form.Bounds.Width, form.Bounds.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(form.Bounds.Location, Point.Empty, form.Bounds.Size);
                bmp.Save(outPath, ImageFormat.Png);
            }
        }

        private static T Field<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, Priv);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void Populate(Events2CodeControl control)
        {
            FillList(Field<ListView>(control, "_lvEntities"), new[,]
            {
                { "AI Builder Datasets Container", "msdyn_aibdatasetscontainer" },
                { "BotContent", "msdync_botcontent" },
                { "Contact", "contact" },
                { "Content Snippet", "mspp_contentsnippet" },
                { "ControlConfiguration", "controlconfiguration" },
                { "Custom Control", "customcontrol" },
                { "Custom Control Default Config", "customcontroldefaultconfig" },
                { "Source Control Branch Container", "sourcecontrolbranchcontainer" },
                { "Staged Source Control Container", "stagedsourcecontrolcontainer" },
            });

            FillList(Field<ListView>(control, "_lvForms"), new[,]
            {
                { "Contact", "Main" },
                { "Contact Quick Create", "Quick Create" },
                { "E2E Events Test", "Main" },
                { "Information", "Main" },
                { "Portal Contact (Enhanced)", "Main" },
                { "Profile Web Form (Enhanced)", "Main" },
            });

            // One of each kind the grid renders differently: an internal handler it greys out and
            // refuses to convert, a convertible one it checks, and a disabled one.
            var handlers = new List<EventHandlerInfo>
            {
                new EventHandlerInfo
                {
                    Kind = EventKind.Other,
                    EventName = "setadditionalparams",
                    TargetName = "general",
                    FunctionName = "AppCommon.Contact.Instance.parentCustomerOnChange",
                    LibraryName = "AppCommon/Contact/Contact_Main_Library.js",
                    Parameters = "",
                    Enabled = true,
                    IsInternal = true
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.FormOnLoad,
                    EventName = "onload",
                    TargetName = "",
                    FunctionName = "mspp.subgrid_filtering.filterAssociatedRecords",
                    LibraryName = "mspp_entities/subgrid_filtering.js",
                    Parameters = "",
                    PassExecutionContext = true,
                    Enabled = true
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.AttributeOnChange,
                    EventName = "onchange",
                    TargetName = "parentcustomerid",
                    FunctionName = "MyOrg.Contact.onParentCustomerChanged",
                    LibraryName = "new_/scripts/contact_main.js",
                    Parameters = "\"accountid\", true",
                    PassExecutionContext = true,
                    Enabled = false
                },
            };

            control.GetType().GetField("_handlers", Priv).SetValue(control, handlers);
            control.GetType().GetMethod("FillHandlersGrid", Priv).Invoke(control, null);

            Highlight(Field<RichTextBox>(control, "_txtCode"), SampleCode);
        }

        /// <summary>
        /// Rows go in without a Tag on purpose: the real SelectedIndexChanged handlers read it and
        /// then go to Dataverse, which the harness has no connection for. Nothing is selected here
        /// for the same reason.
        /// </summary>
        private static void FillList(ListView list, string[,] rows)
        {
            list.BeginUpdate();
            for (var i = 0; i < rows.GetLength(0); i++)
            {
                var item = new ListViewItem(rows[i, 0]);
                item.SubItems.Add(rows[i, 1]);
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

        private const string SampleCode =
            "// Generated by Events2Code (XrmToolBox) on 2026-08-14\n" +
            "// Entity: contact   Form: Portal Contact (Enhanced)\n" +
            "// Register ONLY this function on the form OnLoad event (pass execution context).\n" +
            "\"use strict\";\n" +
            "\n" +
            "var MyOrg = MyOrg || {};\n" +
            "MyOrg.FormEvents = MyOrg.FormEvents || {};\n" +
            "\n" +
            "MyOrg.FormEvents.onLoad = function (executionContext) {\n" +
            "    var formContext = executionContext.getFormContext();\n" +
            "\n" +
            "    // --- Original form OnLoad handlers ---\n" +
            "    mspp.subgrid_filtering.filterAssociatedRecords(executionContext);\n" +
            "};\n";
    }
}
