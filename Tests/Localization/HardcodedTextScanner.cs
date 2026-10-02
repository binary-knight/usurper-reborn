using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UsurperReborn.Tests.Localization;

/// <summary>
/// Finds player-visible English written directly in Scripts/ instead of going through Loc.Get.
///
/// It parses each file with Roslyn (syntax only, no binding) and looks at the text arguments of the
/// output methods listed below (the sinks). Inside a text argument it follows string literals,
/// interpolated strings (and their holes), concatenation, ?:, ??, switch expressions, casts,
/// array initialisers, string.Format / string.Concat / string.Join and the receiver of
/// PadLeft/PadRight/ToUpper/ToLower/Trim. It does not look inside any other call, which is what
/// leaves Loc.Get keys and their arguments out.
///
/// A site is one string literal or interpolated string node. It counts when, after removing
/// interpolation holes, ANSI escapes, colour markup ([bright_red] .. [/]), backtick colour codes and
/// {0} format tokens, it still holds a word of two or more letters, and is not shaped like a Loc key
/// (lower case dotted id such as "combat.menu.attack").
///
/// Known limits: a literal stored in a local, field or const and passed to a sink later is not seen;
/// default parameter values on sink declarations are not seen; #if STEAM_BUILD blocks are inactive
/// in the parse (no symbols defined), matching the default build.
/// </summary>
public static class HardcodedTextScanner
{
    public sealed record Site(string File, int Line, string Sink, string Literal);

    public sealed record Exclusion(string Kind, string Target, string Reason);

    /// <summary>Which argument positions of a sink carry player text.</summary>
    private sealed record SinkSpec(string Name, int[]? Positions, bool FromPositionOn = false, bool LastOnly = false);

    // ---------------------------------------------------------------------------------------------
    // Sinks on a terminal. The receiver must be a terminal (last identifier ends in "term" or
    // "terminal", any case, or is "own"), or the call is unqualified inside a terminal class.
    // Positions are the text parameters of TerminalEmulator (Scripts/UI/TerminalEmulator.cs).
    // ---------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, SinkSpec> TerminalSinks = new[]
    {
        new SinkSpec("Write", new[] { 0 }),
        new SinkSpec("WriteLine", new[] { 0 }),
        new SinkSpec("WriteRawAnsi", new[] { 0 }),
        new SinkSpec("WriteAsync", new[] { 0 }),
        new SinkSpec("WriteLineAsync", new[] { 0 }),
        new SinkSpec("WriteColorAsync", new[] { 0 }),
        new SinkSpec("WriteColorLineAsync", new[] { 0 }),
        new SinkSpec("DisplayMessage", new[] { 0 }),
        new SinkSpec("SetStatusLine", new[] { 0 }),
        new SinkSpec("GetInput", new[] { 0 }),
        new SinkSpec("GetInputAsync", new[] { 0 }),
        new SinkSpec("GetInputSync", new[] { 0 }),
        new SinkSpec("GetStringInput", new[] { 0 }),
        new SinkSpec("GetStringAsync", new[] { 0 }),
        new SinkSpec("GetMaskedInput", new[] { 0 }),
        new SinkSpec("GetNumberInput", new[] { 0 }),
        new SinkSpec("GetValidNumber", new[] { 0 }),
        new SinkSpec("GetValidChoice", new[] { 0, 3 }),
        new SinkSpec("AskYesNoAsync", new[] { 0 }),
        new SinkSpec("ConfirmAsync", new[] { 0 }),
        new SinkSpec("PressAnyKey", new[] { 0 }),
        new SinkSpec("WaitForKey", new[] { 0 }),
        new SinkSpec("WaitForKeyPress", new[] { 0 }),
    }.ToDictionary(s => s.Name);

    /// <summary>Classes whose own unqualified Write/WriteLine calls go to the player's screen.</summary>
    private static readonly HashSet<string> TerminalClasses = new() { "TerminalEmulator", "BBSTerminalAdapter", "SocketTerminal", "TerminalUI", "LegacyUI" };

