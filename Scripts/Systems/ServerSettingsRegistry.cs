using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.60.8 Phase 1: schema-driven registry of admin-tunable server settings
    /// for the MUD (online) deployment. Each descriptor names one setting, its
    /// type, validation bounds, and how to read/write it on the GameConfig
    /// statics. The web admin UI fetches the registry as JSON and renders an
    /// appropriate input control per setting; the C# backend uses the same
    /// registry to validate writes and route values into GameConfig.
    ///
    /// This is MUD-server-only. BBS sysops use the SysOpConfigSystem JSON file
    /// (per-BBS, file-based). The two systems can diverge -- BBS sysops have a
    /// different operational model and tooling.
    ///
    /// Adding a new tunable: append a descriptor below. The web UI auto-renders
    /// it on next refresh, the API auto-validates writes against the descriptor's
    /// bounds, and the SQLite-backed `server_config` table auto-persists the
    /// value. No other code changes required.
    /// </summary>
    public static class ServerSettingsRegistry
    {
        public enum SettingType { Bool, Int, Float, String }

        /// <summary>1.2.7: the server_config key of the telemetry operator switch.</summary>
        public const string TelemetryPromptKey = "telemetry_prompt";

        public class ServerSettingDescriptor
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public string Category { get; init; } = "";
            public SettingType Type { get; init; }
            public string DefaultValue { get; init; } = "";
            public double? MinValue { get; init; }
            public double? MaxValue { get; init; }
            public int? MaxLength { get; init; }
            public string Description { get; init; } = "";
            public string ChangeImpact { get; init; } = "";
            public Func<string> CurrentValue { get; init; } = () => "";
            public Action<string> Apply { get; init; } = _ => { };
            public Func<string, (bool ok, string? error)>? CustomValidator { get; init; }
        }

        public static readonly List<ServerSettingDescriptor> All = new()
        {
            // ============= DEATH =============
            new ServerSettingDescriptor
            {
                Key = "default_starting_resurrections",
                Label = "Starting Resurrections (new chars)",
                Category = "Death",
                Type = SettingType.Int,
                DefaultValue = "3",
                MinValue = 0, MaxValue = 99,
                Description = "Number of free deaths each new character starts with before permadeath fires. Setting to 0 means one death = permadeath.",
                ChangeImpact = "Affects new characters only. Existing players keep their current Resurrections counter unchanged.",
                CurrentValue = () => GameConfig.DefaultStartingResurrections.ToString(),
                Apply = v => { if (int.TryParse(v, out int n) && n >= 0 && n <= 99) GameConfig.DefaultStartingResurrections = n; }
            },
            new ServerSettingDescriptor
            {
                Key = "online_permadeath_enabled",
                Label = "Online Permadeath Enabled",
                Category = "Death",
                Type = SettingType.Bool,
                DefaultValue = "true",
                Description = "When ON, online deaths consume a resurrection and erase the character at zero. When OFF, deaths route to the legacy Temple/Deal/Accept penalty menu and no character is ever erased.",
                ChangeImpact = "Live, takes effect on the next death after the change.",
                CurrentValue = () => GameConfig.OnlinePermadeathEnabled ? "true" : "false",
                Apply = v => GameConfig.OnlinePermadeathEnabled = ParseBool(v)
            },

            // ============= DIFFICULTY =============
            new ServerSettingDescriptor
            {
                Key = "xp_multiplier",
                Label = "XP Multiplier",
                Category = "Difficulty",
                Type = SettingType.Float,
                DefaultValue = "1.0",
                MinValue = 0.1, MaxValue = 10.0,
                Description = "Global multiplier on every XP award. 1.0 is default. 2.0 = double XP, 0.5 = half XP.",
                ChangeImpact = "Live, takes effect on the next XP award.",
                CurrentValue = () => GameConfig.XPMultiplier.ToString("F2"),
                Apply = v => { if (float.TryParse(v, out float f)) GameConfig.XPMultiplier = Math.Clamp(f, 0.1f, 10.0f); }
            },
            new ServerSettingDescriptor
            {
                Key = "gold_multiplier",
                Label = "Gold Multiplier",
                Category = "Difficulty",
                Type = SettingType.Float,
                DefaultValue = "1.0",
                MinValue = 0.1, MaxValue = 10.0,
                Description = "Global multiplier on every gold award. 1.0 is default.",
                ChangeImpact = "Live, takes effect on the next gold award.",
                CurrentValue = () => GameConfig.GoldMultiplier.ToString("F2"),
                Apply = v => { if (float.TryParse(v, out float f)) GameConfig.GoldMultiplier = Math.Clamp(f, 0.1f, 10.0f); }
            },
            new ServerSettingDescriptor
            {
                Key = "monster_hp_multiplier",
                Label = "Monster HP Multiplier",
                Category = "Difficulty",
                Type = SettingType.Float,
                DefaultValue = "1.0",
                MinValue = 0.1, MaxValue = 10.0,
                Description = "Global multiplier on every monster's HP. Higher = monsters are tougher.",
                ChangeImpact = "Live, takes effect on the next monster spawn.",
                CurrentValue = () => GameConfig.MonsterHPMultiplier.ToString("F2"),
                Apply = v => { if (float.TryParse(v, out float f)) GameConfig.MonsterHPMultiplier = Math.Clamp(f, 0.1f, 10.0f); }
            },
            new ServerSettingDescriptor
            {
                Key = "monster_damage_multiplier",
                Label = "Monster Damage Multiplier",
                Category = "Difficulty",
                Type = SettingType.Float,
                DefaultValue = "1.0",
                MinValue = 0.1, MaxValue = 10.0,
                Description = "Global multiplier on monster basic-attack damage rolls against the player. Higher = monsters hit harder. Note: monster special abilities (DirectDamage, life drain, AoE) currently bypass this multiplier and use their raw damage values.",
                ChangeImpact = "Live, takes effect on the next basic monster attack.",
                CurrentValue = () => GameConfig.MonsterDamageMultiplier.ToString("F2"),
                Apply = v => { if (float.TryParse(v, out float f)) GameConfig.MonsterDamageMultiplier = Math.Clamp(f, 0.1f, 10.0f); }
            },
            // max_dungeon_level intentionally NOT exposed: capping below 100
            // breaks the 7 Old God boss floors (25/40/55/70/85/95/100), the
            // 7 Ancient Seals on specific floors, and the True / Conqueror
            // ending sequence at floor 100. Field remains in GameConfig for
            // BBS sysops who tune it via the file-based SysOpConfig, but
            // it's not a web-admin knob -- the only useful value for an
            // online server is 100, and any other value is a foot-gun.

            // ============= ACCESS =============
            new ServerSettingDescriptor
            {
                Key = "disable_online_play",
                Label = "Disable Online Play (kill switch)",
                Category = "Access",
                Type = SettingType.Bool,
                DefaultValue = "false",
                Description = "When ON, the [O] Online Play menu is hidden in the desktop / Steam clients. Useful as an emergency kill switch during incidents or maintenance.",
                ChangeImpact = "Live, takes effect on the next main-menu render.",
                CurrentValue = () => GameConfig.DisableOnlinePlay ? "true" : "false",
                Apply = v => GameConfig.DisableOnlinePlay = ParseBool(v)
            },
            new ServerSettingDescriptor
            {
                Key = "idle_timeout_minutes",
                Label = "Idle Timeout (minutes)",
                Category = "Access",
                Type = SettingType.Int,
                DefaultValue = "15",
                MinValue = 1, MaxValue = 60,
                Description = "Disconnect a session after this many minutes of no input. Lower values free up sessions faster but may annoy slow players.",
                ChangeImpact = "Live, takes effect on the next session.",
                CurrentValue = () => UsurperRemake.BBS.DoorMode.IdleTimeoutMinutes.ToString(),
                Apply = v => { if (int.TryParse(v, out int n)) UsurperRemake.BBS.DoorMode.IdleTimeoutMinutes = Math.Clamp(n, GameConfig.MinBBSIdleTimeoutMinutes, GameConfig.MaxBBSIdleTimeoutMinutes); }
            },

            // ============= PRIVACY =============
            // 1.2.7: the one operator switch of opt-in telemetry, for BBS doors and self hosted servers.
            new ServerSettingDescriptor
            {
                Key = TelemetryPromptKey,
                Label = "Combat Data Sharing (telemetry)",
                Category = "Privacy",
                Type = SettingType.Bool,
                DefaultValue = "false",
                Description = "When ON, each player is asked once whether to send combat data to usurper-reborn.net, and the fights of players who say yes are sent there (numbers only: no names, no chat, no save files). The uploads carry one random id that belongs to this server and is shared by its players. When OFF, nobody is asked and nothing is sent; turning it off deletes the rows not yet sent and the id.",
                ChangeImpact = "Live. Players are asked at their next login. Turning it off deletes the rows not yet sent and the id at once (or at the next start if the queue is busy).",
                CurrentValue = () => GameConfig.TelemetryPromptEnabled ? "true" : "false",
                Apply = v => TelemetryConsent.ApplyOperatorSwitch(ParseBool(v))
            },

            // ============= COMMUNICATION =============
            new ServerSettingDescriptor
            {
                Key = "motd",
                Label = "Message of the Day",
                Category = "Communication",
                Type = SettingType.String,
                DefaultValue = "Thanks for playing Usurper Reborn! Report bugs with the in-game ! command.",
                MaxLength = 500,
                Description = "Shown to every player at session start. Use it to announce maintenance windows, events, or rule changes. Plain text, no markup.",
                ChangeImpact = "Live, every new session sees the new MOTD.",
                CurrentValue = () => GameConfig.MessageOfTheDay ?? "",
                Apply = v => GameConfig.MessageOfTheDay = v ?? ""
            },
        };

        /// <summary>
        /// Read-only lookup by key. Used by SqlSaveBackend on startup-load and
        /// by the web API on write to find the descriptor for validation.
        /// </summary>
        public static ServerSettingDescriptor? Get(string key) =>
            All.FirstOrDefault(d => d.Key == key);

        /// <summary>
        /// Validate a candidate value against the descriptor's bounds without
        /// applying it. Returns (true, null) on pass, (false, reason) on fail.
        /// Used by the web API before any write to give the admin a useful
        /// error instead of silently clamping.
        /// </summary>
        public static (bool ok, string? error) Validate(string key, string value)
        {
            var d = Get(key);
            if (d == null) return (false, $"Unknown setting key: {key}");
            if (value == null) return (false, "Value is required.");

            switch (d.Type)
            {
                case SettingType.Bool:
                    if (value != "true" && value != "false" && value != "1" && value != "0")
                        return (false, "Value must be 'true' or 'false'.");
                    break;
                case SettingType.Int:
                    if (!int.TryParse(value, out int iv))
                        return (false, "Value must be a whole number.");
                    if (d.MinValue.HasValue && iv < d.MinValue.Value)
                        return (false, $"Value must be >= {d.MinValue.Value}.");
                    if (d.MaxValue.HasValue && iv > d.MaxValue.Value)
                        return (false, $"Value must be <= {d.MaxValue.Value}.");
                    break;
                case SettingType.Float:
                    if (!float.TryParse(value, out float fv))
                        return (false, "Value must be a number.");
                    if (d.MinValue.HasValue && fv < d.MinValue.Value)
                        return (false, $"Value must be >= {d.MinValue.Value:F2}.");
                    if (d.MaxValue.HasValue && fv > d.MaxValue.Value)
                        return (false, $"Value must be <= {d.MaxValue.Value:F2}.");
                    break;
                case SettingType.String:
                    if (d.MaxLength.HasValue && value.Length > d.MaxLength.Value)
                        return (false, $"Value must be <= {d.MaxLength.Value} characters.");
                    break;
            }

            if (d.CustomValidator != null)
                return d.CustomValidator(value);

            return (true, null);
        }

        /// <summary>
        /// Apply a value via the descriptor's Apply action. Caller is expected
        /// to have already called Validate. Silent no-op if the key is unknown
        /// (forward-compat for keys removed in future versions whose rows still
        /// exist in older DB snapshots).
        /// </summary>
        public static void ApplyConfigValue(string key, string value) =>
            Get(key)?.Apply(value);

        private static bool ParseBool(string v) =>
            v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }
}
