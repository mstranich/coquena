namespace Coquena;

public static partial class Messages
{
    public static readonly Text OutputAdapter = new("Adapter: {0}");
    public static readonly Text OutputErrorSummary = new("Error: {0}. {1}");
    public static readonly Text OutputWarning = new("Warning: {0}. {1}");
    public static readonly Text OutputSupportedFamilies = new("Families: cpu, vulkan, cuda@\u003Cmajor\u003E.");
    public static readonly Text OutputWarningDetail = new("Warning: {0}");
    public static readonly Text OutputEventsLine = new("Events: {0}");
    public static readonly Text OutputEventsIndentedLine = new("  Events: {0}");
    public static readonly Text OutputRemovedPath = new("Removed: {0}");
    public static readonly Text OutputPendingPath = new("Pending: {0}");
}
