# Steam copy for 1.3.4 (edited into the Steam page by hand)

Steam formats with BBCode, so (a) is written in it. Every line about 1.3.0 to 1.3.3 comes from the shipped release notes in
DOCS/release-notes. The 1.3.4 lines are a TODO: 1.3.4 is not finished, and only what is merged at the release goes in.

## a) Announcement for 1.3.4 (to pin)

```
[h1]Usurper Reborn 1.3.4[/h1]

1.3.4 is out. Since 1.3.0, difficulty has been reworked step by step. Here is what changed, in order.

[h2]1.3.0: the dungeon fights back[/h2]
[list]
[*]Monsters use their abilities with more sense.
[*]On floors 4 to 10, groups mix monster types and sizes. A mixed group can include a spellcaster.
[*]Elite monsters from floor 3 have more HP, damage and defence, and a named trait.
[*]Your physical attacks roll to hit. The number you need is shown.
[*]Healing potions heal at least 20% of your maximum HP.
[/list]

[h2]1.3.2: tuning and fixes[/h2]
[list]
[*]Class combat bonuses now apply in fights.
[*]Broken monster abilities were fixed.
[/list]

[h2]1.3.3: floors 25 to 100[/h2]
[list]
[*]Monsters from level 25 gain HP and damage with each level. The deep floors are built for a full team of five.
[*]Monster groups from level 25 grow a little each level instead of jumping at levels 31 and 61.
[*]An ally at 0 HP is Downed, not killed. Press U to Rescue them with a healing potion.
[*]An ally is lost only when a monster finishes them. Predators and bosses announce a finish first.
[/list]

[h2]1.3.4[/h2]
[list]
[*]TODO at the release: one short line per player-facing change that merged in 1.3.4.
[/list]

[h2]Next[/h2]
1.3.5 tunes dungeon floors 11 to 24, measured in 1.3.4. After it: party survival (1.5), then the Player District (1.4).

The roadmap: https://usurper-reborn.net/#roadmap
```

Not yet true, so not in the text above: the Old God fights and the prestige classes have not been retuned. Their measurement
runs in the 1.3.4 work, and whether they change is still open. Add a line for them only if a change merges.

## b) Store page "About" paragraph

```
Usurper Reborn is free and open source under the GPL v2: https://github.com/binary-knight/usurper-reborn
You can play it free in your browser at https://usurper-reborn.net, over SSH or telnet, or from the GitHub release builds. The Steam version is the same game with a terminal and fonts bundled. Buying it is an optional way to support development.
```

## c) Secondary store listing

The line "Source code available at: [your-repo]" still has the placeholder. Replace it with:

```
Source code available at: https://github.com/binary-knight/usurper-reborn
```
