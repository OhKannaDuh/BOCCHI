# 4.2.0.10

### Mob Farmer
- Pull buffs no longer swap through Dancer / Geomancer / Monk without casting (and no longer skip Ringing Respite after Battle Bell)

### Carrot Hunt
- Fortune Carrot no longer logs "used" and skips the pad when the game rejected the use (retries until cast / inventory drop)
- Auto shopping pauses Carrot Hunt and resumes where it left off (same as treasure hunt)

### Pot chests
- Ninja Hide arms earlier when walking to pot / 2nd-chance pads surrounded by high-Knowledge mobs (needs Use Ninja Hide on)

### Logs
- In-plugin log viewer (Config → Logs, main window list icon, or `/bocchi logs`)
- Debug lines are captured automatically (no need to set Dalamud log level to Debug)
- Copy all BOCCHI logs includes version, combat rotation, loaded plugins, active modes, Illegal Mode state, and zone
- Extra Debug for stuck triage: Illegal Mode state changes, Return/pot-farm enter, treasure hunt pause/idle reasons, combat AI enable/skip, shopping phases

### Repair
- Mender NPC repair no longer leaves Illegal Mode stuck on Repairing forever (timeout + short skip; falls back to self-repair if no mender nearby)

### Treasure / Hide
- Crescent Haunts no longer block Ninja Hide (they were wrongly treated as seeing through Hide)
