using System;
using System.IO;
using System.Reflection;
using System.Text;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14 evidence: both Main Street layouts at tier 3 online, ANSI stripped (runs only with USURPER_CLASSIC_SCREENS_OUT set).</summary>
[Collection("SharedGameSingletons")]
public class ClassicMainStreetScreens1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void RenderBothLayouts_ForTheMaintainer()
    {
        string? path = Environment.GetEnvironmentVariable("USURPER_CLASSIC_SCREENS_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder();
        foreach (bool classic in new[] { false, true })
        foreach (string mode in new[] { "visual", "screenreader", "bbs" })
        {
            object oldOnline = ClassicMainStreetLayout1114Tests.OnlineFlag.GetValue(null)!;
            object? oldChat = ClassicMainStreetLayout1114Tests.ChatFallback.GetValue(null);
            bool oldCompact = GameConfig.CompactMode;
            try
            {
                ClassicMainStreetLayout1114Tests.OnlineFlag.SetValue(null, true);
                ClassicMainStreetLayout1114Tests.ChatFallback.SetValue(null,
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OnlineChatSystem)));
                GameConfig.CompactMode = mode == "bbs";
                var (street, output, hero) = ClassicMainStreetLayout1114Tests.Rig(10, classic, "", "", "");
                hero.ScreenReaderMode = mode == "screenreader";
                hero.ClassicTipDraws = MainStreetLocation.ClassicTipDrawLimit - 1; // the districts screen shows its tip once more
                typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
                sb.AppendLine($"==================== Main Street, level 10 (tier 3), online, {mode}, {(classic ? "CLASSIC layout" : "districts layout")} ====================");
                sb.AppendLine(ClassicMainStreetLayout1114Tests.Plain(street, output));
            }
            finally
            {
                ClassicMainStreetLayout1114Tests.OnlineFlag.SetValue(null, oldOnline);
                ClassicMainStreetLayout1114Tests.ChatFallback.SetValue(null, oldChat);
                GameConfig.CompactMode = oldCompact;
            }
        }
        File.WriteAllText(path, sb.ToString());
    }
}
