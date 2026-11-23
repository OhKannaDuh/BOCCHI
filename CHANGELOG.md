# 4.3.0.0

### New
- New look. Prefer your own Dalamud colours? Turn off "Use BOCCHI theme" under Settings → UI
- The main window now has a tab per mode (Illegal Mode, Pots & Treasure, Mob Farmer, Treasure Hunter, World). Status, coffers left and gold / silver / XP per hour stay visible on top, and the window jumps to a mode's tab when it starts (and back to Illegal Mode when its treasure hunt pauses for a FATE or CE)
- Completionist is now a switch inside the Illegal Mode tab
- Search box in the settings, and the settings cog opens on the page of the tab you are on
- Each tab says in one line what the mode does, and warns you when a plugin it needs isn't running
- Triage Mode: optional wait before raising, so nearby healers get a chance first
- Hover texts rewritten in plainer words, in all four languages. Spotted odd wording in Japanese, Korean or Chinese? Let us know
- Auto revive (Illegal Mode → General → When you die): accept raises, and go back to base camp if nobody raises you in time. Both are off by default. Nothing happens inside Forked Tower; in FATEs and critical encounters raises are accepted, and the wait before returning to camp only starts once the FATE or encounter you died in is over, so you keep its reward. While you are dead, YesAlready is paused so it can't answer the prompt first
- New option (Movement → Routes): use Return for long trips instead of walking through mobs to an aethernet shard

### Fixes
- Illegal Mode now fights back when a mob your level or lower attacks while it waits to Return, instead of standing still
- The pot chest search no longer starts over (and uses another Magical Elixir) while Cache Me If You Can is still active after it finished
- If you die during Treasure Hunt or Carrot Hunt, it plans the route again once you are back up (aethernet included) instead of riding straight from base camp
- Treasure Hunt no longer hangs when an aethernet teleport or Return keeps failing; after three tries it carries on on foot
- Ninja Hide: a gearset number that is empty or not a Ninja set no longer freezes the hunt next to a strong enemy; it carries on without Hide
- With combat set to None, the pot chest search keeps moving when a mob attacks instead of standing still
- Mob Farmer gives up on a crystal buff trip after 5 minutes instead of staying paused
- Carrot Hunt no longer gets stuck on a carrot or bunny coffer it can't reach or open; it moves on to the next spot after 90 seconds
- Stopping Carrot Hunt while it is using Return or an aethernet shard now cancels that trip too
- Carrot Hunt no longer shuffles sideways when it carries on after a pause
- Mob Farmer no longer gets stuck on "Buffing" when a phantom job change doesn't go through; after 30 seconds it pulls anyway
- Shopping gives up after 10 minutes if the trip to camp or the purchase never finishes, so the rest of BOCCHI isn't left paused
- Treasure Hunt and Carrot Hunt now stop and fight an enemy they can't outrun (after 10 seconds), then carry on. Needs a combat rotation to be set
- Illegal Mode's treasure hunt does the same, and hands over at once when you die
- Mob Farmer no longer stands still forever waiting for a pull that never comes, and retries enemies it gave up on after a few minutes
- No more extra Return to camp right after Illegal Mode pauses its treasure hunt
- The Completionist list shows FATE and CE names in your game language
- Typing an unknown /bocchi option now tells you so
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
- Settings sliders no longer keep a typed value outside their range
- Time limits no longer fire early or late when the clock changes for daylight saving
- `/bocchi debug shop` now only shows the shopping status; add `start` to begin a run
- Errors now reach the log with their details, which makes reports easier to act on
