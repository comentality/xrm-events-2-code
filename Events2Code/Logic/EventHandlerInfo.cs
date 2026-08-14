namespace Events2Code.Logic
{
    public enum EventKind
    {
        FormOnLoad,
        FormOnSave,
        AttributeOnChange,
        TabStateChange,
        GridOnLoad,
        Other
    }

    public class EventHandlerInfo
    {
        public EventKind Kind { get; set; }
        public string EventName { get; set; }
        public string TargetName { get; set; }
        public string FunctionName { get; set; }
        public string LibraryName { get; set; }
        public string Parameters { get; set; }
        public bool PassExecutionContext { get; set; }
        public bool Enabled { get; set; }
        public string HandlerUniqueId { get; set; }

        /// <summary>
        /// Registered by Dynamics itself rather than through the form designer: these live in
        /// &lt;InternalHandlers&gt; instead of &lt;Handlers&gt;. Never ours to convert or remove.
        /// </summary>
        public bool IsInternal { get; set; }

        public bool IsConvertible => Kind != EventKind.Other && !IsInternal;

        public string SkipReason => IsInternal
            ? "internal handler, owned by Dynamics"
            : "no programmatic registration API";

        public string KindDisplay
        {
            get
            {
                if (IsInternal) return EventName + " (internal)";

                switch (Kind)
                {
                    case EventKind.FormOnLoad: return "Form OnLoad";
                    case EventKind.FormOnSave: return "Form OnSave";
                    case EventKind.AttributeOnChange: return "OnChange";
                    case EventKind.TabStateChange: return "TabStateChange";
                    case EventKind.GridOnLoad: return "Grid OnLoad";
                    default: return EventName;
                }
            }
        }
    }
}
