# 4.2.0.11

### Pot timer
- Upcoming and active pot FATEs show North or South in the zone (Persistent Pots / Daylight Pottery = North; Pleading Pots / In a Pot of Bother = South)

### Mob Farmer
- No longer idles after deciding to Return during pot travel — Return actually runs
- Gather timeout now stops gathering and fights what you already pulled (not only after stacking starts)
- Tanks stop re-using Provoke / gap closer / ranged pull on mobs that already have you
- Treasure Sight can cast on the first chance instead of waiting a full timer first

### Ninja Hide
- Crescent Haunts no longer trigger Hide (they see through it — walking stealthed into them was worse)
- Crowded far-northeast North Horn coffers: stuck recovery no longer sidesteps into packs; with Hide on, Hide also starts farther out on those pads

### Treasure Hunt
- Stuck recovery no longer sidesteps while you are Hidden or next to route threats (that was dropping Hide into aggro)

### Illegal Mode
- Combat targeting no longer flickers On/Off every tick in FATEs and CEs
- Allowed FATEs (e.g. The Winged Terror) stay saved after reload / zone entry
- No longer mounts for the short walk from pot wait into a live pot FATE

### Auto shopping
- Shopping is Knightshopper only — set your Occult Crescent list there (the old in-plugin list is gone)
- If you are out in the field, BOCCHI Returns to base camp first, then starts Knightshopper
- Will not start during a FATE/CE, while waiting for a CE or pot, or mid pot-chest farm
- Emergency Stop also cancels Knightshopper
- Dependencies lists Knightshopper under Shopping

### Pot chests
- Pot chest farming can start even if treasure hunt had paused Illegal Mode (no longer waits for the hunt to fully stop)

### Gear repair
- Mender repair no longer walks up and leaves while gear still needs Repair All
