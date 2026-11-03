# Dialogue Bible

## Principles
- Authored lines with contextual **variants** (flags, relationship thresholds, attitude). The core campaign never
  depends on live AI-generated dialogue.
- Options carry a **tone**: Respectful, Joke, Flirt, Negotiate, Challenge, Apologize, Lie, Truth, Threaten, WalkAway.
  The tone is shown to the player.
- Choices have consequences (relationship deltas, flags, mission branches, fights) and apply **once** per conversation option.
- Characters only reference what they could know (witnessed or heard; see LivingWorldSpecification.md).
- No copyrighted lyrics. Slang should feel regional without parody. Avoid real-world crimes attributed to real people.

## Voice notes
| Character | Voice |
|---|---|
| Grandma Ruth | Short sentences, warmth that deflects; calls him "baby"; never lies outright, redirects |
| Dre | Loud, jokey, competitive; masks insecurity with bravado |
| Rell | Clipped, controlled, threatening without volume |
| Tone | Dry and economical; respects work, not talk |
| Big Reece | Gruff; frames everything as a test |
| Nia | Fast, transactional, persuasive |
| Lou | Storyteller; drops heavy hints, then pretends he didn't |

## Implemented conversations
`dialogue.grandma.welcome` (photo clue), `dialogue.grandma.home`, `dialogue.grandma.invitation` (variant if the errand
was done), `dialogue.lou.barbershop` (five crews, "somebody above all five"), `dialogue.dre.reunion`,
`dialogue.rell.confrontation` (talk / stand / fight), `dialogue.tone.studio` (gated on Rell), `dialogue.reece.venue`
(variant on the talk branch), `dialogue.nia.invitation` (negotiate path), `dialogue.marlo.intro`.

Which conversation plays is decided by `Data/Dialogue/npc_conversations.json` (first matching rule; can also start a fight or show a one-line bark).
