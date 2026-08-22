using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Events2Code.Logic;

namespace Events2Code.Harness
{
    /// <summary>A table, as the tool lists it.</summary>
    public class SampleEntity
    {
        public string LogicalName;
        public string DisplayName;
    }

    /// <summary>A form, and the handlers registered on it.</summary>
    public class SampleForm
    {
        public Guid Id;
        public string Name;
        public string Entity;
        public int Type = 2;                 // 2 main, 7 quick create
        public bool IsManaged;
        public List<EventHandlerInfo> Handlers = new List<EventHandlerInfo>();

        /// <summary>The form XML the environment hands back for this form.</summary>
        public string Xml { get { return Sample.FormXml(Handlers); } }
    }

    /// <summary>
    /// One environment, shared by both harnesses so the two cannot disagree about what "the
    /// sample" is: the UI harness photographs these rows, and the slow harness serves them from a
    /// fake Dataverse a scenario can make take seconds.
    ///
    /// The forms carry real form XML, built here from the same handler lists the screenshots use
    /// and parsed back by the parser the tool ships - so a scenario that says "the grid should
    /// hold the handlers of the form selected last" is comparing against what the tool would
    /// really have read out of that form.
    /// </summary>
    public static class Sample
    {
        public static List<SampleEntity> Entities()
        {
            return new List<SampleEntity>
            {
                Entity("msdyn_aibdatasetscontainer", "AI Builder Datasets Container"),
                Entity("msdync_botcontent", "BotContent"),
                Entity("contact", "Contact"),
                Entity("mspp_contentsnippet", "Content Snippet"),
                Entity("controlconfiguration", "ControlConfiguration"),
                Entity("customcontrol", "Custom Control"),
                Entity("customcontroldefaultconfig", "Custom Control Default Config"),
                Entity("sourcecontrolbranchcontainer", "Source Control Branch Container"),
                Entity("stagedsourcecontrolcontainer", "Staged Source Control Container"),
            };
        }

        private static SampleEntity Entity(string logicalName, string displayName)
        {
            return new SampleEntity { LogicalName = logicalName, DisplayName = displayName };
        }

        /// <summary>
        /// Deterministic ids: a scenario names a form by its name, and a fake environment handing
        /// out fresh guids per run would make a failing shot impossible to read twice.
        /// </summary>
        private static Guid Id(string seed)
        {
            var bytes = new byte[16];
            var text = Encoding.UTF8.GetBytes(seed ?? "");
            for (var i = 0; i < text.Length; i++) bytes[i % 16] ^= text[i];
            bytes[0] |= 0x40;
            return new Guid(bytes);
        }

        /// <summary>
        /// The forms of one table. Contact is the interesting one - it is where the handlers are;
        /// every other table gets forms of its own carrying a single handler named after it,
        /// which is enough to tell whose answer landed in the list.
        /// </summary>
        public static List<SampleForm> Forms(string entity)
        {
            if (entity == "contact")
            {
                return new List<SampleForm>
                {
                    Form("contact", "Contact", ShowcaseHandlers()),
                    Form("contact", "Contact Quick Create", LayoutHandlers(), type: 7),
                    Form("contact", "E2E Events Test", OneHandler("contact")),
                    Form("contact", "Information", new List<EventHandlerInfo>()),
                    Form("contact", "Portal Contact (Enhanced)", LayoutHandlers()),
                    Form("contact", "Profile Web Form (Enhanced)", LayoutHandlers(), managed: true),
                };
            }

            return new List<SampleForm>
            {
                Form(entity, entity + " Main", OneHandler(entity)),
                Form(entity, entity + " Quick Create", OneHandler(entity), type: 7),
            };
        }

        public static List<SampleForm> AllForms()
        {
            return Entities().SelectMany(e => Forms(e.LogicalName)).ToList();
        }

        private static SampleForm Form(string entity, string name, List<EventHandlerInfo> handlers,
            int type = 2, bool managed = false)
        {
            return new SampleForm
            {
                Id = Id(entity + "/" + name),
                Entity = entity,
                Name = name,
                Type = type,
                IsManaged = managed,
                Handlers = handlers
            };
        }

