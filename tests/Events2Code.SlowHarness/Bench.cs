using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Events2Code.Harness;
using ThreadExceptionEventHandler = System.Threading.ThreadExceptionEventHandler;

namespace Events2Code.SlowHarness
{
    /// <summary>One thing that happens, at a moment, to a tool somebody is using.</summary>
    public class Beat
    {
        public int At;
        public string Label;
        public Action<Run> Do;
    }

    /// <summary>
    /// A tool in a window, an environment that answers slowly, and a list of things that go wrong.
    /// Handed to every beat of a scenario.
    /// </summary>
    public class Run
    {
        public Form Form;
        public Events2CodeControl Control;
        public Probe Probe;
        public SlowService Service;
        public string ShotDir;

        /// <summary>Everything the run has to answer for, findings and crashes alike.</summary>
        public readonly List<string> Failures = new List<string>();

        /// <summary>Captions of every dialog the tool put up, in order, closed as they appeared.</summary>
        public readonly List<string> Dialogs = new List<string>();

        /// <summary>The backups folder as it stood before this scenario pressed anything.</summary>
        public List<string> BackupsBefore = new List<string>();

        /// <summary>Backup files the tool has written since. One write, one backup.</summary>
        public List<string> NewBackups()
        {
            return Backups.All().Except(BackupsBefore).OrderBy(p => p).ToList();
        }

        public Stopwatch Clock = new Stopwatch();

        public void Check(bool ok, string what)
        {
            if (!ok) Failures.Add(Clock.ElapsedMilliseconds + "ms  " + what);
        }

        /// <summary>
        /// Hands the tool a different environment, the way XrmToolBox does when the connection in
        /// the toolbar changes. The empty action name is what a plain change carries; anything
        /// else and the base class goes looking for a method of that name to run.
        /// </summary>
        public void SwitchTo(SlowService service)
        {
            Control.UpdateConnection(service, new McTools.Xrm.Connection.ConnectionDetail(), "", null);
            Service = service;
        }
    }

    public class Scenario
    {
        public string Name;

        /// <summary>One line, printed beside the result: which finding this is about.</summary>
        public string Why;

        /// <summary>How the environment behaves, set before anything is pressed.</summary>
        public Action<SlowService> Wire;

        public readonly List<Beat> Beats = new List<Beat>();

        public Scenario At(int ms, string label, Action<Run> what)
        {
            Beats.Add(new Beat { At = ms, Label = label, Do = what });
            return this;
        }

        /// <summary>
        /// Runs the whole scenario in a window of its own, and returns what went wrong.
        ///
        /// Everything happens on the UI thread with a live message loop, because that is where
        /// the tool lives: WorkAsync marshals its callback back here, and a scenario that drove
        /// the control from anywhere else would be testing something nobody does.
        /// </summary>
        public List<string> Play(string root)
        {
            var run = new Run { ShotDir = Path.Combine(root, Name) };

            if (Directory.Exists(run.ShotDir)) Directory.Delete(run.ShotDir, true);
            Directory.CreateDirectory(run.ShotDir);

            // The tool backs a form up before rewriting it, to a folder of its own under the
            // user's Documents, and that path is not the harness's to redirect. So note what was
            // there before, and take away only what this scenario made.
            run.BackupsBefore = Backups.All();

            run.Service = SlowService.Sampled();
            if (Wire != null) Wire(run.Service);

            run.Control = new Events2CodeControl { Dock = DockStyle.Fill };
            Connect(run.Control, run.Service);
            run.Probe = new Probe(run.Control);

            run.Form = new QuietForm
            {
                Text = "Events2Code — " + Name,
                ClientSize = new Size(1280, 900)
            };
            run.Form.Controls.Add(run.Control);

            ThreadExceptionEventHandler onThreadException = (s, e) =>
                run.Failures.Add(run.Clock.ElapsedMilliseconds + "ms  the tool threw: " + Head(e.Exception));
            Application.ThreadException += onThreadException;

            var sweeper = new Dialogs(run);

            run.Form.Shown += (s, e) =>
            {
                run.Clock.Start();
                sweeper.Start();

                var queue = new Queue<Beat>(Beats.OrderBy(b => b.At));
                var shot = new int[1];
                var beating = false;
                var pump = new Timer { Interval = 25 };
                pump.Tick += (s2, e2) =>
                {
                    // A beat that puts up a modal is still inside its own gesture, and a modal
                    // pumps messages - including this timer. Without the guard the next beat
                    // would fire from inside the last one, which is not a thing a person can do.
                    if (beating) return;
                    beating = true;
                    try
                    {
                        Due(run, queue, shot);
                    }
                    finally
                    {
                        beating = false;
                    }

                    if (queue.Count > 0) return;

                    pump.Stop();
                    sweeper.Stop();
                    run.Form.Close();
                };
                pump.Start();
            };

            try
            {
                Application.Run(run.Form);
            }
            finally
            {
                Application.ThreadException -= onThreadException;
                sweeper.Stop();
                if (!run.Control.IsDisposed) run.Control.Dispose();
                run.Form.Dispose();

                foreach (var made in run.NewBackups())
                {
                    try { File.Delete(made); }
                    catch (IOException) { }
                }
            }

            return run.Failures;
        }

