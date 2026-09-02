using System;
using System.Collections.Generic;
using System.Linq;

namespace Events2Code.SlowHarness
{
    /// <summary>
    /// One scenario per thing a slow link does to this tool. Each is a sequence of ordinary
    /// gestures at ordinary intervals, against an environment that takes seconds rather than
    /// milliseconds, and a handful of questions asked of the window in between.
    ///
    /// None of them is about the JavaScript the tool emits - that is what the E2E tests and the
    /// generator fixtures are for. They are about the window while the answer is still on its
    /// way: which buttons a person can still press, what the status line is claiming, and whether
    /// the answer that lands last is the answer to the question asked last.
    /// </summary>
    public static class Scenarios
    {
        private const string Contact = "Contact";
        private const string Bot = "BotContent";
        private const string Datasets = "AI Builder Datasets Container";

        /// <summary>The form the showcase handlers are on, and one of the plainer ones.</summary>
        private const string ShowcaseForm = "Contact";
        private const string PlainForm = "Portal Contact (Enhanced)";
        private const string EmptyForm = "Information";

        /// <summary>Only the showcase form has one, so it says which form the grid belongs to.</summary>
        private const string ShowcaseOnly = "MyOrg.Contact.onSave";
        private const string PlainOnly = "mspp.subgrid_filtering.filterAssociatedRecords";

        /// <summary>
        /// A link where one kind of question is slow and the rest are merely remote. Latency is
        /// scripted per call so a scenario's clock is readable: the first forms fetch can take
        /// three seconds and the second three hundred milliseconds, which is the whole of what
        /// an out-of-order answer needs to happen.
        /// </summary>
        private static Func<Call, int> Slow(string what, params int[] perCall)
        {
            return call =>
            {
                if (call.What != what) return 20;
                return perCall[Math.Min(call.Nth - 1, perCall.Length - 1)];
            };
        }

        public static List<Scenario> All()
        {
            return new List<Scenario>
            {
                CloseDuringLoad(),
                LoadTwice(),
                StaleForms(),
                StaleHandlers(),
                ActWhileLoading(),
                PartialTruth(),
                Cancel(),
                LoadFails(),
                FormsFail(),
                WriteGuarded(),
                SwitchOrgLetsGo(),
            };
        }

        /// <summary>
        /// Somebody gives up and closes the tab while the environment is still thinking. The
        /// answer arrives afterwards and is handed to a control that no longer exists.
        ///
        /// The rendering itself turns out to survive it here - a ListView with no handle left
        /// takes rows into its own list and draws nothing, so nothing throws. What does not
        /// survive is the reporting: seconds after the tab is gone, the failed fetch puts a modal
        /// error up over whatever the person moved on to, about a tool that is not on screen any
        /// more. The callback has to notice it is talking to nobody before it says anything.
        /// </summary>
        private static Scenario CloseDuringLoad()
        {
            return new Scenario
            {
                Name = "close-during-load",
                Why = "a fetch landing on a closed tab must neither throw nor say anything",
                Wire = s =>
                {
                    s.Latency = Slow("formxml", 3000);
                    s.Fails = call => call.What == "formxml"
                        ? new TimeoutException("The request channel timed out while waiting for a reply.")
                        : null;
                }
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1400, "click a form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2000, "close the tab", r =>
                {
                    r.Form.Controls.Remove(r.Control);
                    r.Control.Dispose();
                })
                .At(6000, "let the answer land on nobody", r =>
                {
                    r.Check(r.Service.Log("formxml").Count == 1,
                        "the fetch should have gone out exactly once");
                    r.Check(r.Dialogs.Count == 0,
                        "a closed tab must not put a dialog up afterwards, and put up: "
                        + string.Join(" | ", r.Dialogs));
                });
        }

