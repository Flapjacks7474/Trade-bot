"""Generates ffts_build_fight_boards.js for Storyboard Pro (24+/27) from the game's move data.

The script it writes builds the whole fight-choreography board: one sequence per style, an intro scene per style,
one scene per move with three panels (anticipation / impact / recovery) using the Blender-rendered frames, panel
durations from the move's frame data, and captions for action, camera and timing. Run:
    python3 Tools/StoryboardPro/generate_sbp_script.py <art root on the PC> > ffts_build_fight_boards.js
"""
import json, os, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "C:/Users/Administrator/Documents/FightForTheSouth_Art"
DATA = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Data", "Moves")
FILES = ["street_fighting", "boxing", "kickboxing", "muay_thai", "karate", "taekwondo", "kung_fu", "wrestling", "judo", "mma", "hybrids"]
HITSTOP = {"Light": 2, "Heavy": 4, "Kick": 4, "Counter": 3, "Signature": 6, "Finisher": 10, "Throw": 4, "EnvironmentalThrow": 6,
           "ClinchStrike": 3, "GroundAttack": 3}
CAMERA = {
    "Light": "Side fight cam 36mm at chest height, both fighters full figure. 2-tick hit-stop, small shake. No cut.",
    "Heavy": "Side fight cam. 4-tick hit-stop, medium shake, 5% push-in through the impact and ease back over recovery.",
    "Kick": "Side fight cam, frame widened 10% so the kicking leg stays in frame. 4-tick hit-stop, medium shake.",
    "Signature": "Snap to low 3/4 angle 28mm on the impact frame, 6-tick hit-stop, heavy shake; knockdown gets a dust burst. Cut back to side cam in recovery.",
    "Finisher": "CINEMATIC: letterbox in over startup while the camera orbits 90 degrees off the side cam to a low hero angle. Impact plays at 0.25x for 18 ticks, then cut to wide side cam for the knockdown.",
    "Counter": "1-frame white flash on the catch, 10-degree whip pan toward the counter-hitter, 3-tick hit-stop.",
    "Grapple": "Pull out to 30mm to hold both bodies; track the clinch centre, not either fighter.",
    "ClinchStrike": "Tight two-shot 40mm on the clinch; small shake per knee or strike.",
    "Throw": "Pull out to 30mm and follow the throw arc; floor impact gets dust and a 4-tick hit-stop. Victim plays react.thrown.",
    "EnvironmentalThrow": "Cut to a wall-side angle 28mm so the wall is in frame; 6-tick hit-stop on the wall hit, debris decal.",
    "GroundAttack": "Drop the camera to 0.6 m height, aim down at the downed fighter.",
    "Taunt": "Medium close-up 50mm on the taunting fighter; opponent soft in the background. Crowd reaction cue.",
}

def esc(s):
    return s.replace("\\", "\\\\").replace("'", "\\'")

