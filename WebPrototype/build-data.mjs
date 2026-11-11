// Collects the shared JSON content from the Unity project so the browser build uses exactly the same data.
import { readFileSync, readdirSync } from 'node:fs';
const root = new URL('../Assets/_Project/Data/', import.meta.url);
const j = (p) => JSON.parse(readFileSync(new URL(p, root), 'utf8'));
export function gameData() {
  return {
    missions: j('Missions/chapter1.json').Missions,
    dialogues: j('Dialogue/chapter1_dialogue.json').Dialogues,
    rules: j('Dialogue/npc_conversations.json').Rules,
    // street_fighting first so the starter style keeps its place, then every other style and the hybrids.
    styles: ['street_fighting.json', ...readdirSync(new URL('Moves/', root)).filter((f) => f.endsWith('.json') && f !== 'street_fighting.json').sort()]
      .flatMap((f) => j('Moves/' + f).Styles),
    appearance: j('Appearance/appearance_catalog.json'),
    fighters: j('Characters/fighters.json').Fighters,
    npcs: j('Characters/npc_profiles.json').Npcs,
    layout: j('World/southside_layout.json'),
    vehicles: j('Vehicles/vehicles.json'),
    races: j('Racing/races.json').Races,
    districts: j('World/districts.json').Districts,
    crews: j('Crews/crews.json').Crews
  };
}
