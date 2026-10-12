# 4.2.0.18

### Mob Farmer
- Optional: Prioritize pots during timed hunt — runs the timer as Pots & Treasure so a Magic Pot still interrupts the route

### Illegal Mode
- Pause auto treasure hunt for CEs waits until you are out of combat before pausing (no soft-stop mid-pull)

### Fixes
- Pots & Treasure no longer thrash when a live pot is past the Skip FATE progress % (still finish the pot)
- Treasure Hunt keeps the wind-safe approach to Suspended Masonry coffer 2061 (no longer skips the via when the chest looks closer)
- Auto shopping aborts and cools down when Knightshopper denies start (IPC) or is busy, instead of retrying every tick
- Pausing Treasure Hunt restores your previous gearset after Ninja Hide (CE/FATE/pot yields)
- Pot chest farm enables combat AI if you take aggro mid-open, and only opens pot-spot reveals (not nearby map coffers)

### Forked Tower
- Illegal Mode can optionally walk to the tower pad when registration opens (Config → Forked Tower)
- Turns itself off once you are inside
- Registration countdown now shows for North Horn too