def main():
    styles = []
    for f in FILES:
        styles += json.load(open(os.path.join(DATA, f + ".json")))["Styles"]
    scenes = []
    for si, st in enumerate(styles):
        prefix = st["Moves"][0]["Id"].split(".")[0]
        folder = "%s/storyboard/frames/%02d_%s" % (ROOT, si, prefix)
        stn = st.get("Stance") or {}
        hybrid = (" Hybrid of " + " + ".join(st["HybridOf"]) + ".") if st.get("HybridOf") else ""
        links = st.get("Links", [])
        intro = {
            "seq": "%02d %s" % (si, st["DisplayName"]), "name": "%s_00_intro" % prefix.upper()[:10],
            "panels": [{"img": folder + "/000_intro.png", "ticks": 120,
                        "action": "%s - %s.%s %s Defence: %s. Strengths: %s Weaknesses: %s" % (st["DisplayName"], stn.get("Name", ""), hybrid,
                                   stn.get("Description", ""), stn.get("Defense", ""), st.get("Strengths", ""), st.get("Weaknesses", "")),
                        "camera": "STYLE INTRO: 3/4 hero cam 42mm, slow 2-second push-in on the stance loop (anim.%s.stance_idle). Title card '%s' lower-left. ENTRANCE: fighter walks in from screen-left, plays the style taunt, settles into stance on the last beat." % (prefix, st["DisplayName"].upper()),
                        "timing": "120 ticks (2 s) stance loop. Clips: anim.%s.stance_idle, stance_guard, shuffle_forward, shuffle_back." % prefix}],
        }
        scenes.append(intro)
        for mi, m in enumerate(st["Moves"]):
            su, ac, rc = m["Startup"], m["Active"], m["Recovery"]
            if m["Kind"] == "ClinchStrike": su, ac, rc = 6, 2, 12
            into = ["%s on %s" % (l["To"].split(".")[1], l["Command"]) for l in links if l["From"] == m["Id"]]
            frm = ["%s (%s)" % (l["From"].split(".")[1], l["Command"]) for l in links if l["To"] == m["Id"]]
            combo = (" Combos into: " + ", ".join(into) + ".") if into else ""
            combo += (" Reached from: " + ", ".join(frm) + ".") if frm else ""
            base = "%s %s (%s). Damage %s, range %.2f m%s.%s" % (st["DisplayName"], m["DisplayName"], m["Kind"], m["Damage"], m["Range"],
                                                                  ", KNOCKDOWN" if m.get("Knockdown") else "", combo)
            cam = CAMERA.get(m["Kind"], CAMERA["Light"])
            hs = HITSTOP.get(m["Kind"], 0)
            name = "%s_%02d_%s" % (prefix.upper()[:10], mi + 1, m["Id"].split(".")[1])
            scenes.append({"seq": intro["seq"], "name": name[:40], "panels": [
                {"img": "%s/%03d_%s_a.png" % (folder, mi + 1, m["Id"].split(".")[1]), "ticks": su,
                 "action": "ANTICIPATION. " + base + " Attacker (white) screen-right loads the strike; opponent (red) in guard.",
                 "camera": cam, "timing": "Startup %d ticks: frames 0-%d. Anticipation key at ~55%% of startup." % (su, su - 1)},
                {"img": "%s/%03d_%s_b.png" % (folder, mi + 1, m["Id"].split(".")[1]), "ticks": max(1, ac) + hs,
                 "action": "IMPACT on game frame %d (the tick the simulation resolves the hit). Opponent plays %s." % (
                     su, "react.knockdown" if m.get("Knockdown") else ("react.thrown" if "Throw" in m["Kind"] else "react.hit_head_light / heavy by damage")),
                 "camera": cam, "timing": "Active %d ticks + %d-tick hit-stop. Clip key %s." % (ac, hs, m["AnimationKey"])},
                {"img": "%s/%03d_%s_c.png" % (folder, mi + 1, m["Id"].split(".")[1]), "ticks": rc,
                 "action": "RECOVERY back to stance.%s" % ((" Cancel window: first %d ticks of recovery." % m["CancelWindow"]) if m.get("CancelWindow") else " No cancel: fully committed."),
                 "camera": "Return to neutral side cam framing.", "timing": "Recovery %d ticks, total clip %d ticks." % (rc, su + ac + rc)}]})
    out = []
    out.append("// GENERATED by Tools/StoryboardPro/generate_sbp_script.py - do not edit by hand.")
    out.append("// Storyboard Pro: Windows > Script Editor, open this file, run FFTS_BuildFightBoards.")
    out.append("var FFTS_SCENES = [")
    for sc in scenes:
        ps = ",".join("{img:'%s',ticks:%d,action:'%s',camera:'%s',timing:'%s'}" % (esc(p["img"]), p["ticks"], esc(p["action"]), esc(p["camera"]), esc(p["timing"])) for p in sc["panels"])
        out.append("  {seq:'%s',name:'%s',panels:[%s]}," % (esc(sc["seq"]), esc(sc["name"]), ps))
    out.append("];")
    out.append(r'''
function FFTS_fps() {
  try { var f = scene.getFrameRate(); if (f > 0) return f; } catch (e) {}
  return 30;
}
function FFTS_ensureCaption(cm, name) {
  for (var i = 0; i < cm.numberOfPanelCaptions(); i++) if (cm.nameOfPanelCaption(i) == name) return;
  cm.addPanelCaption(name);
}
function FFTS_BuildFightBoards() {
  var sb = new StoryboardManager(), lm = new LayerManager(), cm = new CaptionManager();
  var fps = FFTS_fps();
  FFTS_ensureCaption(cm, "Action Notes"); FFTS_ensureCaption(cm, "Camera"); FFTS_ensureCaption(cm, "Timing");
  var firstExisting = sb.numberOfPanelsInProject() > 0 ? sb.panelInProject(0) : null;
  var log = [], seqFirst = {}, seqLast = {}, order = [];
  for (var s = 0; s < FFTS_SCENES.length; s++) {
    var sc = FFTS_SCENES[s];
    var sceneId = sb.appendScene(sc.name);
    if (!sceneId) { log.push("scene failed " + sc.name); continue; }
    if (!(sc.seq in seqFirst)) { seqFirst[sc.seq] = sceneId; order.push(sc.seq); }
    seqLast[sc.seq] = sceneId;
    var prev = sb.numberOfPanelsInScene(sceneId) > 0 ? sb.panelInScene(sceneId, 0) : null;
    for (var p = 0; p < sc.panels.length; p++) {
      var pd = sc.panels[p];
      var panelId = (p == 0 && prev) ? prev : sb.insertPanel(true, prev, sc.name + "_" + "abc".charAt(p));
      if (!panelId) { log.push("panel failed " + sc.name); continue; }
      prev = panelId;
      if (!lm.importImageAsLayer(panelId, pd.img, "Composite")) log.push("image failed " + pd.img);
      sb.setPanelDuration(panelId, Math.max(2, Math.round(pd.ticks * fps / 60)));
      cm.setPanelCaptionText("Action Notes", panelId, pd.action);
      cm.setPanelCaptionText("Camera", panelId, pd.camera);
      cm.setPanelCaptionText("Timing", panelId, pd.timing + " (60 fps game ticks, shown at " + fps + " fps)");
    }
  }
  for (var i = 0; i < order.length; i++) {
    var seqId = sb.createSequence(seqFirst[order[i]], seqLast[order[i]]);
    if (seqId) sb.renameSequence(seqId, order[i]);
    if (i > 0) { try { sb.createTrx(seqFirst[order[i]], Math.round(fps / 2), "Dissolve"); } catch (e) { log.push("trx " + e); } }
  }
  if (firstExisting && sb.numberOfPanelsInProject() > 1) {
    var empty = sb.sceneIdOfPanel(firstExisting);
    if (sb.numberOfPanelsInScene(empty) == 1 && lmEmpty(lm, firstExisting)) sb.deleteScene(empty);
  }
  MessageLog.trace("FFTS boards: " + FFTS_SCENES.length + " scenes, issues: " + log.length + "\n" + log.join("\n"));
  MessageBox.information("FFTS fight boards built: " + FFTS_SCENES.length + " scenes in " + order.length + " sequences. Issues: " + log.length);
}
function lmEmpty(lm, panelId) {
  for (var i = 0; i < lm.numberOfLayers(panelId); i++) if (!lm.isEmpty(panelId, i)) return false;
  return true;
}
''')
    print("\n".join(out))

if __name__ == "__main__":
    main()
