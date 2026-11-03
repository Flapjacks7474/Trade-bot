# World Map — Finess City

Eight districts connected by roads, highways and bridges. District state (ownership, events) must persist across
transitions. Data: `Data/World/districts.json`. Only Southside has a layout so far.

| District | Control | Identity | Key landmarks |
|---|---|---|---|
| Southside *(vertical slice)* | The Slaughterhouse | Starting neighborhood | Grandma's house, Magnolia Courts, Cold Storage (underground venue), The Yard |
| Crown Center | Crown Syndicate | Downtown luxury & industry | Crown Tower |
| East End | Slime Circle | Industrial, independent music, racing | Slime House, East End Customs |
| Westside | 4PF Empire | Older neighborhoods, small business | The Blueprint |
| North Heights | Tru Alliance (+Crown influence) | Luxury homes, private parties | The Estate |
| The Strip | Contested | Nightlife corridor | The Overlook |
| Old Town | Tru Alliance (commercial) | Historic, hidden businesses | The Backroom |
| Outskirts / County Line | Independent / contested | Rural, raceways, secrets | County Line Motel, County Line Raceway |

Secret locations: The Basement (hidden tournament), Studio 404 (underground studio), The Overlook (rooftop events),
County Line Motel (hidden missions), The Backroom (Southern Table meetings).

## Southside slice layout (400 m × 400 m, `southside_layout.json`)
Roads: Southside Blvd (E–W, z=0), Magnolia Ave (z=20), Pecan St (N–S, x=0), Industrial Rd (x=100), West St (x=−80).

| Location | Position (x, z) | Type |
|---|---|---|
| Grandma's House | (−60, 40) | residence (spawn just south) |
| Pecan Court Apartments | (−110, 40) | residence |
| Corner Store | (−20, −25) | shop |
| Lou's Barbershop | (15, −25) | shop |
| Southside Threads | (45, −25) | clothing |
| Tone's Studio | (80, 40) | studio |
| Mama Dot's Kitchen | (45, 40) | restaurant |
| Southside Community Gym | (−60, −70) | gym |
| Magnolia Courts | (−100, −30) | court |
| Cold Storage | (110, −80) | underground venue |
| Southside Motors | (130, 40) | garage / used cars |
| The Yard | (150, −150) | Slaughterhouse HQ |
| Pecan Street Lot | (−150, −120) | race start |

Streaming plan (M11): one cell per 200 m square, a district-level manifest, Addressable cell scenes, and three NPC
simulation tiers (full within 60 m, simplified to 250 m, statistical beyond).
