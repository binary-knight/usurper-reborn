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
/// Second half of the hardcoded text scan: player-visible English that is not an argument of an
/// output call (those are HardcodedTextScanner's). Three categories:
///
///   data      string literals in data tables (object and collection initialisers, arrays, dictionary
///             values, constructor arguments, field initialisers) whose (file, slot) is listed as a
///             "table" or "nouns" entry in Tests/Localization/hardcoded-data-sources.txt. A slot names
///             where the literal sits, for example init.Text (object initialiser member Text),
///             decl:Taunts (field or local Taunts), new:SpellInfo#1 (constructor argument 1),
///             init.[k]= (dictionary value), dictpair#1 (value of a { "a", "b" } pair), and
///             decl:X/tuple#0 (first element of a tuple inside X). The sources file says per slot
///             whether its strings reach players; only listed slots count.
///   electron  literals sent to the Electron client and shown there: arguments of ElectronBridge.Emit*
///             (walking anonymous objects, object and collection initialisers) and object initialisers
///             of the payload classes declared in Scripts/UI/ElectronBridge.cs, anywhere. Event names,
///             keys, styles, icons, sound ids and similar members are left out (IdMembers).
///   throw     a literal in a thrown exception's message, when the throw sits in a try block whose
///             catch (Exception, or the thrown type) writes that exception's .Message through an output
///             call. Other throws are internal and do not count.
///
/// Not counted, with the reason:
///   CombatLog entries (CombatResult.CombatLog): the only reader that prints them is
///   MainStreetLocation.TestCombat, which has no caller.
///   Slots listed as "keyed": the literal is the English source or fallback of a Loc key the display
///   resolves first (discovery.*, riddle.*, dream.* and others), so it is already translatable.
///   Slots listed as "names": proper nouns (NPC, god, boss, place and team names) are names, not
///   translation targets. Rule: a "table" entry may not name a slot ending in Name, Names or Surnames;
///   such a slot is either "names" (proper nouns, not counted) or "nouns" (common nouns such as ability
///   or item names, counted).
///   Identifier shaped literals: no whitespace and all lower case, all upper case, or containing "_"
///   (ids, keys, enum-like tags). Colour names, Loc key shaped strings, and text without a word
///   (HardcodedTextScanner.IsWordy) do not count either.
///
/// Known limits: a table in a file or slot not listed in the sources file is not seen (review new
/// tables when adding them); literals built in code (switch arms, returns, assignments outside
/// initialisers) are only seen when their slot is listed; literals nested inside an interpolated string
/// are left to the outer string.
/// </summary>
public static class DataTextScanner
{
    public const string Data = "data";
    public const string Electron = "electron";
    public const string Throw = "throw";
    public static readonly string[] Categories = { Data, Electron, Throw };

    public sealed record Site(string File, int Line, string Category, string Slot, string Literal);

    /// <summary>One line of the sources file: kind|file|slot|reason.</summary>
    public sealed record Source(string Kind, string File, string Slot, string Reason);

    /// <summary>A literal in a listed slot that does not count (keyed or names), kept for the report.</summary>
    public sealed record Skipped(string File, string Kind, string Slot, int Count);

    public static readonly string[] SourceKinds = { "table", "nouns", "names", "keyed" };
    private static readonly Regex NameSlot = new(@"(Name|Names|Surnames)(/tuple#\d+)*$", RegexOptions.Compiled);