        /// <summary>Every beat that has come due, in order, each with a shot of what it left.</summary>
        private static void Due(Run run, Queue<Beat> queue, int[] shot)
        {
            while (queue.Count > 0 && run.Clock.ElapsedMilliseconds >= queue.Peek().At)
            {
                var beat = queue.Dequeue();
                try
                {
                    beat.Do(run);
                }
                catch (Exception ex)
                {
                    run.Failures.Add(run.Clock.ElapsedMilliseconds + "ms  \"" + beat.Label
                                     + "\" threw: " + Head(ex));
                }

                // A shot per beat, so a failure is something to look at rather than only a line
                // to read. Named in order, because the order is the story.
                try
                {
                    if (!run.Form.IsDisposed)
                    {
                        Capture.Of(run.Form, Path.Combine(run.ShotDir,
                            (++shot[0]).ToString("00") + "-" + Slug(beat.Label) + ".png"));
                    }
                }
                catch (Exception)
                {
                    // A shot that cannot be taken is not worth failing a scenario over.
                }
            }
        }

        /// <summary>
        /// Hands the control its connection, the way XrmToolBox does. The setter is protected,
        /// because outside a host nothing has any business setting one - so a harness that is
        /// standing in for the host reaches it the way it reaches everything else here.
        /// </summary>
        private static void Connect(Events2CodeControl control, Microsoft.Xrm.Sdk.IOrganizationService service)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.DeclaredOnly;

            for (var type = control.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty("Service", any);
                var setter = property == null ? null : property.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(control, new object[] { service });
                    return;
                }

                var backing = type.GetField("<Service>k__BackingField", any);
                if (backing != null)
                {
                    backing.SetValue(control, service);
                    return;
                }
            }

            throw new MissingMemberException("PluginControlBase", "Service");
        }

        private static string Head(Exception ex)
        {
            var line = ex.GetBaseException().ToString();
            var stop = line.IndexOf('\n');
            return (stop < 0 ? line : line.Substring(0, stop)).Trim();
        }

        private static string Slug(string label)
        {
            var sb = new StringBuilder();
            foreach (var c in label ?? "beat")
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }

            return sb.ToString().Trim('-');
        }
    }

    /// <summary>
    /// The folder the tool copies a form into before rewriting it. Not the harness's to redirect
    /// - it is where the tool puts them, under the user's own Documents - so a scenario counts
    /// what appeared there rather than pointing it somewhere else.
    /// </summary>
    public static class Backups
    {
        public static List<string> All()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Events2Code", "backups");
            return Directory.Exists(dir) ? Directory.GetFiles(dir).ToList() : new List<string>();
        }
    }

    /// <summary>
    /// Answers whatever modal the tool puts up, and writes down that it did.
    ///
    /// Nothing here is a workaround for the tool being awkward: a message box is a perfectly good
    /// way to confirm a write, and a run with nobody at the keyboard has to answer it or hang.
    /// The captions are worth keeping either way - "one dialog, not two" is exactly the assertion
    /// a double press is caught by.
    ///
    /// A Yes/No box is answered Yes, because the scenarios that reach one are about what happens
    /// after somebody agrees to the write. It has to be a click on its Yes button: a confirmation
    /// has no close box, and posting it WM_CLOSE would be ignored and hang the run.
    /// </summary>
    internal class Dialogs
    {
        private const int WM_CLOSE = 0x0010;
        private const int BM_CLICK = 0x00F5;

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadDelegate callback, IntPtr param);

        private delegate bool EnumThreadDelegate(IntPtr hwnd, IntPtr param);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumThreadDelegate callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

        private readonly Run _run;
        private readonly Timer _timer;

        public Dialogs(Run run)
        {
            _run = run;
            // Faster than a person, slower than the message loop. A modal dialog runs its own
            // loop and pumps WM_TIMER, which is the whole reason this works from inside one.
            _timer = new Timer { Interval = 120 };
            _timer.Tick += (s, e) => Sweep();
        }

        public void Start() { _timer.Start(); }

        public void Stop() { _timer.Stop(); _timer.Dispose(); }

        private void Sweep()
        {
            var mine = _run.Form.IsDisposed ? IntPtr.Zero : _run.Form.Handle;
            EnumThreadWindows(GetCurrentThreadId(), (hwnd, param) =>
            {
                if (hwnd == mine || !IsWindowVisible(hwnd)) return true;

                var caption = new StringBuilder(260);
                GetWindowText(hwnd, caption, caption.Capacity);
                var text = caption.ToString();
                if (text.Length == 0) return true;

                _run.Dialogs.Add(text);

                var yes = Yes(hwnd);
                if (yes != IntPtr.Zero) PostMessage(yes, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                else PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        /// <summary>The dialog's Yes button, if it has one. The ampersand is the accelerator.</summary>
        private static IntPtr Yes(IntPtr dialog)
        {
            var found = IntPtr.Zero;
            EnumChildWindows(dialog, (child, param) =>
            {
                var className = new StringBuilder(64);
                GetClassName(child, className, className.Capacity);
                if (className.ToString() != "Button") return true;

                var text = new StringBuilder(64);
                GetWindowText(child, text, text.Capacity);
                if (text.ToString().Replace("&", "") != "Yes") return true;

                found = child;
                return false;
            }, IntPtr.Zero);

            return found;
        }
    }
}
