# 4.2.0.11

### Pot timer
- Next / active pot FATEs show North or South within the zone (Persistent Pots / Daylight Pottery = North; Pleading Pots / In a Pot of Bother = South)

### Mob Farmer
- Pot travel no longer sits idle after planning Return (Return runs even while Pots & Treasure is managing the pot window)
- Gather timeout now ends gathering and fights packs you already have, instead of only applying after you start stacking
- Tanks no longer re-cast Provoke / gap closer / ranged pull on mobs that already have you
- Treasure Sight casts on the first yield opportunity instead of waiting a full interval first

### Ninja Hide
- Crescent Haunts no longer trigger Hide (they see through it — walking stealthed into them was worse than running past)
- Far-northeast North Horn coffers (crowded ridge) no longer lateral-nudge into packs when stuck; with Hide on, those pads also start Hide farther out

### Treasure Hunt
- Stuck recovery no longer sideways-nudges while Hidden / near route threats (nudge was breaking Hide into aggro)

### Illegal Mode
- Combat targeting (RSR Henched) no longer flips On/Off every tick during FATEs and CEs
- Allowed FATEs (e.g. The Winged Terror) now stay saved across reloads / zone entry
- No longer mounts for the short walk from pot wait into a live pot FATE before combat

### Auto shopping
- Uses Knightshopper only (configure your Occult Crescent list there); the in-plugin shopping list and camp travel/buy path are removed
- Returns to base camp first when you are out in the field, then starts Knightshopper
- No longer starts while you are registered for / in a CE, waiting for a CE or pot, or mid pot-chest farm
- Emergency Stop also cancels Knightshopper shopping
- Dependencies tab shows Knightshopper under Shopping
- Debug: `/bocchi debug shop` Returns to camp if needed, then starts Knightshopper (`shop status` / `shop cancel`)

### Pot chests
- Pot chest farming can start while auto treasure hunt had Illegal Mode suspended (no longer waits until the hunt fully stops)

### Gear repair
- Mender NPC repair no longer stops early when the Repair All button is still disabled but gear still needs repair (walk-up-and-leave)
