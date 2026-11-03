# Story Bible — CONFIDENTIAL (contains the Chapter 7 reveal)

> Writers and QA only. Nothing in this file may surface in-game before its chapter. The reveal flag
> `story.chairman.identity_revealed` is code-guarded (TD-015).

## Premise
Finess City is split between five crews. Above them sits **the Southern Table**, a quiet council that settles
disputes, assigns territory and takes a share of everything. It is run by an unseen figure the crew leaders call
**the Chairman**. The leaders know the Table exists; none knows who the Chairman is.

The protagonist comes home to Southside to live with **Grandma Ruth Hayes**. He is unknown, broke, and
free to become anyone.

## The family (truth)
- **Protagonist** — surname Hayes (first name and stage name player-chosen). He was 8 when the family broke apart.
- **Marcus Avery Hayes** — older brother, 8 years older. At 16 he disappeared during the **Pecan Street crisis**: the family
  home was going to auction, their father had been arrested, and a debt to the wrong people was coming due. Marcus
  made a deal that paid the debt and saved Grandma's new house, and the price was leaving and never contacting them.
  He rose by being useful to every crew at once. He became the broker they all needed, then built the Table so no
  crew could become strong enough to come after his family again. He is **the Chairman**.
- **Grandma Ruth** knows Marcus is alive. For years an entity called **Avery Holdings** has paid her property taxes,
  and she has guessed who it is. She does not know what he became.

## Marcus's motivation (keep him human)
He believes the Table is a wall around his family: as long as no crew can win outright, no one can touch Southside's
porches again. He is right that it has kept a kind of peace. He is wrong that it is safe. The Table has made him the
most valuable target in the city, and his brother is now walking into it. He watches the protagonist with pride and
dread, and intervenes indirectly: Nia's invitation and Solo's disappearance are both his doing.

## Solomon "Solo" King
Fictional music executive, Crown Center. The only industry figure who knew Marcus before the Table. At the Chapter 2
showcase Solo recognizes the protagonist's face and starts talking. Marcus has him removed from the city (not killed):
he is hidden at the **County Line Motel** (Chapter 6, *Solo's Secret*). Witnesses give conflicting accounts by design;
each witness's account depends on what they actually saw (memory system).

## The photograph (central object)
Two boys on a Pecan Street porch, summer 2012. The bigger boy's face is worn away by a fold and water damage. The back
says **"M.A.H. & baby — summer '12"** in Grandma's handwriting.

Plausible readings before the reveal: a cousin; the protagonist's father as a teen (wrong year, but players won't
know); Grandma's late son; a neighbor ("Mah" as a nickname). Placement of clues:

| Chapter | Clue | Misdirection |
|---|---|---|
| 1 | Photo on the mantel; Grandma: "That's family. Old family." | Feels like the father |
| 1 | Lou: "somebody sits above all five" | Seeds the Table, not the brother |
| 1 | Grandma's pause at "Crown Tower" | Points at the Crown Syndicate (red herring) |
| 2 | Solo stares at the protagonist and says "You got the Hayes jaw"; Solo vanishes | Suggests the father's past |
| 4 | *The Chairman*: a voice-only call that knows Grandma's street name | Could be any leader |
| 5 | *The Missing Records*: properties owned by **Avery Holdings** (incl. Grandma's taxes) | "Avery" reads as a company |
| 6 | *The Old Photograph*: restoration reveals a scar on the bigger boy's hand | Matched to a different character in a red-herring branch |
| 6 | *Somebody Knows You*: the Chairman's message uses the protagonist's childhood nickname | — |
| 7 | *What Grandma Knows*: Grandma tells the Pecan Street story; "M.A.H." = Marcus Avery Hayes | Reveal |

## Chapter beats
1. **Southside Raised** — home, Dre, first fight, first demo, Rell, Brick, Nia's showcase invitation. *(implemented)*
2. **The Introduction** — the showcase at Crown Tower, the five leaders, the incident, Solo's disappearance, conflicting witnesses, first crew offer.
3. **Earn Your Name** — join, stay independent or work several crews; underground tournament; racing ladder.
4. **Blood Isn't Always Thicker** — family business, betrayal (Dre's arc can break here), the leak, the first Chairman contact.
5. **The City Divided** — territory war; the Missing Records; the five leaders unite against the protagonist.
6. **The Southern Table** — the organisation is exposed (identity stays hidden); Solo found; the photo restored; "Somebody Knows You".
7. **The Bloodline** — Grandma's story, the last day of Pecan Street, the man behind the Table, the reunion.
8. **Nobody Owns the South** — final confrontation and decision.

## Endings (state-driven)
| Ending | Requires (indicative) |
|---|---|
| Reconcile with the brother | Grandma trust high; protagonist spared Solo; brother's trust ≥ threshold; no betrayal of family flags |
| Take over the organization | Influence/Fear high; ≥3 crews' support; chose power branches in Ch5–6 |
| Choose family over power | Walk away from the Table offer; Grandma affection high; Dre reconciled |
| Refuse forgiveness and fight | Resentment toward the brother high, or Grandma hurt in Ch5 |
| Build a new dynasty | Own businesses + label + crew of original characters; independent path |
| Dismantle the Southern Table | Evidence flags from Missing Records + Solo's testimony + leaders turned |

Every ending is computed from persisted flags and relationship values, never from a single last-scene choice.