    // ---------------------------------------------------------------------------------------------
    // UIHelper (Scripts/UI/UIHelper.cs): receiver "UIHelper", or unqualified inside UIHelper.
    // The first argument is the terminal; text positions follow the declarations.
    // ---------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, SinkSpec> UIHelperSinks = new[]
    {
        new SinkSpec("DrawBoxTop", new[] { 1 }),
        new SinkSpec("DrawBoxLine", new[] { 1 }),
        new SinkSpec("DrawBoxLabelValue", new[] { 1, 2 }),
        new SinkSpec("DrawMenuOption", new[] { 1, 2 }),
        new SinkSpec("DrawSimpleBox", new[] { 1, 2 }),
        new SinkSpec("DrawBoxCentered", new[] { 1 }),
        new SinkSpec("DrawStatBar", new[] { 1 }),
        new SinkSpec("WriteWrapped", new[] { 1 }),
        new SinkSpec("WriteBoxHeader", new[] { 1 }),
        new SinkSpec("WriteSectionHeader", new[] { 1 }),
    }.ToDictionary(s => s.Name);

    // ---------------------------------------------------------------------------------------------
    // News, mail, messages and broadcasts to other players. The names are distinctive, so any
    // receiver counts.
    //   NewsSystem.Newsy / WriteNews / GenericNews / Write*News  (Scripts/Systems/NewsSystem.cs)
    //   MailSystem.SendSystemMail / CompatLayer.SendMail / LegacyCompat.SendMail
    //   OnlineStateManager.SendMessage, SqlSaveBackend.SendMessage / SendMessageToKey, Player.SendMessage (last argument)
    //   GroupSystem.NotifyGroup / BroadcastToGroupSessions / BroadcastToAllGroupSessions
    //   MudServer.BroadcastToAll / SendToPlayer, RoomRegistry.BroadcastToRoom / BroadcastAction / BroadcastGlobal
    //   PlayerSession.EnqueueMessage, OnlineStateManager.AddNews / BroadcastMessage, SqlSaveBackend.AddNews
    //   DungeonLocation.BroadcastDungeonEvent, CombatEngine.BroadcastGroupCombatEvent, TeamSystem.NotifyTeamMembers,
    //   CastleLocation.NotifyDethronedPlayer (reason), QuestSystem.PostBountyOnPlayer (crime),
    //   WorldSimulator.NotifyPlayerSpouseOfDeath (location)
    // ---------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, SinkSpec> MessageSinks = new[]
    {
        new SinkSpec("Newsy", null),
        new SinkSpec("WriteNews", new[] { 1 }),
        new SinkSpec("GenericNews", new[] { 2 }),
        new SinkSpec("WriteDeathNews", new[] { 2 }),
        new SinkSpec("WriteMarriageNews", new[] { 2 }),
        new SinkSpec("WriteRoyalNews", new[] { 1 }),
        new SinkSpec("WriteHolyNews", new[] { 1 }),
        new SinkSpec("WriteQuestNews", new[] { 1 }),
        new SinkSpec("WriteTeamNews", new[] { 1 }),
        new SinkSpec("WritePrisonNews", new[] { 1 }),
        new SinkSpec("SendSystemMail", new[] { 1 }, FromPositionOn: true),
        new SinkSpec("SendMail", new[] { 1 }, FromPositionOn: true),
        new SinkSpec("SendMessage", null, LastOnly: true),
        new SinkSpec("SendMessageToKey", null, LastOnly: true),
        new SinkSpec("NotifyGroup", new[] { 1 }),
        new SinkSpec("BroadcastToGroupSessions", new[] { 2, 3 }),
        new SinkSpec("BroadcastToAllGroupSessions", new[] { 1 }),
        new SinkSpec("BroadcastToAll", new[] { 0 }),
        new SinkSpec("SendToPlayer", new[] { 1 }),
        new SinkSpec("BroadcastToRoom", new[] { 1 }),
        new SinkSpec("BroadcastAction", new[] { 0 }),
        new SinkSpec("BroadcastGlobal", new[] { 0 }),
        new SinkSpec("EnqueueMessage", new[] { 0 }),
        new SinkSpec("AddNews", new[] { 0 }),
        new SinkSpec("BroadcastMessage", new[] { 1 }),
        new SinkSpec("BroadcastDungeonEvent", new[] { 0 }),
        new SinkSpec("BroadcastGroupCombatEvent", new[] { 1 }),
        new SinkSpec("NotifyTeamMembers", new[] { 1 }),
        new SinkSpec("NotifyDethronedPlayer", new[] { 2 }),
        new SinkSpec("PostBountyOnPlayer", new[] { 1 }),
        new SinkSpec("NotifyPlayerSpouseOfDeath", new[] { 2 }),
    }.ToDictionary(s => s.Name);

