# 4.3.0.0

### New
- New look with a tab per mode. Your status and gold / silver / XP per hour stay on top, and the window switches to the mode that is running. Prefer Dalamud's own colours? Turn off "Use BOCCHI theme" under Settings → UI
- Each tab explains its mode in one line and warns you when a plugin it needs isn't running
- Each tab shows its most-used settings at the bottom, and the World tab is now called FATEs & CEs
- The Illegal Mode tab says why it is waiting or paused (for example when "Stop after return and teleport" is on)
- Search box in the settings; the cog in the title bar opens the settings for the tab you are on
- Completionist is now a switch in the Illegal Mode tab
- Auto revive (off by default): accept raises, and go back to camp if nobody raises you. Never inside Forked Tower, and in a FATE or CE it waits until that is over so you keep the reward
- Triage Mode can wait a moment before raising, so nearby healers get the first chance
- New option: use Return for long trips instead of walking past mobs to an aethernet shard
- Plainer hover texts in all four languages. Spotted odd Japanese, Korean or Chinese? Let us know
- Travel maps now come with BOCCHI, so the first start is quicker

### Fighting
- BOCCHI now fights back when attacked: while riding (mobs your level or lower), while waiting for a pot, CE or Return, and during Treasure Hunt, Carrot Hunt and Triage Mode
- With combat set to None, BOCCHI keeps moving when attacked and fully lets go of your rotation plugin
- Rotation plugins and BossMod presets are switched on and off more reliably
- Warrior with the BossMod rotation no longer uses AoE skills on a single enemy

### Pots and treasure
- Pots now work with "Do FATEs" turned off
- The second pot chest search no longer goes back to the first pot's spots, and no Magical Elixir is wasted while Cache Me If You Can is still up
- No more mounting up again just to reach a pot from its waiting spot
- Treasure Hunt and Carrot Hunt no longer get stuck on failed teleports, coffers or carrots they can't reach, or the Wanderer's Haven ledge coffer
- After you die, Treasure Hunt and Carrot Hunt plan a fresh route instead of riding straight from camp

### Mob Farmer
- No more waiting forever for a pull, getting stuck on "Buffing", or spending cooldowns before the pack is gathered
- Walks back to its spot after you die, ignores your own party on a spot, and only uses spots saved in the zone you are in
- Pulls stay around the farm spot instead of drifting away and losing the enemies already pulled
- Waits for your HP to recover before the next pull (new setting, 80% by default)
- Removed the setting "Wait until combat fully ends"; it never did anything

### Fewer stalls
- Buffs, Treasure Sight, phantom job leveling, repairs and shopping no longer hold BOCCHI up forever; they give up after a while and carry on
- The jump when stuck gives up after five tries instead of jumping forever

### Settings
- Safer updates: a backup is made before your settings change, and a broken settings file no longer stops BOCCHI from loading
- Changes made with the chat command `/bocchi config set` are now kept after a restart
- Your chosen mount and combat plugin are no longer reset by themselves
- Removed the settings that hid tabs; every tab is always shown

### Other fixes
- Gold and silver per hour stay correct when you spend down to 0 or move a stack
- The pot timer only shows the zone you are in
- Shared chest spots and pot timers are more reliable and harder to fake
- FATE and CE names in your game language, shorter times (01:30 instead of 00:01:30), and clearer warnings
- Easier-to-read logs when you report a problem
