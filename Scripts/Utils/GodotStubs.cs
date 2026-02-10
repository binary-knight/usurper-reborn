using UsurperRemake.Utils;
using UsurperRemake.BBS;
// Godot stub implementations for standalone .NET compilation
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Godot
{
    // Base Godot classes
    public abstract class GodotObject
    {
        public virtual void _Ready() { }
        public virtual void QueueFree() { }
    }

    public abstract class Node : GodotObject
    {
        public string Name { get; set; } = "";
        public virtual void AddChild(Node child) { }
        public virtual T GetNode<T>(string path) where T : class, new() => SystemHelper.GetSystem<T>();
        public virtual Node GetNode(string path) => default(Node)!;
        public virtual void RemoveChild(Node child) { }
        public virtual Node GetParent() => default(Node)!;
    }

    public abstract class Control : Node
    {
        public Vector2 Size { get; set; }
        public virtual void Show() { }
        public virtual void Hide() { }
        public void SetAnchorsAndOffsetsPreset(int preset) { }
        
        public static class Preset
        {
            public const int FullRect = 15;
            public const int FULL_RECT = 15;
        }
    }

    public class RichTextLabel : Control
    {
        public string Text { get; set; } = "";
        public bool BbcodeEnabled { get; set; } = true;
        
        public void AppendText(string text) => Text += text;
        public void Clear() => Text = "";
        public bool ScrollFollowing { get; set; } = true;
        public bool FitContent { get; set; } = true;
        public void AddThemeFontOverride(string name, Font font) { }
        public void AddThemeFontSizeOverride(string name, int size) { }
        public void AddThemeStyleboxOverride(string name, StyleBox style) { }
        public void AddThemeColorOverride(string name, Color color) { }
    }

    public class LineEdit : Control
    {
        public string Text { get; set; } = "";
        public bool Visible { get; set; } = true;
#pragma warning disable CS0067 // Event is never used (stub for Godot compatibility)
        public event Action<string>? TextSubmitted;
#pragma warning restore CS0067
        
        public void AddThemeFontOverride(string name, Font font) { }
        public void AddThemeFontSizeOverride(string name, int size) { }
        public void Clear() => Text = "";
        public void GrabFocus() { }
    }

    public class Timer : Node
    {
        public double WaitTime { get; set; } = 1.0;
        public bool Autostart { get; set; } = false;
        public double Timeout { get; set; } = 1.0;
        public event Action? TimeoutEvent;
        
        public void Start() => Task.Delay(TimeSpan.FromSeconds(WaitTime)).ContinueWith(_ => TimeoutEvent?.Invoke());
        public void Stop() { }
    }

    public class Font : GodotObject
    {
        public int Size { get; set; }
        public string FontPath { get; set; } = "";
    }

    // Godot data types
    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        
        public static Vector2 Zero => new Vector2(0, 0);
    }

    public struct Color
    {
        public static readonly Color Black = new Color(0, 0, 0, 1);
        public static readonly Color White = new Color(1, 1, 1, 1);
        public static readonly Color Red = new Color(1, 0, 0, 1);
        public static readonly Color Green = new Color(0, 1, 0, 1);
        public static readonly Color Blue = new Color(0, 0, 1, 1);
        public static readonly Color Yellow = new Color(1, 1, 0, 1);
        public static readonly Color Cyan = new Color(0, 1, 1, 1);
        public static readonly Color Magenta = new Color(1, 0, 1, 1);
        public static readonly Color Transparent = new Color(0, 0, 0, 0);

        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public float A { get; set; }

        public Color(float r, float g, float b, float a = 1.0f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public string ToHtml()
        {
            return $"{(int)(R * 255):X2}{(int)(G * 255):X2}{(int)(B * 255):X2}";
        }

        // Add missing FromHtml method
        public static Color FromHtml(string htmlColor)
        {
            if (string.IsNullOrEmpty(htmlColor))
                return White;

            // Remove # if present
            if (htmlColor.StartsWith("#"))
                htmlColor = htmlColor.Substring(1);

            if (htmlColor.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(htmlColor.Substring(0, 2), 16);
                    int g = Convert.ToInt32(htmlColor.Substring(2, 2), 16);
                    int b = Convert.ToInt32(htmlColor.Substring(4, 2), 16);
                    return new Color(r / 255.0f, g / 255.0f, b / 255.0f, 1.0f);
                }
                catch
                {
                    return White;
                }
            }

            return White;
        }
    }

    // Godot utility functions
    public static class GD
    {
        private static Random random = new();
        
        public static void Print(params object[] args)
        {
            // Disabled - debug output was spamming screen readers
            // To re-enable for debugging, uncomment the line below:
            // Console.WriteLine(string.Join(" ", args));
        }
        
        public static void PrintErr(params object[] args)
        {
            Console.Error.WriteLine(string.Join(" ", args));
        }
        
        public static int RandRange(int min, int max)
        {
            return random.Next(min, max + 1);
        }
        
        public static float RandRange(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
        
        public static T Load<T>(string path) where T : class => default(T)!;
        public static GodotObject Load(string path) => default(GodotObject)!;
        
        public static int Randi() => random.Next();
        public static float Randf() => (float)random.NextDouble();
        public static int RandiRange(int from, int to) => random.Next(from, to + 1);
        public static float RandfRange(float from, float to) => from + (float)random.NextDouble() * (to - from);
    }

    // Resource classes
    public abstract class Resource : GodotObject { }
    public abstract class StyleBox : Resource { }
    public class StyleBoxFlat : StyleBox 
    { 
        public Color BgColor { get; set; } = Color.Black;
    }

    // File system
    public class FileAccess
    {
        public static bool FileExists(string path) => File.Exists(path);
        public static FileAccess? Open(string path, int mode) => new FileAccess();
        
        public static class ModeFlags
        {
            public const int Read = 1;
            public const int READ = 1;
            public const int Write = 2;
            public const int WRITE = 2;
        }
        
        public string GetAsText() => "";
        public void Close() { }
    }

    // Theme system
    public static class ThemeDB
    {
        public static Font? FallbackFont => new Font();
    }

    /// <summary>
    /// Get system instance - replacement for GetNode calls for system classes
    /// </summary>
    public static class SystemHelper
    {
        public static T GetSystem<T>() where T : class, new()
        {
            // Return singleton instances or create new ones for system classes
            if (typeof(T) == typeof(NewsSystem))
                return NewsSystem.Instance as T;
            if (typeof(T) == typeof(WorldSimulator))
                return new WorldSimulator() as T;
            if (typeof(T) == typeof(LocationManager))
                return new LocationManager() as T;
            if (typeof(T) == typeof(RelationshipSystem))
                return RelationshipSystem.Instance as T;
            if (typeof(T) == typeof(CombatEngine))
                return new CombatEngine() as T;
            
            return new T(); // Fallback for other types
        }
    }
}

