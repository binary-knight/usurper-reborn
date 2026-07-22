using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.UI;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.65.7 AI-generated NPC portraits.
    ///
    /// Every named NPC can have a real painted portrait (PixelLab Pixflux API,
    /// the same service that produced the Electron combat sprites), generated
    /// ONCE in the background the first time a player talks to them, cached to
    /// disk forever, and rendered into the terminal at whatever fidelity the
    /// player's connection supports (see PortraitEncoder tiers). Architecture
    /// mirrors the LLM moments system: env-var config, per-server daily budget,
    /// fire-and-forget background work, and an always-working fallback (the
    /// classic procedural PortraitGenerator) so the game never blocks on, or
    /// breaks from, the network.
    ///
    /// Environment variables (all optional; generation is OFF by default):
    ///   USURPER_PORTRAIT_ENABLED     "true" to enable generation. Default false.
    ///   USURPER_PIXELLAB_API_KEY     PixelLab API key. Never logged.
    ///   USURPER_PORTRAIT_ENDPOINT    Override the pixflux endpoint (rare).
    ///   USURPER_PORTRAIT_DAILY_CAP   Max generation attempts per UTC day.
    ///                                Default 60 (whole 200-NPC world in ~4 days;
    ///                                new immigrants trickle in well under cap).
    ///   USURPER_PORTRAIT_TIMEOUT_MS  Per-request timeout. Default 45000
    ///                                (image generation takes 5-30s).
    ///   USURPER_PORTRAIT_DIR         Cache directory override. Default:
    ///                                {ApplicationData}/UsurperReloaded/portraits
    ///
    /// DISPLAY always works when a cached portrait exists, regardless of the
    /// generation settings -- so a sysop can pre-generate portraits offline
    /// (or copy the cache dir between machines) and never configure a key.
    /// </summary>
    public static class NPCPortraitSystem
    {
        private const string DefaultEndpoint = "https://api.pixellab.ai/v2/create-image-pixflux";
        private const int GenSize = 128; // px; PoC-validated sweet spot for painted busts

        private static readonly HttpClient Http = new HttpClient();

        // In-flight + failure-cooldown guards, process-wide (MUD server hosts
        // many sessions in one process; only one generation per NPC at a time).
        private static readonly ConcurrentDictionary<string, byte> InFlight = new();
        private static readonly ConcurrentDictionary<string, DateTime> FailedUntil = new();
        private static readonly TimeSpan FailureCooldown = TimeSpan.FromHours(6);

        // Encoded-row memory cache: "{pngPath}|{tier}" -> rows. Bounded; a full
        // clear at the cap is fine (entries rebuild from disk in ~1ms).
        private static readonly ConcurrentDictionary<string, string[]> EncodedCache = new();
        private const int EncodedCacheCap = 512;

        // Daily generation budget (attempts, not successes -- a failing key
        // must not retry unbounded). In-memory with UTC rollover, mirroring
        // LLMBudget's original shape.
        private static readonly object BudgetLock = new();
        private static int _attemptsToday;
        private static DateTime _budgetDayUtc = DateTime.UtcNow.Date;

        #region Settings

        public static bool GenerationEnabled => GetBool("USURPER_PORTRAIT_ENABLED", false);
        public static string? ApiKey => GetString("USURPER_PIXELLAB_API_KEY");
        public static string Endpoint => GetString("USURPER_PORTRAIT_ENDPOINT") ?? DefaultEndpoint;
        public static int DailyCap => (int)GetLong("USURPER_PORTRAIT_DAILY_CAP", 60);
        // Floor of 1s: 0 would cancel every request instantly and negative
        // values throw in the CancellationTokenSource constructor.
        public static int TimeoutMs => Math.Max(1_000, (int)GetLong("USURPER_PORTRAIT_TIMEOUT_MS", 45_000));

        /// <summary>True when generation is fully configured.</summary>
        public static bool IsGenerationActive()
        {
            return GenerationEnabled && !string.IsNullOrWhiteSpace(ApiKey);
        }

        public static string CacheDir
        {
            get
            {
                var overrideDir = GetString("USURPER_PORTRAIT_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir!;
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "UsurperReloaded", "portraits");
            }
        }

        #endregion

        #region Display

        /// <summary>
        /// Try to display a cached AI portrait for this character, framed in
        /// the same box-and-name-bar layout as the classic PortraitGenerator.
        /// Returns true if a portrait was rendered; false if the caller should
        /// fall back to the procedural portrait (and, when generation is
        /// configured, a background generation has been kicked off so the
        /// portrait exists on a future visit).
        /// </summary>
        public static bool TryDisplayPortrait(TerminalEmulator terminal, global::Character npc)
        {
            try
            {
                if (terminal == null || npc == null) return false;
                // Art-suppressed contexts: never render, never generate.
                if (GameConfig.ScreenReaderMode || GameConfig.DisableCharacterMonsterArt) return false;
                if (terminal.IsPlainText) return false;
                // Electron renders its own UI; its ANSI parser predates
                // truecolor SGR support. Keep the classic path there.
                // GameConfig.ElectronMode covers local single-player; the
                // ConnectionType check covers Electron users playing ONLINE
                // (proxied through the web relay, where ElectronMode is false
                // in the server process but the same client parser -- no SGR 48
                // support at all -- sits on the other end).
                if (GameConfig.ElectronMode) return false;
                if (UsurperRemake.Server.SessionContext.Current?.ConnectionType == "Electron") return false;

                string pngPath = GetCachePath(npc);
                if (!File.Exists(pngPath))
                {
                    KickGeneration(npc, pngPath);
                    return false;
                }

                var tier = ResolveTier(terminal);
                string[] rows;
                try
                {
                    rows = GetEncodedRows(pngPath, tier);
                }
                catch (Exception decodeEx)
                {
                    // A cached PNG we cannot decode (out-of-profile sysop file,
                    // truncated write from another process) would otherwise be a
                    // permanent dead-end: File.Exists stays true so generation
                    // never re-fires. Quarantine it so the next visit regenerates.
                    DebugLogger.Instance.LogWarning("PORTRAIT",
                        $"Cached portrait '{Path.GetFileName(pngPath)}' failed to decode ({decodeEx.Message}); quarantining as .bad");
                    try { File.Move(pngPath, pngPath + ".bad", overwrite: true); } catch { /* best effort */ }
                    return false;
                }
                RenderFramed(terminal, npc, rows);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("PORTRAIT",
                    $"Display failed for '{npc?.Name2 ?? "?"}': {ex.Message}. Falling back to procedural.");
                return false;
            }
        }

        /// <summary>
        /// Pick the encoder tier for the current session's terminal capability.
        ///   BBS door / CP437       -> 16-color ANSI (the BBS-scene half-block style)
        ///   BBS / Steam / Local    -> 16-color ANSI. Steam/Local means the game's own
        ///                             [O] Online Play client is on the other end, and
        ///                             every client older than v0.65.7 pipes server
        ///                             output through a legacy SGR parser that shreds
        ///                             38;2/38;5 sequences into color noise (the
        ///                             AI-portrait corruption report). 16-color SGR is
        ///                             the dialect those clients parse correctly.
        ///                             TODO: promote to TrueColor once pre-0.65.7
        ///                             clients have aged out of the field.
        ///   SSH / raw-TCP MUD      -> xterm-256 (client could be anything; 256 is safe)
        ///   Web / single-player    -> 24-bit truecolor (xterm.js and WezTerm support it)
        /// </summary>
        private static PortraitEncoder.Tier ResolveTier(TerminalEmulator terminal)
        {
            if (terminal.UseCp437 || UsurperRemake.BBS.DoorMode.IsInDoorMode)
                return PortraitEncoder.Tier.Ansi16;
            var ct = UsurperRemake.Server.SessionContext.Current?.ConnectionType;
            return ct switch
            {
                "BBS" or "Steam" or "Local" => PortraitEncoder.Tier.Ansi16,
                "SSH" or "MUD" => PortraitEncoder.Tier.Xterm256,
                _ => PortraitEncoder.Tier.TrueColor,
            };
        }

        private static string[] GetEncodedRows(string pngPath, PortraitEncoder.Tier tier)
        {
            // mtime in the key so an in-place PNG replacement (sysop drops a new
            // file into the cache dir) shows fresh art without a restart.
            string key = pngPath + "|" + (int)tier + "|" + File.GetLastWriteTimeUtc(pngPath).Ticks;
            if (EncodedCache.TryGetValue(key, out var cached)) return cached;

            var (rgba, w, h) = MiniPng.Decode(File.ReadAllBytes(pngPath));
            var rows = PortraitEncoder.Encode(rgba, w, h, tier);

            if (EncodedCache.Count >= EncodedCacheCap) EncodedCache.Clear();
            EncodedCache[key] = rows;
            return rows;
        }

        /// <summary>
        /// Frame layout matches PortraitGenerator.Render (3-space pad, 34-wide
        /// box, name + race/class bar) so the two paths are visually
        /// interchangeable at the call site. Borders go through the normal
        /// markup writer (CP437-safe); image rows go through WriteRawAnsi,
        /// the same split the race-portrait screen has used since v0.43.3.
        /// </summary>
        private static void RenderFramed(TerminalEmulator terminal, global::Character npc, string[] rows)
        {
            const string bc = "gray";
            string horiz = new string('═', PortraitEncoder.Cols);

            terminal.WriteLine($"   ╔{horiz}╗", bc);
            foreach (var row in rows)
            {
                terminal.Write("   ║", bc);
                terminal.WriteRawAnsi(row);
                terminal.WriteLine("║", bc);
            }

            string name = npc.Name2 ?? npc.Name1 ?? "Unknown";
            if (name.Length > 32) name = name.Substring(0, 32);
            string raceClass = $"{npc.Race} {npc.Class}";
            if (raceClass.Length > 32) raceClass = raceClass.Substring(0, 32);

            terminal.WriteLine($"   ╠{horiz}╣", bc);
            terminal.Write("   ║", bc);
            terminal.Write(CenterText(name, PortraitEncoder.Cols), "bright_yellow");
            terminal.WriteLine("║", bc);
            terminal.Write("   ║", bc);
            terminal.Write(CenterText(raceClass, PortraitEncoder.Cols), "gray");
            terminal.WriteLine("║", bc);
            terminal.WriteLine($"   ╚{horiz}╝", bc);
        }

        private static string CenterText(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            int left = (width - text.Length) / 2;
            return new string(' ', left) + text + new string(' ', width - text.Length - left);
        }

        #endregion

        #region Cache key

        /// <summary>
        /// Stable identity for the cached portrait. Keyed on the traits that
        /// should change the face (name / race / sex / class) and nothing
        /// else -- aging or leveling must NOT regenerate, and the known NPC
        /// id-drift patterns all preserve the display name, so name is the
        /// most durable anchor available.
        /// </summary>
        public static string GetCacheKey(global::Character npc)
        {
            // v0.65.7 audit F1: the crown is part of the painted portrait, so the
            // King flag must be part of the identity -- otherwise a portrait
            // generated pre-coronation shows no crown for a sitting king, and a
            // dethroned NPC (or a later immigrant reusing the freed identity)
            // wears a crown forever. Appended CONDITIONALLY so every existing
            // cached commoner file stays valid; an NPC who ever holds the throne
            // gets at most two files, and the correct one is picked live.
            string identity = $"{npc.Name2 ?? npc.Name1 ?? "unknown"}|{npc.Race}|{npc.Sex}|{npc.Class}"
                + (npc.King ? "|king" : "");
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(identity));
            return Convert.ToHexString(hash, 0, 5).ToLowerInvariant(); // 10 hex chars
        }

        public static string GetCachePath(global::Character npc)
        {
            string safeName = SanitizeName(npc.Name2 ?? npc.Name1 ?? "unknown");
            return Path.Combine(CacheDir, $"{safeName}_{GetCacheKey(npc)}.png");
        }

        private static string SanitizeName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            return s.Length == 0 ? "npc" : (s.Length > 24 ? s.Substring(0, 24) : s);
        }

        #endregion

        #region Generation

        /// <summary>
        /// Fire-and-forget background generation. Guards: feature configured,
        /// daily budget, per-NPC in-flight, per-NPC failure cooldown. The
        /// calling session renders the procedural fallback this visit; the
        /// painted portrait exists by the next one.
        /// </summary>
        private static void KickGeneration(global::Character npc, string pngPath)
        {
            if (!IsGenerationActive()) return;

            string key = pngPath;
            if (FailedUntil.TryGetValue(key, out var until) && DateTime.UtcNow < until) return;
            // In-flight dedup BEFORE the budget spend, so two sessions talking
            // to the same NPC in the same instant burn one slot, not two.
            if (!InFlight.TryAdd(key, 1)) return;
            if (!TryConsumeBudget()) { InFlight.TryRemove(key, out _); return; }

            // Snapshot everything the background task needs BEFORE going async
            // (world-sim can mutate the NPC mid-flight).
            string prompt = BuildPrompt(npc);
            string npcName = npc.Name2 ?? npc.Name1 ?? "unknown";

            _ = Task.Run(async () =>
            {
                try
                {
                    await GenerateAndCacheAsync(npcName, prompt, pngPath);
                }
                catch (Exception ex)
                {
                    FailedUntil[key] = DateTime.UtcNow + FailureCooldown;
                    DebugLogger.Instance.LogWarning("PORTRAIT",
                        $"Generation failed for '{npcName}': {ex.Message}. Cooldown {FailureCooldown.TotalHours}h.");
                }
                finally
                {
                    InFlight.TryRemove(key, out _);
                }
            });
        }

        private static async Task GenerateAndCacheAsync(string npcName, string prompt, string pngPath)
        {
            // Prove the cache dir is writable BEFORE spending a paid API call:
            // with an unwritable USURPER_PORTRAIT_DIR every attempt would burn
            // a real generation, throw on the write, and repeat daily forever.
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

            var payload = JsonSerializer.Serialize(new
            {
                description = prompt,
                image_size = new { width = GenSize, height = GenSize },
                text_guidance_scale = 12.0,
                detail = "highly detailed",
                no_background = false, // painted dark backdrop is part of the look
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeoutMs);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await Http.SendAsync(req, cts.Token);
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {Truncate(body, 120)}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("image", out var imageEl)
                || !imageEl.TryGetProperty("base64", out var b64El)
                || b64El.GetString() is not string b64 || b64.Length == 0)
                throw new InvalidDataException("No image in API response");

            byte[] png = Convert.FromBase64String(b64);
            // Validate before caching: a corrupt file must never poison the cache.
            MiniPng.Decode(png);

            // Unique tmp name: two processes sharing one cache dir (server +
            // local single-player) must not collide on a fixed ".tmp".
            string tmp = pngPath + "." + Path.GetRandomFileName() + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tmp, png);
                File.Move(tmp, pngPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            }

            DebugLogger.Instance.LogInfo("PORTRAIT",
                $"Generated portrait for '{npcName}' ({png.Length} bytes, {sw.ElapsedMilliseconds}ms) -> {Path.GetFileName(pngPath)}");
        }

        private static bool TryConsumeBudget()
        {
            lock (BudgetLock)
            {
                var today = DateTime.UtcNow.Date;
                if (today != _budgetDayUtc)
                {
                    _budgetDayUtc = today;
                    _attemptsToday = 0;
                }
                if (_attemptsToday >= DailyCap) return false;
                _attemptsToday++;
                return true;
            }
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max);

        #endregion

        #region Prompt

        /// <summary>
        /// Portrait prompt from the character's actual identity. Kept in the
        /// PoC-validated Darkest Dungeon house style so NPC portraits match
        /// the class/monster sprites the Electron client already ships.
        /// </summary>
        public static string BuildPrompt(global::Character npc)
        {
            string sex = npc.Sex == CharacterSex.Female ? "female" : "male";
            string age = npc.Age >= 60 ? "elderly, gray-haired " : npc.Age >= 45 ? "weathered middle-aged " : npc.Age <= 21 && npc.Age > 0 ? "youthful " : "";
            string race = npc.Race.ToString() switch
            {
                "Human" => "human",
                "Elf" => "elf with pointed ears and fine features",
                "HalfElf" => "half-elf with subtly pointed ears",
                "Dwarf" => "dwarf with a heavy braided beard and broad nose",
                "Orc" => "orc with tusks and a heavy jaw",
                "Troll" => "troll with mottled green-gray skin and a heavy brow",
                "Hobbit" => "halfling with curly hair and round cheeks",
                "Gnome" => "gnome with a large nose and bright eyes",
                "Gnoll" => "gnoll, spotted hyena-headed humanoid with a broad blunt muzzle and rounded ears",
                "Mutant" => "mutant humanoid with asymmetric features and patchy skin",
                _ => npc.Race.ToString().ToLowerInvariant(),
            };
            string cls = npc.Class switch
            {
                CharacterClass.Warrior => "battle-scarred warrior in dented plate armor",
                CharacterClass.Paladin => "holy knight in polished armor with a bright tabard",
                CharacterClass.Ranger => "ranger in a hooded green cloak with a quiver strap",
                CharacterClass.Assassin => "assassin in dark leathers with a half-mask",
                CharacterClass.Bard => "bard in a colorful doublet with a feathered hat",
                CharacterClass.Jester => "jester in motley with painted face and belled hood",
                CharacterClass.Sage => "sage scholar in ornate robes with arcane trinkets",
                CharacterClass.Magician => "wizard in flowing robes with a wide-brimmed hat",
                CharacterClass.Cleric => "cleric in white and gold vestments with a holy symbol",
                CharacterClass.Barbarian => "barbarian in furs with war paint",
                CharacterClass.Alchemist => "alchemist with brass goggles and a stained leather apron",
                CharacterClass.MysticShaman => "tribal shaman with bone totems and ritual paint",
                CharacterClass.Tidesworn => "ocean warrior in coral and shell armor",
                CharacterClass.Wavecaller => "sea mage in flowing aqua robes with pearl jewelry",
                CharacterClass.Cyclebreaker => "time warrior in cracked armor leaking temporal light",
                CharacterClass.Abysswarden => "grim warden in spiked dark plate wrapped in chains",
                CharacterClass.Voidreaver => "void mage in torn dark robes with a skull half-mask",
                _ => npc.Class.ToString().ToLowerInvariant(),
            };
            string crown = npc.King ? ", wearing an ornate royal crown" : "";
            string bearing = npc.Darkness > 400 ? ", sinister expression with cruel eyes"
                : npc.Chivalry > 400 ? ", noble bearing with kind eyes" : "";

            return $"Head and shoulders portrait of a {age}{sex} {race}, {cls}{crown}{bearing}, " +
                   "dark moody background, dramatic side lighting. " +
                   "Dark fantasy Darkest Dungeon style character portrait.";
        }

        #endregion

        #region Env helpers

        private static string? GetString(string name)
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            catch { return null; }
        }

        private static bool GetBool(string name, bool def)
        {
            var v = GetString(name);
            if (v == null) return def;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static long GetLong(string name, long def)
        {
            var v = GetString(name);
            if (v == null) return def;
            return long.TryParse(v, out long parsed) ? parsed : def;
        }

        #endregion
    }
}