    // ---------------------------------------------------------------------------------------------
    // Menu and header helpers declared in location classes. Called unqualified (or this./base.).
    // Positions follow the declarations; key arguments fall out through the two letter rule.
    //   BaseLocation: WriteBoxHeader(title), WriteSectionHeader(title), WriteSRMenuOption(key, label),
    //                 ShowBBSHeader(title), ShowBBSMenuRow(params (key, color, label)[]), AddMenuOption(key, text)
    //   PantheonLocation.WriteMenuOption(key, label, desc), AnchorRoadLocation.WriteMenuOption(key, label),
    //   TeamCornerLocation.WriteMenuOption(key1, label1, key2, label2),
    //   HomeLocation.WriteMenuOption/WriteMenuCol/WriteMenuNL(prefix, key, label, ...),
    //   AnchorRoadLocation.WriteMenuRow(k, l, k, l, k, l), MagicShopLocation.WriteMenuRow(k, c, l, k, c, l),
    //   MagicShopLocation.WriteMenuKey(key, color, label), SanctumLocation.WriteSanctumOption(key, label)
    // Other screen helpers called unqualified:
    //   MagicShopLocation.DisplayMessage(message), MaintenanceSystem.WriteIfNotSilent(text),
    //   BaseLocation help list WriteCmd(cmd, desc) / WriteCmdAlias(cmd, alias, desc) / WriteOnlineCmd(cmd, desc),
    //   BaseLocation.DisplayEquipmentSlotWithStats(target, slot, label), GameEngine.ShowInfoScreen(title, content),
    //   GameEngine.ShowLoadFailureWithRecovery(fileName, errorMessage),
    //   pre-login connection text: MudServer.WriteLineAsync(stream, message) / WriteAnsiAsync(stream, text),
    //   RelayClient.WriteAnsi(stream, text)
    // ---------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, SinkSpec> MenuHelperSinks = new[]
    {
        new SinkSpec("WriteBoxHeader", new[] { 0 }),
        new SinkSpec("WriteSectionHeader", new[] { 0 }),
        new SinkSpec("WriteSRMenuOption", new[] { 1 }),
        new SinkSpec("ShowBBSHeader", new[] { 0 }),
        new SinkSpec("ShowBBSMenuRow", null),
        new SinkSpec("AddMenuOption", new[] { 1 }),
        new SinkSpec("WriteMenuOption", null),
        new SinkSpec("WriteMenuCol", new[] { 2 }),
        new SinkSpec("WriteMenuNL", new[] { 2 }),
        new SinkSpec("WriteMenuRow", null),
        new SinkSpec("WriteMenuKey", new[] { 2 }),
        new SinkSpec("WriteSanctumOption", new[] { 1 }),
        new SinkSpec("DisplayMessage", new[] { 0 }),
        new SinkSpec("WriteIfNotSilent", new[] { 0 }),
        new SinkSpec("WriteCmd", new[] { 1 }),
        new SinkSpec("WriteCmdAlias", new[] { 2 }),
        new SinkSpec("WriteOnlineCmd", new[] { 1 }),
        new SinkSpec("DisplayEquipmentSlotWithStats", new[] { 2 }),
        new SinkSpec("ShowInfoScreen", new[] { 0, 1 }),
        new SinkSpec("ShowLoadFailureWithRecovery", new[] { 1 }),
        new SinkSpec("WriteLineAsync", new[] { 1 }),
        new SinkSpec("WriteAnsiAsync", new[] { 1 }),
        new SinkSpec("WriteAnsi", new[] { 1 }),
    }.ToDictionary(s => s.Name);

    /// <summary>Methods whose calls are never followed (debug and log output).</summary>
    private static readonly HashSet<string> LogReceivers = new(StringComparer.Ordinal)
    {
        "DebugLogger", "GD", "Console", "Error", "Out", "logger", "_logger", "Logger", "Log", "Debug", "Trace",
    };

    private static readonly HashSet<string> PassThroughMethods = new(StringComparer.Ordinal)
    {
        "PadLeft", "PadRight", "ToUpper", "ToLower", "ToUpperInvariant", "ToLowerInvariant", "Trim", "TrimStart", "TrimEnd",
    };