// Additional stub classes for specific project needs
namespace UsurperRemake.Utils
{
    public interface ITerminal
    {
        void WriteLine(string text);
        void Write(string text);
        string ReadLine();
        void Clear();
    }

    public partial class TerminalUI : ITerminal
    {
        public void WriteLine(string text) => Console.WriteLine(text);
        public void WriteLine(string text, string color) => Console.WriteLine(text);
        public void Write(string text) => Console.Write(text);
        public string ReadLine() => Console.ReadLine() ?? "";
        public void Clear() => SafeClearScreen();
        public void ClearScreen() => SafeClearScreen();

        private void SafeClearScreen()
        {
            if (DoorMode.ShouldUseAnsiOutput)
            {
                Console.Write("\x1b[2J\x1b[H");
            }
            else
            {
                try { Console.Clear(); }
                catch (System.IO.IOException) { Console.Write("\x1b[2J\x1b[H"); }
            }
        }
        public async Task PressAnyKey(string message = "Press Enter to continue...")
        {
            Console.Write(message);
            Console.ReadKey();
        }
        public async Task<string> GetInputAsync(string prompt = "")
        {
            Console.Write(prompt);
            return Console.ReadLine() ?? "";
        }
    }

    public class SaveManager
    {
        public static void SaveGame(object data, string filename) { }
        public static T LoadGame<T>(string filename) where T : new() => new T();
        public static bool SaveExists(string filename) => false;
        
        /// <summary>
        /// Load player character from save file
        /// </summary>
        public static Player? LoadPlayer(string playerName)
        {
            // Stub implementation - would load from file in real game
            return new Player
            {
                Name1 = playerName,
                Name2 = playerName,
                AI = CharacterAI.Human,
                Level = 1,
                HP = 100,
                MaxHP = 100,
                Allowed = true,
                TurnsRemaining = GameConfig.TurnsPerDay // give starting turns
            };
        }
        
        /// <summary>
        /// Save player character to save file
        /// </summary>
        public static void SavePlayer(Character player)
        {
            // Stub implementation - would save to file in real game
            Console.WriteLine($"Saving player: {player.DisplayName}");
        }
        
        /// <summary>
        /// Save player character to save file (overload)
        /// </summary>
        public static void SavePlayer(Character player, string filename)
        {
            SavePlayer(player);
        }
    }

    public class GodWorldLocation
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