    public static List<Source> ParseSources(string text)
    {
        var list = new List<Source>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|');
            if (parts.Length != 4 || parts[3].Trim().Length == 0)
                throw new FormatException($"data source line needs kind|file|slot|reason: {line}");
            string kind = parts[0].Trim();
            if (!SourceKinds.Contains(kind))
                throw new FormatException($"data source kind must be one of {string.Join(", ", SourceKinds)}: {line}");
            string slot = parts[2].Trim();
            if (kind == "table" && NameSlot.IsMatch(slot))
                throw new FormatException($"slot {slot} holds names: list it as names (proper nouns) or nouns (common nouns): {line}");
            list.Add(new Source(kind, parts[1].Trim(), slot, parts[3].Trim()));
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // Electron
    // ---------------------------------------------------------------------------------------------

    /// <summary>Payload classes declared in Scripts/UI/ElectronBridge.cs (a test keeps this in step).</summary>
    public static readonly HashSet<string> ElectronPayloadTypes = new(StringComparer.Ordinal)
    {
        "ChoiceOption", "TargetOption", "MenuItemData", "NPCPresenceData", "ShopItemData", "SaveSlotData",
        "RecoveryFileData", "ShopBrowseItem", "DialogueChoiceData", "QuestSummaryData", "QuestDetailData",
        "QuestRewardData", "LevelUpStatGains", "DeathScreenData", "EndingScreenData", "ImmortalAscensionData",
        "NewsFeedData", "NewsFeedSection", "NewsFeedItem", "SettingsScreenData", "SettingsLanguageOption",
    };

    /// <summary>Emit methods whose arguments are ids and settings, not text.</summary>
    private static readonly HashSet<string> ElectronNoText = new(StringComparer.Ordinal)
    {
        "EmitSound", "EmitSoundStop", "EmitVolumeSet", "EmitSettingsApplied",
    };

    /// <summary>Payload members and parameters that carry ids, keys and styling, not text (any case).</summary>
    private static readonly HashSet<string> IdMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "e", "eventType", "type", "key", "slotKey", "id", "style", "icon", "category", "rarity", "path", "promptStage",
        "endingType", "lastPlayed", "phase", "perspective", "channel", "confirmAction", "settingKey", "newValue",
        "portraitKey", "listType", "soundId", "action", "context", "color", "colour", "sfx", "timeOfDay",
    };

    // ---------------------------------------------------------------------------------------------
    // Scan
    // ---------------------------------------------------------------------------------------------

    public sealed class Result
    {
        public List<Site> Sites { get; } = new();
        public List<Skipped> Skipped { get; } = new();
    }

