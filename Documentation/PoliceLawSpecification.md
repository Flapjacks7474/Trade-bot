# Police & Law Specification — status: NOT IMPLEMENTED

Fictional gameplay states, not a real-world law simulator. Response is based on **severity, witnesses, recognition
and evidence**, never on speed alone.

**Tiers:** 0 Clear · 1 Attention · 2 Active · 3 Serious · 4 Major · 5 Critical.

**IncidentRecord:** id, time, location, severity, involved, witnesses, evidence (with reliability), player recognition,
report status, investigation status, resolution.

**Reuse:** witnesses come from the same `SocialManager.Record` mechanism used by fights, so NPCs can't report what they
didn't see. Rumors carry their source. Fines go through `FinessBank` with incident-keyed idempotency (no duplicate fines).

**Modules:** PoliceSystem, IncidentManager, WantedLevelManager, LawResponseDirector, WitnessManager (extends
SocialManager), RecognitionManager, InvestigationManager, ArrestSequence, FineManager, ImpoundManager, LegalEventManager.

Street fights already record witnesses and apply Controversy/Credibility changes; they will feed incidents.
