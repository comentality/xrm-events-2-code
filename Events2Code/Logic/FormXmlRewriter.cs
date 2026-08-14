using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Events2Code.Logic
{
    public static class FormXmlRewriter
    {
        /// <summary>
        /// Removes the given handlers from the form XML, ensures a single OnLoad registration
        /// for the bootstrap function, and ensures the bootstrap library is on the form.
        /// </summary>
        public static string Rewrite(string formXml, IList<EventHandlerInfo> toRemove, string bootstrapFunction, string bootstrapLibrary)
        {
            var doc = XDocument.Parse(formXml, LoadOptions.PreserveWhitespace);

            foreach (var handler in AllHandlers(doc).ToList())
            {
                // Belt and braces: internal handlers are never converted, so removing one would
                // silently drop form logic Dynamics owns. Refuse regardless of what was passed in.
                if (FormXmlParser.IsInternalHandler(handler)) continue;

                var uniqueId = (string)handler.Attribute("handlerUniqueId") ?? "";
                var fn = (string)handler.Attribute("functionName") ?? "";
                var lib = (string)handler.Attribute("libraryName") ?? "";

                var matches = toRemove.Any(r =>
                    !string.IsNullOrEmpty(r.HandlerUniqueId) && r.HandlerUniqueId == uniqueId ||
                    string.IsNullOrEmpty(r.HandlerUniqueId) && r.FunctionName == fn && r.LibraryName == lib);

                if (matches) handler.Remove();
            }

            // Drop event elements that no longer have any handlers
            foreach (var ev in doc.Descendants()
                .Where(e => e.Name.LocalName.Equals("event", StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.Descendants().Any(d => d.Name.LocalName.Equals("Handler", StringComparison.OrdinalIgnoreCase)))
                .ToList())
            {
                ev.Remove();
            }

            // Drop events containers that are now empty (form-level one is recreated below if needed)
            foreach (var events in doc.Descendants()
                .Where(e => e.Name.LocalName.Equals("events", StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.Elements().Any(d => d.Name.LocalName.Equals("event", StringComparison.OrdinalIgnoreCase)))
                .ToList())
            {
                events.Remove();
            }

            EnsureBootstrapOnLoad(doc, bootstrapFunction, bootstrapLibrary);
            EnsureLibrary(doc, bootstrapLibrary);

            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private static IEnumerable<XElement> AllHandlers(XDocument doc)
        {
            return doc.Descendants().Where(e => e.Name.LocalName.Equals("Handler", StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureBootstrapOnLoad(XDocument doc, string bootstrapFunction, string bootstrapLibrary)
        {
            var root = doc.Root;
            var events = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("events", StringComparison.OrdinalIgnoreCase));
            if (events == null)
            {
                events = new XElement("events");
                var libraries = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("formLibraries", StringComparison.OrdinalIgnoreCase));
                if (libraries != null) libraries.AddBeforeSelf(events);
                else root.Add(events);
            }

            // Form-level onload: an event without attribute/control/tab target
            var onload = events.Elements()
                .Where(e => e.Name.LocalName.Equals("event", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(e => string.Equals((string)e.Attribute("name"), "onload", StringComparison.OrdinalIgnoreCase)
                    && e.Attribute("attribute") == null && e.Attribute("control") == null && e.Attribute("tab") == null);

            if (onload == null)
            {
                onload = new XElement("event",
                    new XAttribute("name", "onload"),
                    new XAttribute("application", "false"),
                    new XAttribute("active", "false"));
                events.Add(onload);
            }

            var handlers = onload.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Handlers", StringComparison.OrdinalIgnoreCase));
            if (handlers == null)
            {
                handlers = new XElement("Handlers");
                onload.Add(handlers);
            }

            var alreadyThere = handlers.Elements().Any(h =>
                string.Equals((string)h.Attribute("functionName"), bootstrapFunction, StringComparison.Ordinal) &&
                string.Equals((string)h.Attribute("libraryName"), bootstrapLibrary, StringComparison.OrdinalIgnoreCase));

            if (!alreadyThere)
            {
                handlers.Add(new XElement("Handler",
                    new XAttribute("functionName", bootstrapFunction),
                    new XAttribute("libraryName", bootstrapLibrary),
                    new XAttribute("handlerUniqueId", "{" + Guid.NewGuid() + "}"),
                    new XAttribute("enabled", "true"),
                    new XAttribute("parameters", ""),
                    new XAttribute("passExecutionContext", "true")));
            }
        }

        private static void EnsureLibrary(XDocument doc, string libraryName)
        {
            var root = doc.Root;
            var libraries = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("formLibraries", StringComparison.OrdinalIgnoreCase));
            if (libraries == null)
            {
                libraries = new XElement("formLibraries");
                root.Add(libraries);
            }

            var exists = libraries.Elements().Any(l =>
                string.Equals((string)l.Attribute("name"), libraryName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                libraries.Add(new XElement("Library",
                    new XAttribute("name", libraryName),
                    new XAttribute("libraryUniqueId", "{" + Guid.NewGuid() + "}")));
            }
        }
    }
}