        /// <summary>One handler named after its table, so a grid row says which fetch filled it.</summary>
        private static List<EventHandlerInfo> OneHandler(string entity)
        {
            return new List<EventHandlerInfo>
            {
                new EventHandlerInfo
                {
                    Kind = EventKind.FormOnLoad,
                    EventName = "onload",
                    TargetName = "",
                    FunctionName = "MyOrg." + entity + ".onLoad",
                    LibraryName = "new_/scripts/" + entity + ".js",
                    Parameters = "",
                    PassExecutionContext = true,
                    Enabled = true
                }
            };
        }

        /// <summary>
        /// One of each kind the grid renders differently: an internal handler it greys out and
        /// refuses to convert, a convertible one it checks, and a disabled one.
        /// </summary>
        public static List<EventHandlerInfo> LayoutHandlers()
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
        /// A form carrying one of every event the tool can convert, plus the one it cannot, so a
        /// single screenshot shows the whole vocabulary of the grid and of the generated code.
        /// </summary>
        public static List<EventHandlerInfo> ShowcaseHandlers()
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
        /// Form XML carrying exactly these handlers, in the shape Dynamics writes it: form level
        /// events for onload and onsave, an attribute on the event for an OnChange, a tab
        /// attribute for tabstatechange, a control attribute for a subgrid OnLoad, and the
        /// registrations Dynamics owns under InternalHandlers rather than Handlers.
        ///
        /// Built rather than fixtured because the point is the round trip: the tool parses this
        /// back and a scenario asserts on what it got, so anything the parser and this disagree
        /// about shows up as a scenario that cannot pass rather than as a quiet difference.
        /// </summary>
        public static string FormXml(IEnumerable<EventHandlerInfo> handlers)
        {
            var list = handlers.ToList();
            var xml = new StringBuilder();
            xml.Append("<form showImage=\"true\">");

            xml.Append("<formLibraries>");
            foreach (var library in list.Select(h => h.LibraryName)
                         .Where(l => !string.IsNullOrEmpty(l)).Distinct())
            {
                xml.Append("<Library name=\"" + Escape(library) + "\" libraryUniqueId=\""
                           + Id(library) + "\" />");
            }
            xml.Append("</formLibraries>");

            xml.Append("<events>");
            foreach (var handler in list)
            {
                xml.Append("<event name=\"" + Escape(handler.EventName) + "\"");
                switch (handler.Kind)
                {
                    case EventKind.AttributeOnChange:
                        xml.Append(" attribute=\"" + Escape(handler.TargetName) + "\"");
                        break;
                    case EventKind.TabStateChange:
                        xml.Append(" tab=\"" + Escape(handler.TargetName) + "\"");
                        break;
                    case EventKind.GridOnLoad:
                        xml.Append(" control=\"" + Escape(handler.TargetName) + "\"");
                        break;
                    default:
                        // The internal sample handler names its target the way a real
                        // setadditionalparams registration does.
                        if (handler.IsInternal && !string.IsNullOrEmpty(handler.TargetName))
                            xml.Append(" attribute=\"" + Escape(handler.TargetName) + "\"");
                        break;
                }
                xml.Append(" application=\"false\" active=\"false\">");

                var container = handler.IsInternal ? "InternalHandlers" : "Handlers";
                xml.Append("<" + container + ">");
                xml.Append("<Handler functionName=\"" + Escape(handler.FunctionName) + "\""
                           + " libraryName=\"" + Escape(handler.LibraryName) + "\""
                           + " handlerUniqueId=\"" + Id(handler.FunctionName + handler.TargetName) + "\""
                           + " enabled=\"" + (handler.Enabled ? "true" : "false") + "\""
                           + " parameters=\"" + Escape(handler.Parameters) + "\""
                           + " passExecutionContext=\"" + (handler.PassExecutionContext ? "true" : "false")
                           + "\" />");
                xml.Append("</" + container + ">");
                xml.Append("</event>");
            }
            xml.Append("</events>");

            xml.Append("</form>");
            return xml.ToString();
        }

        private static string Escape(string text)
        {
            return (text ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