    public static IEnumerable<string> SinkNames =>
        TerminalSinks.Keys.Select(k => "terminal." + k)
            .Concat(UIHelperSinks.Keys.Select(k => "UIHelper." + k))
            .Concat(MessageSinks.Keys)
            .Concat(MenuHelperSinks.Keys.Select(k => "(helper) " + k))
            .Append("return (bool, string)");

    private static readonly Regex Ansi = new(@"(\x1b|\\x1b|\\u001b|\\e)\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    /// <summary>Colour names and theme roles the terminal understands (TerminalEmulator.AnsiColorCodes, ColorTheme).</summary>
    public static readonly HashSet<string> ColourNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "black", "blue", "brown", "cyan", "gray", "grey", "green", "magenta", "red", "white", "yellow",
        "bright_blue", "bright_cyan", "bright_green", "bright_magenta", "bright_red", "bright_white", "bright_yellow",
        "dark_blue", "dark_cyan", "dark_gray", "dark_green", "dark_magenta", "dark_red", "dark_yellow", "dim_green",
        "darkblue", "darkcyan", "darkgray", "darkgreen", "darkmagenta", "darkred", "darkyellow",
        "role_action", "role_critical", "role_derived", "role_disabled", "role_narration", "role_notice", "role_success",
    };

    private static readonly Regex Markup = new(@"\[(/|[A-Za-z_]+)\]", RegexOptions.Compiled);
    private static readonly Regex Backtick = new(@"`.", RegexOptions.Compiled);
    private static readonly Regex FormatToken = new(@"\{\d+(,-?\d+)?(:[^}]*)?\}", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"\p{L}{2,}", RegexOptions.Compiled);
    private static readonly Regex LocKeyShape = new(@"^[a-z0-9_]+(\.[a-z0-9_]+)+$", RegexOptions.Compiled);

    /// <summary>True when the text (holes already removed) holds a word a player would read.</summary>
    public static bool IsWordy(string text)
    {
        string t = Ansi.Replace(text, " ");
        t = Markup.Replace(t, m => m.Value == "[/]" || ColourNames.Contains(m.Value.Trim('[', ']')) ? " " : m.Value);
        t = Backtick.Replace(t, " ");
        t = FormatToken.Replace(t, " ");
        if (LocKeyShape.IsMatch(t.Trim())) return false;
        return Word.IsMatch(t);
    }

    // ---------------------------------------------------------------------------------------------
    // Exclusions
    // ---------------------------------------------------------------------------------------------

