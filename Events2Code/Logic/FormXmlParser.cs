using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Events2Code.Logic
{
    public class ParsedForm
    {
        public List<EventHandlerInfo> Handlers { get; } = new List<EventHandlerInfo>();
        public List<string> Libraries { get; } = new List<string>();
    }

    public static class FormXmlParser
    {
        public static ParsedForm Parse(string formXml)
        {
            var result = new ParsedForm();
            var doc = XDocument.Parse(formXml);

            foreach (var lib in doc.Descendants().Where(e => e.Name.LocalName.Equals("Library", StringComparison.OrdinalIgnoreCase)))
            {
                var name = (string)lib.Attribute("name");
                if (!string.IsNullOrEmpty(name) && !result.Libraries.Contains(name))
                    result.Libraries.Add(name);
            }

            foreach (var ev in doc.Descendants().Where(e => e.Name.LocalName.Equals("event", StringComparison.OrdinalIgnoreCase)))
            {
                var eventName = ((string)ev.Attribute("name") ?? "").ToLowerInvariant();
                var attribute = (string)ev.Attribute("attribute");

                // Target may be declared on the event itself or implied by the enclosing element
                var ancestorTab = ev.Ancestors().FirstOrDefault(a => a.Name.LocalName.Equals("tab", StringComparison.OrdinalIgnoreCase));
                var ancestorControl = ev.Ancestors().FirstOrDefault(a => a.Name.LocalName.Equals("control", StringComparison.OrdinalIgnoreCase));
                var tabName = (string)ev.Attribute("tab") ?? (string)ancestorTab?.Attribute("name");
                var controlId = (string)ev.Attribute("control") ?? (string)ancestorControl?.Attribute("id");

                var kind = Classify(eventName, attribute, tabName, controlId);
                var target = kind == EventKind.AttributeOnChange ? attribute
                           : kind == EventKind.TabStateChange ? tabName
                           : kind == EventKind.GridOnLoad ? controlId
                           : attribute ?? controlId ?? tabName ?? "";

                foreach (var handler in ev.Descendants().Where(e => e.Name.LocalName.Equals("Handler", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Handlers.Add(new EventHandlerInfo
                    {
                        Kind = kind,
                        EventName = eventName,
                        TargetName = target ?? "",
                        FunctionName = (string)handler.Attribute("functionName") ?? "",
                        LibraryName = (string)handler.Attribute("libraryName") ?? "",
                        Parameters = (string)handler.Attribute("parameters") ?? "",
                        PassExecutionContext = string.Equals((string)handler.Attribute("passExecutionContext"), "true", StringComparison.OrdinalIgnoreCase),
                        Enabled = !string.Equals((string)handler.Attribute("enabled"), "false", StringComparison.OrdinalIgnoreCase),
                        HandlerUniqueId = (string)handler.Attribute("handlerUniqueId") ?? ""
                    });
                }
            }

            return result;
        }

        private static EventKind Classify(string eventName, string attribute, string tabName, string controlId)
        {
            switch (eventName)
            {
                case "onload":
                    if (!string.IsNullOrEmpty(controlId)) return EventKind.GridOnLoad;
                    return EventKind.FormOnLoad;
                case "onsave":
                    return EventKind.FormOnSave;
                case "onchange":
                    return string.IsNullOrEmpty(attribute) ? EventKind.Other : EventKind.AttributeOnChange;
                case "tabstatechange":
                    return string.IsNullOrEmpty(tabName) ? EventKind.Other : EventKind.TabStateChange;
                default:
                    return EventKind.Other;
            }
        }
    }
}
