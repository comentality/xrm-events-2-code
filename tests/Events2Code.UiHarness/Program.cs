using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
            var form = new Form
            {
                Text = title,
                ClientSize = new Size(width, height),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                TopMost = true      // CopyFromScreen grabs whatever is on screen, so stay on top of it
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

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Asks the window for its own pixels rather than reading them off the screen: scraping the
        /// screen fails outright on a locked workstation, and picks up whatever happens to be in
        /// front otherwise. Screen capture stays as a fallback for anything PrintWindow refuses.
        /// </summary>
        private static void Capture(Form form, string outPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var bmp = new Bitmap(form.Bounds.Width, form.Bounds.Height))
            {
                var printed = false;
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try { printed = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }

                if (!printed)
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

        private static void Populate(Events2CodeControl control, string scene)
        {
            var handlers = scene == "showcase" ? ShowcaseHandlers() : LayoutHandlers();
            var formName = scene == "showcase" ? "Contact" : "Portal Contact (Enhanced)";

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

            control.GetType().GetField("_handlers", Priv).SetValue(control, handlers);
            control.GetType().GetMethod("FillHandlersGrid", Priv).Invoke(control, null);

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
        /// One of each kind the grid renders differently: an internal handler it greys out and
        /// refuses to convert, a convertible one it checks, and a disabled one.
        /// </summary>
        private static List<EventHandlerInfo> LayoutHandlers()
        {
            return new List<EventHandlerInfo>
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
        }

        /// <summary>
        /// A form carrying one of every event the tool can convert, plus the two it cannot, so a
        /// single screenshot shows the whole vocabulary of the grid and of the generated code.
        /// </summary>
        private static List<EventHandlerInfo> ShowcaseHandlers()
        {
            return new List<EventHandlerInfo>
            {
                new EventHandlerInfo
                {
                    Kind = EventKind.FormOnLoad,
                    EventName = "onload",
                    TargetName = "",
                    FunctionName = "MyOrg.Contact.onLoad",
                    LibraryName = "new_/scripts/contact_main.js",
                    Parameters = "",
                    PassExecutionContext = true,
                    Enabled = true
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.FormOnSave,
                    EventName = "onsave",
                    TargetName = "",
                    FunctionName = "MyOrg.Contact.onSave",
                    LibraryName = "new_/scripts/contact_main.js",
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
                    Enabled = true
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.AttributeOnChange,
                    EventName = "onchange",
                    TargetName = "emailaddress1",
                    FunctionName = "MyOrg.Contact.validateEmail",
                    LibraryName = "new_/scripts/contact_validation.js",
                    Parameters = "",
                    PassExecutionContext = false,
                    Enabled = false
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.TabStateChange,
                    EventName = "tabstatechange",
                    TargetName = "tab_details",
                    FunctionName = "MyOrg.Contact.onDetailsTabToggled",
                    LibraryName = "new_/scripts/contact_main.js",
                    Parameters = "",
                    PassExecutionContext = true,
                    Enabled = true
                },
                new EventHandlerInfo
                {
                    Kind = EventKind.GridOnLoad,
                    EventName = "onload",
                    TargetName = "Cases",
                    FunctionName = "MyOrg.Contact.onCasesGridLoad",
                    LibraryName = "new_/scripts/contact_grids.js",
                    Parameters = "",
                    PassExecutionContext = true,
                    Enabled = true
                },
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
            };
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
    }
}