        /// <summary>
        /// Load Tables pressed again because the first press did not appear to do anything. It is
        /// the most natural thing in the world on a slow link, and it costs a second full
        /// RetrieveAllEntities - the heaviest request the tool makes - whose answer then rebuilds
        /// the list underneath whatever was being pointed at.
        /// </summary>
        private static Scenario LoadTwice()
        {
            return new Scenario
            {
                Name = "load-twice",
                Why = "Load Tables must be dead while it is loading",
                Wire = s => s.Latency = Slow("tables", 2500)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "press it again", r =>
                {
                    r.Check(!r.Probe.Load.Enabled, "Load Tables should be dead while it is loading");
                    r.Probe.PressLoad();
                })
                .At(1600, "and again", r => r.Probe.PressLoad())
                .At(5000, "count the round trips", r =>
                {
                    r.Check(r.Service.Log("tables").Count == 1,
                        "three presses, one query, and there were " + r.Service.Log("tables").Count);
                    r.Check(r.Probe.Load.Enabled, "and Load Tables should be live again afterwards");
                });
        }

        /// <summary>
        /// Two tables clicked in a row, which is what browsing a list is. The first table's forms
        /// take three seconds and the second's take three hundred milliseconds, so the answer to
        /// the question nobody is asking any more lands last and wins.
        ///
        /// What is left is a forms list belonging to one table under a selection pointing at
        /// another - and the next click loads a form the selected table does not have.
        /// </summary>
        private static Scenario StaleForms()
        {
            return new Scenario
            {
                Name = "stale-forms",
                Why = "the forms shown must belong to the table selected last",
                Wire = s => s.Latency = Slow("forms", 3000, 300)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click a table", r => r.Probe.SelectEntity(Datasets))
                .At(1400, "change your mind", r => r.Probe.SelectEntity(Contact))
                .At(6000, "read the forms list", r =>
                {
                    var forms = r.Probe.FormNames();
                    r.Check(forms.Contains(ShowcaseForm),
                        "the list should hold Contact's forms, and holds: " + string.Join(", ", forms));
                    r.Check(!forms.Any(f => f.StartsWith("msdyn_", StringComparison.Ordinal)),
                        "the abandoned table's answer should have landed on nobody");
                    r.Check(r.Service.Log("forms").Count == 2,
                        "both clicks ask, which is fine; only the last one may answer");
                });
        }

        /// <summary>
        /// The same race one level down, and the one that ends in a wrong write: two forms of the
        /// same table clicked in a row, and the slower answer lands last. The grid then holds one
        /// form's handlers while the selection - and everything Unregister rewrites - points at
        /// another.
        /// </summary>
        private static Scenario StaleHandlers()
        {
            return new Scenario
            {
                Name = "stale-handlers",
                Why = "the handlers shown must belong to the form selected last",
                Wire = s => s.Latency = Slow("formxml", 3000, 300)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1400, "click a form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2000, "change your mind", r => r.Probe.SelectForm(PlainForm))
                .At(6500, "read the grid", r =>
                {
                    var functions = r.Probe.HandlerFunctions();
                    r.Check(functions.Contains(PlainOnly),
                        "the grid should hold the second form's handlers, and holds: "
                        + string.Join(", ", functions));
                    r.Check(!functions.Contains(ShowcaseOnly),
                        "the abandoned form's answer should have landed on nobody");
                });
        }

        /// <summary>
        /// Generate and Unregister are offered while the form they would act on is still arriving.
        /// Both read the grid, and the grid still holds the form before this one - so Generate
        /// writes a file of the wrong form's handlers under the right form's name, and Unregister
        /// would strip handlers off a form nobody was looking at.
        /// </summary>
        private static Scenario ActWhileLoading()
        {
            return new Scenario
            {
                Name = "act-while-loading",
                Why = "nothing that acts on a form may be pressable while the form is loading",
                Wire = s => s.Latency = Slow("formxml", 300, 3000)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1400, "click a form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2000, "generate from it", r =>
                {
                    r.Check(r.Probe.Generate.Enabled, "Generate should be live once the form has landed");
                    r.Probe.CheckAllHandlers();
                    r.Probe.PressGenerate();
                    r.Check(r.Probe.Unregister.Enabled, "and Unregister behind it");
                })
                .At(2600, "click another form", r => r.Probe.SelectForm(PlainForm))
                .At(3000, "while it is still coming", r =>
                {
                    r.Check(!r.Probe.Generate.Enabled,
                        "Generate must be dead while the form it would read is loading");
                    r.Check(!r.Probe.Unregister.Enabled,
                        "Unregister must be dead while the form it would rewrite is loading");
                    r.Check(!r.Probe.Save.Enabled,
                        "and Save must not offer the previous form's code under this form's name");
                })
                .At(7000, "and once it lands", r =>
                {
                    r.Check(r.Probe.Generate.Enabled, "Generate should come back");
                    r.Check(r.Probe.HandlerFunctions().Contains(PlainOnly),
                        "with the grid holding the form that was asked for");
                    r.Check(r.Service.Log("update").Count == 0, "and nothing should have been written");
                });
        }

        /// <summary>
        /// The status line while a form is on its way. "7 handler(s), 5 convertible" is a sentence
        /// about the form selected before this one, sitting under a selection that has moved on;
        /// and once the form does land empty, "No handlers registered on this form" is a claim
        /// about a form the tool has read rather than about one it is still reading.
        ///
        /// Both un-say themselves when the network catches up, which is the worst way for a tool
        /// to be wrong: it is only wrong while somebody is waiting and looking at it.
        /// </summary>
        private static Scenario PartialTruth()
        {
            return new Scenario
            {
                Name = "partial-truth",
                Why = "the status line must not state as fact what has not arrived",
                Wire = s => s.Latency = Slow("formxml", 300, 3000)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1400, "click the busy form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2000, "which is what it says", r =>
                    r.Check(r.Probe.Status.IndexOf("handler", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the status should count the handlers it has, and says \"" + r.Probe.Status + "\""))
                .At(2400, "click a form with none", r => r.Probe.SelectForm(EmptyForm))
                .At(2900, "while its answer is still out", r =>
                {
                    r.Check(r.Probe.Status.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the status should own up to the fetch, and says \"" + r.Probe.Status + "\"");
                    r.Check(r.Probe.Status.IndexOf("convertible", StringComparison.OrdinalIgnoreCase) < 0,
                        "nothing may be counted while the form it would count is loading");
                })
                .At(7000, "and once it lands", r =>
                    r.Check(r.Probe.Status.IndexOf("No handlers", StringComparison.OrdinalIgnoreCase) >= 0,
                        "an empty form should read as empty, and says \"" + r.Probe.Status + "\""));
        }

        /// <summary>
        /// The panel's Cancel button, which the tool never offered because no fetch was marked
        /// cancelable - so pressing it does nothing at best, and throws at worst.
        ///
        /// RetrieveAllEntities is the one request here big enough to be worth abandoning: it is
        /// every table in the environment, and on a slow link it is most of a minute. The query
        /// on the wire cannot be recalled, but the tool can stop waiting for it, say so, and give
        /// itself back.
        /// </summary>
        private static Scenario Cancel()
        {
            return new Scenario
            {
                Name = "cancel",
                Why = "a fetch must be abandonable, and must leave nothing stuck behind",
                Wire = s => s.Latency = Slow("tables", 3000, 200)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(1000, "give up", r => r.Probe.PressCancel())
                .At(4500, "nothing landed", r =>
                {
                    r.Check(r.Probe.Entities.Items.Count == 0,
                        "a cancelled load should not fill the list behind your back");
                    r.Check(r.Probe.Status.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0,
                        "and should say so rather than looking like an empty environment: \""
                        + r.Probe.Status + "\"");
                    r.Check(r.Probe.Load.Enabled, "the tool should be usable again");
                })
                .At(4800, "press Load Tables again", r => r.Probe.PressLoad())
                .At(6500, "and this time it lands", r =>
                {
                    r.Check(r.Probe.Entities.Items.Count > 0, "the second load should fill the list");
                    r.Check(r.Probe.Status.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) < 0,
                        "with the cancellation gone from the status line");
                });
        }

        /// <summary>
        /// The link gives up rather than answering. A timeout is what a slow network does when it
        /// is slow enough, and it is the one path here that ends in a dialog somebody has to read.
        ///
        /// A dialog is read once and dismissed. What is on screen afterwards is an empty table
        /// list, which reads as an environment with nothing in it - and if the flag that holds
        /// Load Tables down does not come up on the failure path, that is a tool bricked by one
        /// timeout, with no way on but closing the tab.
        /// </summary>
        private static Scenario LoadFails()
        {
            return new Scenario
            {
                Name = "load-fails",
                Why = "a load that fails must say so, and must give the tool back",
                Wire = s =>
                {
                    s.Latency = Slow("tables", 1500, 200);
                    s.Fails = call => call.What == "tables" && call.Nth == 1
                        ? new TimeoutException("The request channel timed out while waiting for a reply.")
                        : null;
                }
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(3000, "the tool is still a tool", r =>
                {
                    r.Check(r.Probe.Load.Enabled, "Load must come back, or there is no way on from here");
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0,
                        "and the status line should say why the list is empty: \"" + r.Probe.Status + "\"");
                })
                .At(3200, "press Load Tables again", r => r.Probe.PressLoad())
                .At(5000, "and this time it lands", r =>
                {
                    r.Check(r.Probe.Entities.Items.Count > 0, "the second load should fill the list");
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) < 0,
                        "with the failure gone from the status line");
                });
        }

        /// <summary>
        /// A table whose forms cannot be read, clicked after one whose forms could. The dialog is
        /// dismissed and the forms of the previous table are still listed underneath the new
        /// selection - so the list is not merely stale, it is a straight answer to a question
        /// about a table that was never answered.
        /// </summary>
        private static Scenario FormsFail()
        {
            return new Scenario
            {
                Name = "forms-fail",
                Why = "a table whose forms failed must not show another table's forms",
                Wire = s =>
                {
                    s.Latency = Slow("forms", 200, 1200);
                    s.Fails = call => call.What == "forms" && call.Nth == 2
                        ? new TimeoutException("The request channel timed out while waiting for a reply.")
                        : null;
                }
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1600, "its forms are listed", r =>
                    r.Check(r.Probe.FormNames().Contains(ShowcaseForm), "the first table's forms should land"))
                .At(1800, "click a table that cannot be read", r => r.Probe.SelectEntity(Bot))
                .At(4000, "read what is left", r =>
                {
                    var forms = r.Probe.FormNames();
                    r.Check(!forms.Contains(ShowcaseForm),
                        "the previous table's forms must not stand under this selection: "
                        + string.Join(", ", forms));
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0,
                        "and the status line should say the forms could not be read: \""
                        + r.Probe.Status + "\"");
                });
        }

