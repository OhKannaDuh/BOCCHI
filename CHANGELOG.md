# 4.2.0.20

### New
- New look
- The main window now has a tab per mode (Illegal Mode, Pots & Treasure, Mob Farmer, Treasure Hunter, World). Status, coffers left and gold / silver / XP per hour stay visible on top, and the window jumps to a mode's tab when it starts
- Completionist is now a switch inside the Illegal Mode tab
- Search box in the settings, and a cog on each tab that opens that tab's settings
- Each tab says in one line what the mode does, and warns you when a plugin it needs isn't running
- Triage Mode: optional wait before raising, so nearby healers get a chance first
- Hover texts rewritten in plainer words, in all four languages. Spotted odd wording in Japanese, Korean or Chinese? Let us know
- Auto revive (Illegal Mode → General → When you die): accept raises, and go back to base camp if nobody raises you in time. Both are off by default and never trigger inside Forked Tower or a critical encounter. While you are dead, YesAlready is paused so it can't answer the prompt first
- New option (Movement → Routes): use Return for long trips instead of walking through mobs to an aethernet shard

### Fixes
- If a mob attacks you while you're mounted, you now get off and fight it when it's your level or lower, and keep riding away from stronger ones (one that keeps chasing you gets fought)
- A mob that follows you to a pot or CE waiting spot gets fought instead of killing you while you wait
- The second pot chest search no longer goes back to the first pot's spots
- No more mounting up again just to reach a pot FATE from its waiting spot
- Pot settings now work with "Do FATEs" turned off, so Illegal Mode goes to pots instead of waiting at a CE
- Mob Farmer no longer spends cooldowns and phantom actions before the pack is gathered, and stands still while it pulls so the pull isn't cancelled
- Mob Farmer no longer waits ages between pulls when fewer enemies than your minimum are nearby
- With combat set to None, BOCCHI fully lets go of Rotation Solver
- Clearer warning when Rotation Solver or Wrath runs without BossMod: nothing picks targets for you then
- The treasure hunt skips the Wanderer's Haven coffer that kept getting stuck on its ledge
- Shared chest spots can no longer be faked, and shared pot timers are more reliable
