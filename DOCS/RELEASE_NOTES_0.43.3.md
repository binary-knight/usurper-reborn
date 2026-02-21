# v0.43.3 - ANSI Art Portraits

Character creation now features colorful ANSI art portraits during race and class selection, bringing classic BBS art into the game.

---

## New Features

### ANSI Art Race Portraits
When selecting your character's race, a side-by-side preview now displays a detailed ANSI art portrait on the left with race stats on the right. Portraits fit within the 80x24 BBS terminal standard. Currently available for Orc, Dwarf, Elf, Half-Elf, and Troll, with more races coming.

### ANSI Art Class Portraits
Class selection also features side-by-side ANSI art portraits with class stats, abilities, and description. Currently available for Magician, Assassin, Warrior, and Paladin, with more classes coming.

### Side-by-Side Preview Layout
Both race and class preview screens use a new side-by-side layout: a 38-character-wide ANSI portrait on the left and a 38-character-wide stats panel on the right, separated by box-drawing borders. The stats panel shows paired stat bars, strengths, and descriptions — all fitting within 24 rows for BBS compatibility. Classes and races without portraits gracefully fall back to the existing card layout.

### Credits for ANSI Art
Added credit for [xbit](https://x-bit.org/) for providing the race and class portrait ANSI art, visible in both the in-game credits and the website credits section.

### Training Respec (Draught of Forgetting)
Visit the Level Master's Training menu and choose [R] Reset to unlearn your skill proficiencies and reclaim your training points. Reset a single skill or all skills at once. The Level Master charges a gold fee (500 + 100 per level) and administers the "Draught of Forgetting" — a ritual that strips away your trained muscle memory and restores your potential. Full refund of all training points invested.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.43.3; added RespecBaseGoldCost and RespecGoldPerLevel constants |
| `Scripts/Systems/TrainingSystem.cs` | Added CalculateTotalPointsInvested(), ResetSkillProficiency(), ResetAllProficiencies() methods; added ShowResetMenu(), ShowResetSingleSkillMenu(), ShowResetAllMenu() UI methods with lore flavor text; added [R] Reset option to ShowTrainingMenu() |
| `Scripts/UI/RacePortraits.cs` | **NEW** — Added OrcPortrait, DwarfPortrait, ElfPortrait, HalfElfPortrait, TrollPortrait (race), MagicianPortrait, AssassinPortrait, WarriorPortrait, PaladinPortrait (class) arrays; PortraitData class; Portraits and ClassPortraits dictionaries; GetCroppedPortrait() and GetCroppedClassPortrait() methods; TrimToVisibleWidth() ANSI-aware string trimming |
| `Scripts/Systems/CharacterCreationSystem.cs` | Added ShowRacePreviewSideBySide(), BuildStatsPanel() for race portraits; added ShowClassPreviewSideBySide(), BuildClassStatsPanel() for class portraits; ShowRacePreview() and ShowClassPreview() dispatch to side-by-side when portrait exists, fallback to card layout |
| `Scripts/UI/TerminalEmulator.cs` | Added WriteRawAnsi() method for raw ANSI escape code output used by portrait rendering |
| `Scripts/Core/GameEngine.cs` | Added ANSI ART credit section for xbit in ShowCredits() |
| `web/index.html` | Added xbit credit card with link to x-bit.org in credits section |