    public static Result ScanRepo(string repoRoot, IReadOnlyList<HardcodedTextScanner.Exclusion> exclusions, IReadOnlyList<Source> sources)
    {
        var result = new Result();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(repoRoot, "Scripts"), "*.cs", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (exclusions.Any(e => (e.Kind == "file" && e.Target == rel) || (e.Kind == "folder" && rel.StartsWith(e.Target, StringComparison.Ordinal))))
                continue;
            var r = ScanSource(rel, File.ReadAllText(path), exclusions, sources);
            result.Sites.AddRange(r.Sites);
            result.Skipped.AddRange(r.Skipped);
        }
        return result;
    }

    public static Result ScanSource(string rel, string source, IReadOnlyList<HardcodedTextScanner.Exclusion> exclusions, IReadOnlyList<Source> sources)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();
        var excludedClasses = new HashSet<string>(exclusions.Where(e => e.Kind == "class").Select(e => e.Target), StringComparer.Ordinal);
        var slots = sources.Where(s => s.File == rel).GroupBy(s => s.Slot).ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);
        var result = new Result();
        var skipped = new Dictionary<(string Kind, string Slot), int>();
        var electronSeen = new HashSet<SyntaxNode>();

        // Excluded classes (admin consoles) are skipped whole: the walk does not enter them.
        foreach (var node in root.DescendantNodes(n => !(n is TypeDeclarationSyntax t && excludedClasses.Contains(t.Identifier.ValueText))))
        {
            if (node is TypeDeclarationSyntax td && excludedClasses.Contains(td.Identifier.ValueText))
                continue;

            if (node is InvocationExpressionSyntax inv && IsElectronEmit(inv, out string emitName))
            {
                if (ElectronNoText.Contains(emitName)) continue;
                var args = inv.ArgumentList.Arguments;
                for (int i = 0; i < args.Count; i++)
                {
                    if (emitName == "Emit" && i == 0) continue;
                    if (args[i].NameColon != null && IdMembers.Contains(args[i].NameColon!.Name.Identifier.ValueText)) continue;
                    foreach (var lit in ElectronLiterals(args[i].Expression))
                        if (electronSeen.Add(lit)) Add(result.Sites, rel, Electron, "ElectronBridge." + emitName, lit);
                }
            }
            else if (node is ObjectCreationExpressionSyntax oc && oc.Initializer != null && ElectronPayloadTypes.Contains(LastName(oc.Type)))
            {
                foreach (var lit in ElectronLiterals(oc))
                    if (electronSeen.Add(lit)) Add(result.Sites, rel, Electron, "new " + LastName(oc.Type), lit);
            }
            else if (node is ImplicitObjectCreationExpressionSyntax io && io.Initializer != null && ImpliedPayloadType(io) is string payload)
            {
                foreach (var lit in ElectronLiterals(io))
                    if (electronSeen.Add(lit)) Add(result.Sites, rel, Electron, "new " + payload, lit);
            }
            else if (node is ThrowStatementSyntax || node is ThrowExpressionSyntax)
            {
                var thrown = node is ThrowStatementSyntax ts ? ts.Expression : ((ThrowExpressionSyntax)node).Expression;
                if (thrown is ObjectCreationExpressionSyntax created && created.ArgumentList != null && ThrowIsShown(node, LastName(created.Type)))
                    foreach (var a in created.ArgumentList.Arguments)
                        foreach (var lit in TextLiterals(a.Expression))
                            Add(result.Sites, rel, Throw, "throw new " + LastName(created.Type), lit);
            }
            else if ((node is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression)) || node is InterpolatedStringExpressionSyntax)
            {
                if (slots.Count == 0) continue;
                if (node.Ancestors().Any(a => a is InterpolatedStringExpressionSyntax)) continue;
                if (!Counts((ExpressionSyntax)node)) continue;
                string slot = SlotOf(node);
                if (!slots.TryGetValue(slot, out string? kind)) continue;
                if (kind == "table" || kind == "nouns") Add(result.Sites, rel, Data, slot, (ExpressionSyntax)node);
                else skipped[(kind, slot)] = skipped.TryGetValue((kind, slot), out int n) ? n + 1 : 1;
            }
        }
        foreach (var ((kind, slot), n) in skipped.OrderBy(k => k.Key.Slot, StringComparer.Ordinal))
            result.Skipped.Add(new Skipped(rel, kind, slot, n));
        return result;
    }

    private static bool IsElectronEmit(InvocationExpressionSyntax inv, out string name)
    {
        name = "";
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return false;
        name = ma.Name.Identifier.ValueText;
        if (!name.StartsWith("Emit", StringComparison.Ordinal)) return false;
        return ma.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText == "ElectronBridge",
            AliasQualifiedNameSyntax aq => aq.Name.Identifier.ValueText == "ElectronBridge",
            MemberAccessExpressionSyntax inner => inner.Name.Identifier.ValueText == "ElectronBridge",
            _ => false,
        };
    }

    /// <summary>The payload type of a target typed new() that is an element of a list or array of payloads
    /// (new List&lt;ElectronBridge.MenuItemData&gt; { new() { ... } }, new MenuItemData[] { new() { ... } }).</summary>
    private static string? ImpliedPayloadType(ImplicitObjectCreationExpressionSyntax io)
    {
        if (io.Parent is not InitializerExpressionSyntax init) return null;
        TypeSyntax? container = init.Parent switch
        {
            ObjectCreationExpressionSyntax oc => oc.Type,
            ArrayCreationExpressionSyntax ac => ac.Type.ElementType,
            _ => null,
        };
        if (container == null) return null;
        return container.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Select(n => n.Identifier.ValueText)
            .FirstOrDefault(n => ElectronPayloadTypes.Contains(n));
    }

    private static string LastName(TypeSyntax t) => t switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => t.ToString(),
    };

    /// <summary>Text literals inside an Electron payload expression. Does not enter calls (Loc.Get and
    /// friends) apart from string.Format/Concat/Join, lambdas, or members named in IdMembers.</summary>
    private static IEnumerable<ExpressionSyntax> ElectronLiterals(SyntaxNode node)
    {
        switch (node)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                yield return lit;
                yield break;
            case InterpolatedStringExpressionSyntax interp:
                yield return interp;
                yield break;
            case InvocationExpressionSyntax call:
                if (call.Expression is MemberAccessExpressionSyntax ma
                    && ma.Name.Identifier.ValueText is "Format" or "Concat" or "Join"
                    && ma.Expression is PredefinedTypeSyntax or IdentifierNameSyntax { Identifier.ValueText: "String" })
                    foreach (var a in call.ArgumentList.Arguments)
                        foreach (var x in ElectronLiterals(a.Expression)) yield return x;
                else if (call.Expression is MemberAccessExpressionSyntax pm
                         && pm.Name.Identifier.ValueText is "PadLeft" or "PadRight" or "Trim" or "ToUpper" or "ToLower")
                    foreach (var x in ElectronLiterals(pm.Expression)) yield return x;
                yield break;
            case LambdaExpressionSyntax:
            case BinaryExpressionSyntax b when !b.IsKind(SyntaxKind.AddExpression) && !b.IsKind(SyntaxKind.CoalesceExpression):
            case ElementAccessExpressionSyntax:
                yield break;
            case AnonymousObjectMemberDeclaratorSyntax am:
                if (am.NameEquals != null && IdMembers.Contains(am.NameEquals.Name.Identifier.ValueText)) yield break;
                break;
            case AssignmentExpressionSyntax asg when asg.Parent is InitializerExpressionSyntax:
                if (asg.Left is IdentifierNameSyntax lid && IdMembers.Contains(lid.Identifier.ValueText)) yield break;
                foreach (var x in ElectronLiterals(asg.Right)) yield return x;
                yield break;
            case ArgumentSyntax arg when arg.NameColon != null && IdMembers.Contains(arg.NameColon.Name.Identifier.ValueText):
                yield break;
        }
        foreach (var child in node.ChildNodes())
            foreach (var x in ElectronLiterals(child)) yield return x;
    }

    /// <summary>Literals in a thrown message: the literal, an interpolation, concatenation, ?:.</summary>
    private static IEnumerable<ExpressionSyntax> TextLiterals(ExpressionSyntax e)
    {
        switch (e)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                yield return lit; break;
            case InterpolatedStringExpressionSyntax interp:
                yield return interp; break;
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.AddExpression) || bin.IsKind(SyntaxKind.CoalesceExpression):
                foreach (var x in TextLiterals(bin.Left)) yield return x;
                foreach (var x in TextLiterals(bin.Right)) yield return x;
                break;
            case ConditionalExpressionSyntax c:
                foreach (var x in TextLiterals(c.WhenTrue)) yield return x;
                foreach (var x in TextLiterals(c.WhenFalse)) yield return x;
                break;
            case ParenthesizedExpressionSyntax p:
                foreach (var x in TextLiterals(p.Expression)) yield return x;
                break;
        }
    }

    /// <summary>True when an enclosing try block has a catch for this exception that writes ex.Message to the player.</summary>
    private static bool ThrowIsShown(SyntaxNode throwNode, string thrownType)
    {
        SyntaxNode child = throwNode;
        foreach (var anc in throwNode.Ancestors())
        {
            if (anc is MemberDeclarationSyntax || anc is LocalFunctionStatementSyntax || anc is LambdaExpressionSyntax) return false;
            if (anc is TryStatementSyntax tryStmt && child == tryStmt.Block)
            {
                foreach (var c in tryStmt.Catches)
                {
                    string? caught = c.Declaration == null ? "Exception" : LastName(c.Declaration.Type);
                    if (caught != "Exception" && caught != thrownType) continue;
                    string? ex = c.Declaration?.Identifier.ValueText;
                    if (string.IsNullOrEmpty(ex)) return false; // caught but the message is not read
                    return c.Block.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(i => HardcodedTextScanner.IsOutputCall(i)
                                  && i.ArgumentList.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                                      .Any(m => m.Name.Identifier.ValueText == "Message" && m.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == ex));
                }
            }
            child = anc;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Slots
    // ---------------------------------------------------------------------------------------------

    /// <summary>Where a literal sits, climbing through +, ?:, ??, parentheses, casts, array and collection
    /// initialisers and collection expressions. See the class summary for the names.</summary>
    public static string SlotOf(SyntaxNode node)
    {
        SyntaxNode cur = node;
        string suffix = "";
        while (true)
        {
            var par = cur.Parent;
            switch (par)
            {
                case BinaryExpressionSyntax:
                case ConditionalExpressionSyntax:
                case ParenthesizedExpressionSyntax:
                case CastExpressionSyntax:
                case ArrayCreationExpressionSyntax:
                case ImplicitArrayCreationExpressionSyntax:
                case ExpressionElementSyntax:
                case CollectionExpressionSyntax:
                case InitializerExpressionSyntax ie when ie.IsKind(SyntaxKind.ArrayInitializerExpression) || ie.IsKind(SyntaxKind.CollectionInitializerExpression):
                case ObjectCreationExpressionSyntax oc when cur == oc.Initializer:
                case ImplicitObjectCreationExpressionSyntax io when cur == io.Initializer:
                    cur = par;
                    continue;
                case ArgumentSyntax arg when arg.Parent is TupleExpressionSyntax tup:
                    suffix = "/tuple#" + tup.Arguments.IndexOf(arg) + suffix;
                    cur = tup;
                    continue;
                case ArgumentSyntax arg when arg.Parent is ArgumentListSyntax al:
                {
                    int idx = al.Arguments.IndexOf(arg);
                    string callee = al.Parent switch
                    {
                        InvocationExpressionSyntax inv => "call:" + (inv.Expression is MemberAccessExpressionSyntax ma ? ma.Name.Identifier.ValueText : inv.Expression.ToString()),
                        ObjectCreationExpressionSyntax o => "new:" + LastName(o.Type),
                        ImplicitObjectCreationExpressionSyntax => "new()",
                        _ => al.Parent?.Kind().ToString() ?? "?",
                    };
                    return (arg.NameColon != null ? callee + "." + arg.NameColon.Name.Identifier.ValueText : callee + "#" + idx) + suffix;
                }
                case AssignmentExpressionSyntax asg when asg.Right == cur:
                {
                    string left = asg.Left switch
                    {
                        IdentifierNameSyntax id => id.Identifier.ValueText,
                        ImplicitElementAccessSyntax => "[k]=",
                        _ => Regex.Replace(asg.Left.ToString(), @"\s+", ""),
                    };
                    return (asg.Parent is InitializerExpressionSyntax ? "init." + left : "=" + left) + suffix;
                }
                case EqualsValueClauseSyntax when par.Parent is VariableDeclaratorSyntax vd:
                    return "decl:" + vd.Identifier.ValueText + suffix;
                case EqualsValueClauseSyntax when par.Parent is PropertyDeclarationSyntax pd:
                    return "decl:" + pd.Identifier.ValueText + suffix;
                case InitializerExpressionSyntax cx when cx.IsKind(SyntaxKind.ComplexElementInitializerExpression):
                    return "dictpair#" + cx.Expressions.IndexOf((ExpressionSyntax)cur) + suffix;
                case ReturnStatementSyntax:
                case ArrowExpressionClauseSyntax:
                    return "return" + suffix;
                case SwitchExpressionArmSyntax:
                    return "switcharm" + suffix;
                default:
                    return (par?.Kind().ToString() ?? "?") + suffix;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Word rules
    // ---------------------------------------------------------------------------------------------

    private static readonly Regex IdChars = new(@"^[A-Za-z0-9_.\-:/]+$", RegexOptions.Compiled);

    /// <summary>No whitespace and all lower case, all upper case, or with "_": an id, key or tag.</summary>
    public static bool IsIdentifierShaped(string text)
    {
        if (!IdChars.IsMatch(text)) return false;
        return text.Contains('_') || !text.Any(char.IsUpper) || !text.Any(char.IsLower);
    }

    private static string Visible(ExpressionSyntax node) => node switch
    {
        LiteralExpressionSyntax lit when HardcodedTextScanner.ColourNames.Contains(lit.Token.ValueText) => "",
        LiteralExpressionSyntax lit => lit.Token.ValueText,
        InterpolatedStringExpressionSyntax interp => string.Concat(
            interp.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText)),
        _ => "",
    };

    private static bool Counts(ExpressionSyntax node)
    {
        string v = Visible(node);
        return HardcodedTextScanner.IsWordy(v) && !IsIdentifierShaped(v.Trim());
    }

    private static void Add(List<Site> sites, string rel, string category, string slot, ExpressionSyntax node)
    {
        if (!Counts(node)) return;
        int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        string shown = Regex.Replace(node.ToString(), @"\s*\r?\n\s*", " ");
        sites.Add(new Site(rel, line, category, slot, shown));
    }

    // ---------------------------------------------------------------------------------------------
    // Reports
    // ---------------------------------------------------------------------------------------------

    public static string InventoryMarkdown(IReadOnlyList<Site> sites, int filesScanned)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hardcoded player-visible text outside output calls (Scripts/)");
        sb.AppendLine();
        sb.AppendLine($"Files scanned: {filesScanned}. Files with sites: {sites.Select(s => s.File).Distinct().Count()}. Sites: {sites.Count} ("
                      + string.Join(", ", Categories.Select(c => $"{c} {sites.Count(s => s.Category == c)}")) + ").");
        sb.AppendLine();
        foreach (var group in sites.GroupBy(s => s.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"## {group.Key} ({group.Count()}, {HardcodedTextScanner.SystemFor(group.Key)})");
            sb.AppendLine();
            foreach (var s in group)
                sb.AppendLine($"- {s.File}:{s.Line} {s.Category} `{s.Slot}` {s.Literal}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string RollupMarkdown(IReadOnlyList<Site> sites, IReadOnlyList<Skipped> skipped, IReadOnlyList<Source> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hardcoded player-visible text outside output calls, by system");
        sb.AppendLine();
        sb.AppendLine($"Total sites: {sites.Count}");
        sb.AppendLine();
        sb.AppendLine("| System | " + string.Join(" | ", Categories) + " | Total | Files |");
        sb.AppendLine("|---|" + string.Concat(Categories.Select(_ => "---|")) + "---|---|");
        var bySystem = sites.GroupBy(s => HardcodedTextScanner.SystemFor(s.File)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var system in HardcodedTextScanner.SystemOrder)
        {
            bySystem.TryGetValue(system, out var list);
            list ??= new List<Site>();
            sb.AppendLine($"| {system} | " + string.Join(" | ", Categories.Select(c => list.Count(s => s.Category == c)))
                          + $" | {list.Count} | {list.Select(s => s.File).Distinct().Count()} |");
        }
        sb.AppendLine("| total | " + string.Join(" | ", Categories.Select(c => sites.Count(s => s.Category == c))) + $" | {sites.Count} | {sites.Select(s => s.File).Distinct().Count()} |");
        sb.AppendLine();
        foreach (var system in HardcodedTextScanner.SystemOrder)
        {
            if (!bySystem.TryGetValue(system, out var list)) continue;
            sb.AppendLine($"## {system}");
            sb.AppendLine();
            foreach (var f in list.GroupBy(s => s.File).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                sb.AppendLine($"- {f.Key}: {f.Count()} (" + string.Join(", ", f.GroupBy(s => s.Category).Select(c => $"{c.Key} {c.Count()}")) + ")");
            sb.AppendLine();
        }
        foreach (var kind in new[] { "keyed", "names" })
        {
            var rows = skipped.Where(s => s.Kind == kind).ToList();
            sb.AppendLine(kind == "keyed"
                ? $"## Not counted: keyed tables ({rows.Sum(r => r.Count)} literals; English source of a Loc key the display resolves first)"
                : $"## Not counted: proper noun name lists ({rows.Sum(r => r.Count)} literals)");
            sb.AppendLine();
            foreach (var r in rows.OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Slot, StringComparer.Ordinal))
            {
                string reason = sources.FirstOrDefault(s => s.Kind == kind && s.File == r.File && s.Slot == r.Slot)?.Reason ?? "";
                sb.AppendLine($"- {r.File} `{r.Slot}`: {r.Count} ({reason})");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
