namespace Coquena;

// coquena chat and the chat view of the TUI.
public static partial class Messages
{
    public static readonly Text ChatUsage = new("Usage: coquena chat <preset> [--service <name|default|self> | --global] [--load]\n  [--reasoning] [-p|--prompt <text|-> [--json]]");
    public static readonly Text ChatEmptyPrompt = new("The prompt is empty.");
    public static readonly Text ChatReasoningShown = new("Reasoning shown.");
    public static readonly Text ChatReasoningHidden = new("Reasoning hidden.");
    public static readonly Text ChatHelp = new("  <preset>: published name project/key, the key of the recipe in this folder\n  or service/project/key. Without --service, the recipe target is used.\n  --global is the same as --service global. --load asks to load it; Enter sends when it finishes.\n  -p/--prompt sends one prompt and ends; - reads it from standard input. The answer\n  goes to standard output and the metrics to standard error; --json writes one object.\n  --reasoning shows the model reasoning before the answer.\nEnter sends; /reasoning shows or hides the reasoning; /exit or Ctrl+C quits.");
    public static readonly Text ChatNeedsTerminal = new("Use coquena chat in an interactive terminal.");
    public static readonly Text ChatNoService = new("There is no service «{0}». {1}");
    public static readonly Text ChatServiceAmbiguous = new("«{0}» names more than one service; none is chosen by match.");
    public static readonly Text ChatServiceUnavailable = new("Service not available: {0}");
    public static readonly Text ChatStartQuestion = new("«{0}» is not running. Start it to try the chat? [y/N] ");
    public static readonly Text ChatServiceNotReady = new("The service did not become available. {0}");
    public static readonly Text ChatNotPublishedIn = new("«{0}» is not published in {1}. {2}");
    public static readonly Text ChatPublishedInMany = new("«{0}» is published in more than one service; choose one:");
    public static readonly Text ChatNoPreset = new("There is no published preset «{0}» in the active services.");
    public static readonly Text ChatLoadRejected = new("the server rejected the load");
    public static readonly Text ChatLoadFailed = new("the load failed; see the service log");
    public static readonly Text ChatNoLoadState = new("no load state: {0}");
    public static readonly Text ChatLoadingModel = new(" loading model {0} s");
    public static readonly Text ChatWith = new("Chat with {0}/{1}. Enter sends; /exit or Ctrl+C quits.");
    public static readonly Text ChatCannotSendYet = new("cannot send until the model is loaded");
    public static readonly Text ChatNoRequest = new("no request");
    public static readonly Text ChatWaiting = new("waiting");
    public static readonly Text ChatProcessingPrompt = new("processing prompt");
    public static readonly Text ChatGenerating = new("generating");
    public static readonly Text ChatInterrupted = new("interrupted");
    public static readonly Text ChatError = new("error");
    public static readonly Text ChatExitHint = new("/exit quits");
    public static readonly Text ChatSendHint = new("Enter sends · /exit quits");
    public static readonly Text ChatNoTelemetry = new("no request telemetry");
    public static readonly Text ChatCache = new("cache");
    public static readonly Text ChatAccepted = new("accept.");
    public static readonly Text ChatFirstToken = new("1st");
    public static readonly Text ChatYou = new("you");
    public static readonly Text ChatAssistant = new("assistant");
    public static readonly Text ChatNoRequestUpper = new("○ NO REQUEST");
    public static readonly Text ChatWaitingUpper = new(" WAITING");
    public static readonly Text ChatGeneratingUpper = new("● GENERATING");
    public static readonly Text ChatDoneUpper = new("✓ DONE");
    public static readonly Text ChatInterruptedUpper = new("✗ INTERRUPTED");
    public static readonly Text ChatErrorUpper = new("✗ ERROR");
    public static readonly Text ChatChooseServicePreset = new("choose service › preset");
    public static readonly Text ChatChoosePreset = new("choose preset");
    public static readonly Text ChatGoToDefinition = new(" Go to definition ");
    public static readonly Text ChatStop = new(" ■ Stop ");
    public static readonly Text ChatSend = new(" Send ");
    public static readonly Text ChatResponseRunning = new("There is a response in progress.");
    public static readonly Text ChatClientClosed = new("Connection closed by the client; the server did not confirm the cancellation.");
    public static readonly Text ChatPublishedPresets = new("Published presets: {0}.");
    public static readonly Text ChatPresetsList = new("Presets: {0}.");
    public static readonly Text ChatFinish =new("done");
    public static readonly Text ChatFinishLength = new("done: token limit");
    public static readonly Text ChatFinishOther = new("done: {0}");
}
