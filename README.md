# BOCCHI

**Better Occult Crescent & Chest Helper Interface**

A Dalamud plugin for Occult Crescent (South Horn and North Horn): see what's up, get there, open coffers, or let it run the zone for you.

Repo install URL:

```text
https://raw.githubusercontent.com/OhKannaDuh/plugins/refs/heads/master/manifest.json
```

<a href='https://ko-fi.com/kagekazu' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi5.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

---

## Modes

Each mode has its own tab in the main window. Only one runs at a time.

- **Illegal Mode** - full autopilot: picks FATEs and Critical Encounters, travels, fights, waits for Magic Pots and farms pot chests. Can hunt treasure in between, raise players afterwards (Triage Mode) and revive you when you die (off by default). Completionist is a switch here.
- **Pots & Treasure** - waits for Magic Pots and hunts treasure in between, without the rest of Illegal Mode.
- **Treasure Hunter** - bronze and silver coffer routes, and Carrot Hunt for Fortune Carrots.
- **Mob Farmer** - pulls and clears packs at farm spots you save.
- **World** - live FATE and CE lists; walk or teleport to one, or flag it on the map.

Also: knowledge crystal buffs, gold / silver / XP per hour, a pot timer, Forked Tower help, and shopping through Knightshopper.

**Shared maps** (Treasure Hunter → "Share chest locations", on by default): BOCCHI shares coffer, carrot and pot chest spots and pot timers with other players. Only the spot is sent, nothing about you or your character.

## Requirements

| Plugin | For |
|---|---|
| **vnavmesh** | Walking (required) |
| **Lifestream** | Aethernet teleports (required) |
| **BossMod** | Targeting and movement in fights |
| **Wrath Combo** | Your job rotation (or use BossMod's own) |
| **Knightshopper** | Shopping (optional) |

## Quick start

1. Install from the repo URL above.
2. Go to South or North Horn and type `/bocchi`.
3. Pick a tab and press Start. The cog in the title bar opens the settings for that tab.

## Commands

| Command | What it does |
|---|---|
| `/bocchi` | Open the window (also `/och`) |
| `/bocchi config` | Open the settings; `config set <setting> <value>` changes one from chat |
| `/bocchi illegal on\|off\|toggle` | Control Illegal Mode |
| `/bocchi tp fate\|ce\|pot` | Teleport toward the nearest one |
| `/bocchi buff` | Start or stop a buff run |
| `/bocchi logs` | Open the logs (`logs copy` copies them for a bug report) |
| `/bocchi changelog` | Show what's new |

Languages: English, Japanese, Korean, Simplified Chinese.
