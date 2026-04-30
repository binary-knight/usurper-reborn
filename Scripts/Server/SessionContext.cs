using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// Per-player session context for MUD mode. Stored in AsyncLocal so each async task chain
/// sees its own player state without rewriting every method signature.
///
/// In single-player/BBS mode, SessionContext.Current is null and all singletons fall back
/// to their static _fallbackInstance (identical to pre-MUD behavior).
///
/// In MUD mode, each PlayerSession sets SessionContext.Current before entering the game loop,
/// so all SomeSystem.Instance calls resolve to the per-session instance stored here.
/// </summary>
public class SessionContext : IDisposable
{
    private static readonly AsyncLocal<SessionContext?> _current = new();

    /// <summary>
    /// The session context for the current async execution flow.
    /// Null in single-player, BBS door mode, and WorldSim-only processes.
    /// </summary>
    public static SessionContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>Returns true if we're running inside a MUD session context.</summary>
    public static bool IsActive => _current.Value != null;

    // --- Connection I/O ---
    public Stream InputStream { get; init; } = null!;
    public Stream OutputStream { get; init; } = null!;
    public string Username { get; init; } = "";
    public string CharacterKey { get; set; } = "";  // Save key — same as Username unless playing alt character
    public string ConnectionType { get; init; } = "Unknown"; // Web, SSH, BBS
    public string RemoteIP { get; init; } = ""; // Client IP address
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// True if the client negotiated GMCP (Generic MUD Communication Protocol) at connection time.
    /// When enabled, GmcpBridge.Emit fans out structured out-of-band events (Char.Vitals, Room.Info, etc.)
    /// to the connected MUD client. Set during the IAC handshake in MudServer.ProbeTtypeAsync.
    /// </summary>
    public bool GmcpEnabled { get; set; }

    // --- Per-Session Terminal ---
    public TerminalEmulator Terminal { get; set; } = null!;

    // --- Per-Session Player ---
    public Character? Player { get; set; }

    // --- Per-Session Game Engine ---
    public GameEngine Engine { get; set; } = null!;

    // --- Per-Session Location ---
    public LocationManager LocationManager { get; set; } = null!;

    // --- Per-Session Story/Narrative Systems ---
    public StoryProgressionSystem Story { get; set; } = null!;
    public CompanionSystem Companions { get; set; } = null!;
    public OceanPhilosophySystem Ocean { get; set; } = null!;
    public SevenSealsSystem SevenSeals { get; set; } = null!;
    public GriefSystem Grief { get; set; } = null!;
    public BetrayalSystem Betrayal { get; set; } = null!;
    public MoralParadoxSystem MoralParadox { get; set; } = null!;
    public AmnesiaSystem Amnesia { get; set; } = null!;
    public DreamSystem Dreams { get; set; } = null!;
    public StrangerEncounterSystem StrangerEncounters { get; set; } = null!;
    public TownNPCStorySystem TownNPCStories { get; set; } = null!;
    public CycleDialogueSystem CycleDialogue { get; set; } = null!;
    public CycleSystem Cycle { get; set; } = null!;

    // --- Per-Session Mechanics Systems ---
    public AlignmentSystem Alignment { get; set; } = null!;
    public FactionSystem Factions { get; set; } = null!;
    public ArchetypeTracker Archetype { get; set; } = null!;
    public MetaProgressionSystem MetaProgression { get; set; } = null!;
    public DivineBlessingSystem DivineBlessing { get; set; } = null!;
    public PrisonActivitySystem PrisonActivity { get; set; } = null!;
    public RomanceTracker Romance { get; set; } = null!;
    public IntimacySystem Intimacy { get; set; } = null!;
    public RelationshipSystem Relationships { get; set; } = null!;

    // --- Per-Session Online Systems ---
    public OnlineStateManager? OnlineState { get; set; }
    public OnlineChatSystem? OnlineChat { get; set; }

    // --- Per-Session Wizard State ---
    public WizardLevel WizardLevel { get; set; } = WizardLevel.Mortal;
    public bool WizardGodMode { get; set; } = false;
    public bool WizardInvisible { get; set; } = false;

    // --- Per-Session Preferences ---
    /// <summary>
    /// Per-session language preference. Stored on the SessionContext object (not a separate
    /// AsyncLocal) so that changes inside awaited methods propagate back to callers.
    /// AsyncLocal has copy-on-write semantics for value types/strings — modifications in
    /// child async scopes don't flow back to the parent. But property changes on a shared
    /// reference object DO flow back, which is what we need for in-session preference changes.
    /// </summary>
    public string Language { get; set; } = "en";
    public bool CompactMode { get; set; } = false;
    public bool DisableCharacterMonsterArt { get; set; } = false;
    public int DateFormat { get; set; } = 0;
    public ColorThemeType ColorTheme { get; set; } = ColorThemeType.Default;

    // --- Per-Session Notifications ---
    public Queue<string> PendingNotifications { get; } = new();
    public bool IsIntentionalExit { get; set; } = false;

    // v0.60.0 beta-launch Rage event: when set, every save path on this session
    // becomes a no-op so a late autosave can't re-INSERT the player row that the
    // rage cinematic just deleted (the INSERT...ON CONFLICT in WriteGameData
    // doesn't carry password_hash, which would leave a row with an empty hash --
    // re-login then sees "Incorrect password" instead of the intended "Unknown
    // username"). Set BEFORE the cinematic begins.
    public bool IsRageKilled { get; set; } = false;

    /// <summary>
    /// Create fresh instances of all per-session systems for a new player session.
    /// </summary>
    public void InitializeSystems()
    {
        Story = new StoryProgressionSystem();
        Companions = new CompanionSystem();
        Ocean = new OceanPhilosophySystem();
        SevenSeals = new SevenSealsSystem();
        Grief = new GriefSystem();
        Betrayal = new BetrayalSystem();
        MoralParadox = new MoralParadoxSystem();
        Amnesia = new AmnesiaSystem();
        Dreams = new DreamSystem();
        StrangerEncounters = new StrangerEncounterSystem();
        TownNPCStories = new TownNPCStorySystem();
        CycleDialogue = new CycleDialogueSystem();
        Cycle = new CycleSystem();
        Alignment = new AlignmentSystem();
        Factions = new FactionSystem();
        Archetype = new ArchetypeTracker();
        MetaProgression = new MetaProgressionSystem();
        DivineBlessing = new DivineBlessingSystem();
        PrisonActivity = new PrisonActivitySystem();
        Romance = new RomanceTracker();
        Intimacy = new IntimacySystem();
        Relationships = new RelationshipSystem();
    }

    public void Dispose()
    {
        // Clear the AsyncLocal so subsequent code in this async flow
        // falls back to static singletons
        if (_current.Value == this)
            _current.Value = null;
    }
}