    /// <summary>Reads "kind|target|reason" lines; kind is file, folder or class. # starts a comment.</summary>
    public static List<Exclusion> ParseExclusions(string text)
    {
        var list = new List<Exclusion>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|');
            if (parts.Length != 3 || parts[2].Trim().Length == 0)
                throw new FormatException($"exclusion line needs kind|target|reason: {line}");
            string kind = parts[0].Trim();
            if (kind != "file" && kind != "folder" && kind != "class")
                throw new FormatException($"exclusion kind must be file, folder or class: {line}");
            list.Add(new Exclusion(kind, parts[1].Trim(), parts[2].Trim()));
        }
        return list;
    }

    private static bool FileExcluded(string rel, IReadOnlyList<Exclusion> ex) =>
        ex.Any(e => (e.Kind == "file" && e.Target == rel) || (e.Kind == "folder" && rel.StartsWith(e.Target, StringComparison.Ordinal)));

    // ---------------------------------------------------------------------------------------------
    // Scan
    // ---------------------------------------------------------------------------------------------

    public static List<Site> ScanRepo(string repoRoot, IReadOnlyList<Exclusion> exclusions, bool dashes = false)
    {
        var sites = new List<Site>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(repoRoot, "Scripts"), "*.cs", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (FileExcluded(rel, exclusions)) continue;
            sites.AddRange(ScanSource(rel, File.ReadAllText(path), exclusions, dashes));
        }
        return sites;
    }

    /// <summary>With dashes true, a site is any screen string holding U+2014, U+2013 or U+2026 (words not required).</summary>
    public static List<Site> ScanSource(string rel, string source, IReadOnlyList<Exclusion> exclusions, bool dashes = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();
        var excludedClasses = new HashSet<string>(exclusions.Where(e => e.Kind == "class").Select(e => e.Target), StringComparer.Ordinal);
        var sites = new List<Site>();

        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax inv)
            {
                var hit = ClassifyInvocation(inv);
                if (hit == null) continue;
                var (sinkName, spec) = hit.Value;
                if (InExcludedClass(inv, excludedClasses)) continue;
                var args = inv.ArgumentList.Arguments;
                for (int i = 0; i < args.Count; i++)
                {
                    if (!IsTextArgument(spec, i, args.Count, args[i])) continue;
                    foreach (var lit in Collect(args[i].Expression))
                        AddIfWordy(sites, rel, sinkName, lit, dashes);
                }
            }
            else if (node is ReturnStatementSyntax ret && ret.Expression is TupleExpressionSyntax tuple
                     && tuple.Arguments.Count >= 2
                     && tuple.Arguments[0].Expression is LiteralExpressionSyntax first
                     && (first.IsKind(SyntaxKind.TrueLiteralExpression) || first.IsKind(SyntaxKind.FalseLiteralExpression)))
            {
                if (InExcludedClass(ret, excludedClasses)) continue;
                for (int i = 1; i < tuple.Arguments.Count; i++)
                    foreach (var lit in Collect(tuple.Arguments[i].Expression))
                        AddIfWordy(sites, rel, "return (bool, string)", lit, dashes);
            }
        }
        return sites;
    }

    private static bool IsTextArgument(SinkSpec spec, int index, int count, ArgumentSyntax arg)
    {
        if (arg.NameColon != null)
        {
            string n = arg.NameColon.Name.Identifier.ValueText;
            return n is "text" or "message" or "prompt" or "title" or "label" or "description" or "desc" or "validHint";
        }
        if (spec.LastOnly) return index == count - 1;
        if (spec.Positions == null) return true;
        if (spec.FromPositionOn) return index >= spec.Positions[0];
        return spec.Positions.Contains(index);
    }

    /// <summary>True when the call is one of the output sinks above (used by DataTextScanner for throws).</summary>
    public static bool IsOutputCall(InvocationExpressionSyntax inv) => ClassifyInvocation(inv) != null;

    private static (string Sink, SinkSpec Spec)? ClassifyInvocation(InvocationExpressionSyntax inv)
    {
        string? name;
        ExpressionSyntax? receiver = null;
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
                name = ma.Name.Identifier.ValueText;
                receiver = ma.Expression;
                break;
            case MemberBindingExpressionSyntax mb: // a?.Method(...)
                name = mb.Name.Identifier.ValueText;
                receiver = (inv.Parent as ConditionalAccessExpressionSyntax)?.Expression
                           ?? FindConditionalReceiver(inv);
                break;
            case IdentifierNameSyntax id:
                name = id.Identifier.ValueText;
                break;
            default:
                return null;
        }

        string? recvLast = receiver == null ? null : LastIdentifier(receiver);
        bool unqualified = receiver == null || receiver is ThisExpressionSyntax || receiver is BaseExpressionSyntax;
        if (recvLast != null && LogReceivers.Contains(recvLast)) return null;

        if (TerminalSinks.TryGetValue(name, out var t))
        {
            if (unqualified && TerminalClasses.Contains(EnclosingClass(inv) ?? "")) return ("terminal." + name, t);
            if (recvLast != null && IsTerminalName(recvLast)) return ("terminal." + name, t);
        }
        if (UIHelperSinks.TryGetValue(name, out var u))
        {
            if (recvLast == "UIHelper" || (unqualified && EnclosingClass(inv) == "UIHelper")) return ("UIHelper." + name, u);
        }
        if (MessageSinks.TryGetValue(name, out var m))
            return (name, m);
        if (MenuHelperSinks.TryGetValue(name, out var h) && unqualified)
            return ("(helper) " + name, h);
        return null;
    }

    private static ExpressionSyntax? FindConditionalReceiver(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is ConditionalAccessExpressionSyntax ca) return ca.Expression;
        return null;
    }

    private static bool IsTerminalName(string id) =>
        id == "own"
        || id.EndsWith("term", StringComparison.OrdinalIgnoreCase)
        || id.EndsWith("terminal", StringComparison.OrdinalIgnoreCase);

    private static string? LastIdentifier(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
        ConditionalAccessExpressionSyntax ca => LastIdentifier(ca.WhenNotNull),
        PostfixUnaryExpressionSyntax pu => LastIdentifier(pu.Operand), // terminal!
        ParenthesizedExpressionSyntax pe => LastIdentifier(pe.Expression),
        PredefinedTypeSyntax pt => pt.Keyword.ValueText,
        _ => null,
    };

    private static string? EnclosingClass(SyntaxNode node) =>
        node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static bool InExcludedClass(SyntaxNode node, HashSet<string> excluded) =>
        excluded.Count > 0 && node.Ancestors().OfType<TypeDeclarationSyntax>().Any(t => excluded.Contains(t.Identifier.ValueText));

    /// <summary>String literal and interpolated string nodes that reach the output through this expression.</summary>
    private static IEnumerable<ExpressionSyntax> Collect(ExpressionSyntax? e)
    {
        switch (e)
        {
            case null:
                yield break;
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                yield return lit;
                break;
            case InterpolatedStringExpressionSyntax interp:
                yield return interp;
                foreach (var hole in interp.Contents.OfType<InterpolationSyntax>())
                    foreach (var x in Collect(hole.Expression)) yield return x;
                break;
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.AddExpression) || bin.IsKind(SyntaxKind.CoalesceExpression):
                foreach (var x in Collect(bin.Left)) yield return x;
                foreach (var x in Collect(bin.Right)) yield return x;
                break;
            case ConditionalExpressionSyntax cond:
                foreach (var x in Collect(cond.WhenTrue)) yield return x;
                foreach (var x in Collect(cond.WhenFalse)) yield return x;
                break;
            case ParenthesizedExpressionSyntax par:
                foreach (var x in Collect(par.Expression)) yield return x;
                break;
            case CastExpressionSyntax cast:
                foreach (var x in Collect(cast.Expression)) yield return x;
                break;
            case SwitchExpressionSyntax sw:
                foreach (var arm in sw.Arms)
                    foreach (var x in Collect(arm.Expression)) yield return x;
                break;
            case TupleExpressionSyntax tup: // ShowBBSMenuRow(("K", "color", "Label"), ...): label is the last element
                if (tup.Arguments.Count > 0)
                    foreach (var x in Collect(tup.Arguments[^1].Expression)) yield return x;
                break;
            case ImplicitArrayCreationExpressionSyntax ia:
                foreach (var el in ia.Initializer.Expressions)
                    foreach (var x in Collect(el)) yield return x;
                break;
            case ArrayCreationExpressionSyntax ac when ac.Initializer != null:
                foreach (var el in ac.Initializer.Expressions)
                    foreach (var x in Collect(el)) yield return x;
                break;
            case CollectionExpressionSyntax ce:
                foreach (var el in ce.Elements.OfType<ExpressionElementSyntax>())
                    foreach (var x in Collect(el.Expression)) yield return x;
                break;
            case InvocationExpressionSyntax call:
                if (call.Expression is MemberAccessExpressionSyntax ma)
                {
                    string n = ma.Name.Identifier.ValueText;
                    if (PassThroughMethods.Contains(n))
                    {
                        foreach (var x in Collect(ma.Expression)) yield return x;
                    }
                    else if ((n == "Format" || n == "Concat" || n == "Join")
                             && LastIdentifier(ma.Expression) is "string" or "String")
                    {
                        foreach (var a in call.ArgumentList.Arguments)
                            foreach (var x in Collect(a.Expression)) yield return x;
                    }
                }
                break;
        }
    }

    /// <summary>Em-dash, en-dash and ellipsis: kept out of on-screen strings (terminals and BBS clients show them as junk).</summary>
    public static readonly char[] DashChars = { '\u2014', '\u2013', '\u2026' };

    private static void AddIfWordy(List<Site> sites, string rel, string sink, ExpressionSyntax node, bool dashes = false)
    {
        string visible = node switch
        {
            LiteralExpressionSyntax lit when ColourNames.Contains(lit.Token.ValueText) => "",
            LiteralExpressionSyntax lit => lit.Token.ValueText,
            InterpolatedStringExpressionSyntax interp => string.Concat(
                interp.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText)),
            _ => "",
        };
        if (dashes ? visible.IndexOfAny(DashChars) < 0 : !IsWordy(visible)) return;
        int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        string shown = Regex.Replace(node.ToString(), @"\s*\r?\n\s*", " ");
        sites.Add(new Site(rel, line, sink, shown));
    }

    // ---------------------------------------------------------------------------------------------
    // Systems rollup: first matching rule wins.
    // ---------------------------------------------------------------------------------------------
    private static readonly (Regex Pattern, string System)[] SystemRules =
    {
        (new Regex(@"(Combat|Spell|Ability|Monster|WorldBoss|OldGodBoss|StatusEffect|BossPowerSurge|Miracle|Rage|NPCCombat|Headless|Tactical)", RegexOptions.Compiled), "combat"),
        (new Regex(@"(Dungeon|Puzzle|Riddle|Discovery|FeatureInteraction|Settlement|Wilderness|RareEncounter|Beast|SevenSeals|Gauntlet)", RegexOptions.Compiled), "dungeon"),
        (new Regex(@"(VisualNovel|Dialogue|Romance|Intimacy|Relationship|Companion|Marriage|Family|TownNPCStory|StrangerEncounter)", RegexOptions.Compiled), "VN dialogue"),
        (new Regex(@"(Ending|Story|Opening|Awakening|UsurperHistory|OldGod|Dream|Amnesia|OceanPhilosophy|MoralParadox|CycleDialogue|Echo|Journal)", RegexOptions.Compiled), "endings/story"),
        (new Regex(@"(Castle|Prison|Home|King|Challenge|Throne|Court|CityControl|Petition)", RegexOptions.Compiled), "castle/prison/home"),
        (new Regex(@"(Online|Mud|Group|Room|Chat|Mail|News|Wiz|Relay|Session|Gmcp|Team|Guild|Discord|Server/|BBS/|Duel|Bot|SqlSaveBackend)", RegexOptions.Compiled), "online/social"),
        (new Regex(@"(Inventory|Item|Equipment|Loot|GearSet|Character|Player\.cs|Status|Training|LevelMaster|Stat|Achievement|Faction|Alignment|Mental|Grief|Quest|Artifact|Potion)", RegexOptions.Compiled), "inventory/character"),
        (new Regex(@"(Scripts/Locations/|StreetEncounter)", RegexOptions.Compiled), "town locations"),
    };

    public static readonly string[] SystemOrder =
    {
        "combat", "dungeon", "town locations", "inventory/character", "castle/prison/home", "endings/story", "VN dialogue", "online/social", "other",
    };

    public static string SystemFor(string rel)
    {
        foreach (var (pattern, system) in SystemRules)
            if (pattern.IsMatch(rel)) return system;
        return "other";
    }

    // ---------------------------------------------------------------------------------------------
    // Reports
    // ---------------------------------------------------------------------------------------------

    public static SortedDictionary<string, int> CountsByFile(IEnumerable<Site> sites)
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in sites) d[s.File] = d.TryGetValue(s.File, out var n) ? n + 1 : 1;
        return d;
    }

    public static string InventoryMarkdown(IReadOnlyList<Site> sites, int filesScanned)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hardcoded player-visible text (Scripts/)");
        sb.AppendLine();
        sb.AppendLine($"Files scanned: {filesScanned}. Files with sites: {sites.Select(s => s.File).Distinct().Count()}. Sites: {sites.Count}.");
        sb.AppendLine();
        sb.AppendLine("Sinks: " + string.Join(", ", SinkNames));
        sb.AppendLine();
        foreach (var group in sites.GroupBy(s => s.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"## {group.Key} ({group.Count()}, {SystemFor(group.Key)})");
            sb.AppendLine();
            foreach (var s in group)
                sb.AppendLine($"- {s.File}:{s.Line} `{s.Sink}` {s.Literal}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string RollupMarkdown(IReadOnlyList<Site> sites)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hardcoded player-visible text by system");
        sb.AppendLine();
        sb.AppendLine($"Total sites: {sites.Count}");
        sb.AppendLine();
        sb.AppendLine("| System | Sites | Files |");
        sb.AppendLine("|---|---|---|");
        var bySystem = sites.GroupBy(s => SystemFor(s.File)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var system in SystemOrder)
        {
            bySystem.TryGetValue(system, out var list);
            list ??= new List<Site>();
            sb.AppendLine($"| {system} | {list.Count} | {list.Select(s => s.File).Distinct().Count()} |");
        }
        sb.AppendLine();
        foreach (var system in SystemOrder)
        {
            if (!bySystem.TryGetValue(system, out var list)) continue;
            sb.AppendLine($"## {system}");
            sb.AppendLine();
            foreach (var f in list.GroupBy(s => s.File).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                sb.AppendLine($"- {f.Key}: {f.Count()}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
