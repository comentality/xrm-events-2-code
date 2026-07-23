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

        public bool IsConvertible => Kind != EventKind.Other;

        public string KindDisplay
        {
            get
            {
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
