using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Events2Code.SlowHarness
{
    /// <summary>
    /// The tool, driven by hand, against an environment that answers in seconds. Run it through
    /// slow.ps1 rather than directly.
    ///
    ///   slowharness.exe &lt;output folder&gt; [scenario ...]
    ///
    /// Every scenario gets a window and a shot per beat of its own, and prints one line per
    /// finding. The exit code is the number of scenarios that had any.
    ///
    /// It hosts the same control the UI harness photographs, but hands it a
    /// <see cref="SlowService"/> through the connection property XrmToolBox would have set, so
    /// the query code runs for real and a form that takes three seconds to arrive here takes
    /// three seconds the way it would on a bad link.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: slowharness.exe <output folder> [scenario ...]");
                return 2;
            }

            var root = Path.GetFullPath(args[0]);
            var wanted = args.Skip(1).ToList();

            var scenarios = Scenarios.All();
            if (wanted.Count > 0)
            {
                var unknown = wanted.Where(w => scenarios.All(s => s.Name != w)).ToList();
                if (unknown.Count > 0)
                {
                    Console.Error.WriteLine("no such scenario: " + string.Join(", ", unknown));
                    Console.Error.WriteLine("there is: " + string.Join(", ", scenarios.Select(s => s.Name)));
                    return 2;
                }

                scenarios = scenarios.Where(s => wanted.Contains(s.Name)).ToList();
            }

            // A layout mistake or a disposed control otherwise surfaces as a modal dialog on a
            // machine nobody is looking at. Caught here, it is a finding like any other - and
            // catching it is how close-during-load has anything to report at all.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Directory.CreateDirectory(root);

            var failed = 0;
            var report = new List<string>();
            foreach (var scenario in scenarios)
            {
                var findings = scenario.Play(root);
                var ok = findings.Count == 0;
                if (!ok) failed++;

                Write(ok ? ConsoleColor.Green : ConsoleColor.Red, (ok ? "  ok  " : "FAILED") + "  ");
                Console.WriteLine(scenario.Name.PadRight(22) + scenario.Why);
                report.Add((ok ? "ok      " : "FAILED  ") + scenario.Name + "  -  " + scenario.Why);

                foreach (var finding in findings)
                {
                    Console.WriteLine("          " + finding);
                    report.Add("          " + finding);
                }
            }

            File.WriteAllLines(Path.Combine(root, "report.txt"), report);

            Console.WriteLine();
            Console.WriteLine(scenarios.Count - failed + " of " + scenarios.Count
                              + " scenarios clean.  Shots and report in " + root);
            return failed;
        }

        private static void Write(ConsoleColor colour, string text)
        {
            var was = Console.ForegroundColor;
            Console.ForegroundColor = colour;
            Console.Write(text);
            Console.ForegroundColor = was;
        }
    }
}