        /// <summary>
        /// Unregister pressed twice, because the first press put up a panel that disabled nothing
        /// and the form is still on screen looking untouched.
        ///
        /// This is the write path, and it is the worst of them: two rewrites of the same form, two
        /// backups a second apart, and the second write takes as its "before" state a form the
        /// first write had already stripped - so the backup that is meant to be the way back is a
        /// copy of the damage.
        /// </summary>
        private static Scenario WriteGuarded()
        {
            return new Scenario
            {
                Name = "write-guarded",
                Why = "Unregister must be dead while its own write is running",
                Wire = s => s.Latency = Slow("update", 2500)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(800, "click Contact", r => r.Probe.SelectEntity(Contact))
                .At(1400, "click a form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2000, "generate", r =>
                {
                    r.Probe.CheckAllHandlers();
                    r.Probe.PressGenerate();
                })
                .At(2400, "press Unregister and say yes", r =>
                {
                    r.Probe.PressUnregister();
                    // Back on the UI thread, so the write cannot have finished and reported
                    // back: whatever the button says now is what a second press would meet.
                    r.Check(!r.Probe.Unregister.Enabled, "a second press should be impossible mid-write");
                })
                .At(3000, "press it again anyway", r => r.Probe.PressUnregister())
                .At(8000, "count what reached the environment", r =>
                {
                    r.Check(r.Service.Log("update").Count == 1,
                        "one press, one update, and there were " + r.Service.Log("update").Count);
                    r.Check(r.Service.Log("publish").Count == 1,
                        "one press, one publish, and there were " + r.Service.Log("publish").Count);
                    r.Check(r.NewBackups().Count == 1,
                        "one press, one backup, and there were " + r.NewBackups().Count
                        + " - the second is a copy of the first write's damage");
                    r.Check(r.Dialogs.Count(d => d.IndexOf("Done", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                        "one write, one report, and the dialogs were: " + string.Join(" | ", r.Dialogs));
                });
        }

        /// <summary>
        /// The connection changes with a form open and its handlers still on the way.
        ///
        /// Everything on screen was read from the org being left, and the form is the one that
        /// matters: its id is what Unregister and Generate write back to, so a form left selected
        /// across a connection change is a write aimed at one environment landing in another. The
        /// window has to let go of all of it, and the answer still in the air has to land on
        /// nobody - the same rule as any other question asked here and then abandoned.
        /// </summary>
        private static Scenario SwitchOrgLetsGo()
        {
            return new Scenario
            {
                Name = "switch-org-lets-go",
                Why = "nothing read from one environment may be left on screen, or written to, in another",
                Wire = s => s.Latency = Slow("formxml", 3000)
            }
                .At(200, "press Load Tables", r => r.Probe.PressLoad())
                .At(1200, "click a table", r => r.Probe.SelectEntity(Contact))
                .At(2200, "open a form", r => r.Probe.SelectForm(ShowcaseForm))
                .At(2600, "switch to another org while its handlers are still coming", r =>
                {
                    var old = r.Service;
                    r.SwitchTo(SlowService.Sampled());

                    r.Check(r.Probe.EntityNames().Count == 0,
                        "the last org's tables should have gone with its connection, and the list holds: "
                        + string.Join(", ", r.Probe.EntityNames()));
                    r.Check(r.Probe.FormNames().Count == 0,
                        "and its forms, and the list holds: " + string.Join(", ", r.Probe.FormNames()));
                    r.Check(r.Probe.HandlerFunctions().Count == 0,
                        "and its handlers, and the grid holds: "
                        + string.Join(", ", r.Probe.HandlerFunctions()));
                    r.Check(r.Probe.Code.Length == 0,
                        "and the code written from them, which reads: " + r.Probe.Code);
                    r.Check(!r.Probe.Unregister.Enabled && !r.Probe.Generate.Enabled,
                        "with nothing to write to, neither writing button may be pressable");
                    r.Check(r.Probe.Load.Enabled,
                        "and Load should be live for the new org rather than dead behind an abandoned fetch");

                    r.Service = old;
                })
                .At(6000, "the old org's answer lands on nobody", r =>
                {
                    r.Check(r.Probe.HandlerFunctions().Count == 0,
                        "the form opened in the last org must not fill this one's grid, and it holds: "
                        + string.Join(", ", r.Probe.HandlerFunctions()));
                    r.Check(!r.Probe.Unregister.Enabled,
                        "nor may it make a write possible against an environment it was never read from");
                    r.Check(r.Service.Log("update").Count == 0,
                        "and nothing may have been written, but the old org saw "
                        + r.Service.Log("update").Count + " updates");
                });
        }
    }
}
