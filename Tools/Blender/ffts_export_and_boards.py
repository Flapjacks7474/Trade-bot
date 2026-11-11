# FIGHT FOR THE SOUTH - export the fight rig and render storyboard frames.
# Run in Blender after ffts_fight_rig.py (works on the open 10_fight_rig.blend).
#
# Exports (to Documents\FightForTheSouth_Art\export):
#   FFTS_FightRig.glb  - skeleton + mannequin + every action (browser build, glTF)
#   FFTS_FightRig.fbx  - same for Unity (import as Humanoid; clips keep their action names = AnimationKey)
# Storyboard frames (to ...\storyboard\frames\<NN_style>\):
#   per style: 00_intro (stance, 3/4 camera) then for every move three panels:
#   a = anticipation, b = impact (the frame the simulation resolves the hit), c = recovery,
#   rendered from the side "fight camera" with an opponent dummy, timing burned into a caption strip.
import bpy, json, math, os, urllib.request
from mathutils import Vector

RAW = os.environ.get("FFTS_RAW", "https://raw.githubusercontent.com/Flapjacks7474/Trade-bot/claude/fight-for-south-foundation-mgjm04/")
ROOT = os.path.join(os.path.expanduser("~"), "Documents", "FightForTheSouth_Art")
STYLE_FILES = ["street_fighting", "boxing", "kickboxing", "muay_thai", "karate", "taekwondo", "kung_fu", "wrestling",
               "judo", "mma", "hybrids"]
GAP = 1.05   # attacker-to-opponent spacing the clips are authored for (metres)

def styles():
    out = []
    for f in STYLE_FILES:
        with urllib.request.urlopen(RAW + "Assets/_Project/Data/Moves/%s.json" % f) as r:
            out += json.loads(r.read().decode("utf-8"))["Styles"]
    return out

def export(rig, man):
    exp = os.path.join(ROOT, "export"); os.makedirs(exp, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True); man.select_set(True); bpy.context.view_layer.objects.active = rig
    rig.animation_data.action = None
    bpy.ops.export_scene.gltf(filepath=os.path.join(exp, "FFTS_FightRig.glb"), export_format='GLB', use_selection=True,
                              export_animations=True, export_animation_mode='ACTIONS', export_force_sampling=True,
                              export_frame_step=1, export_optimize_animation_size=True, export_def_bones=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(exp, "FFTS_FightRig.fbx"), use_selection=True, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0,
                             armature_nodetype='NULL', primary_bone_axis='Y', secondary_bone_axis='X', apply_unit_scale=True)
    return [os.path.join(exp, f) for f in ("FFTS_FightRig.glb", "FFTS_FightRig.fbx")]

def opponent(rig, man):
    opp = bpy.data.objects.get("FFTS_Opponent")
    if opp: return opp
    opp = rig.copy(); opp.data = rig.data; opp.name = "FFTS_Opponent"; bpy.context.scene.collection.objects.link(opp)
    om = man.copy(); om.data = man.data.copy(); om.name = "FFTS_OpponentBody"; bpy.context.scene.collection.objects.link(om)
    om.parent = opp; om.modifiers["Armature"].object = opp
    # darker trunks so the two read apart in thumbnails
    mat = bpy.data.materials.get("FFTS_top").copy(); mat.name = "FFTS_opp_top"
    mat.diffuse_color = (0.55, 0.12, 0.10, 1)
    if mat.node_tree and mat.node_tree.nodes.get("Principled BSDF"): mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.12, 0.10, 1)
    for i, m in enumerate(om.data.materials):
        if m and m.name == "FFTS_top": om.data.materials[i] = mat
    opp.location = (0, -GAP, 0); opp.rotation_euler = (0, 0, math.pi)
    opp.animation_data_create(); opp.animation_data.action = bpy.data.actions["anim.street.stance_guard"]
    return opp

def aim(cam, loc, target):
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()

def set_opp_visible(opp, visible):
    opp.hide_render = not visible
    for ch in opp.children: ch.hide_render = not visible

def stage():
    sc = bpy.context.scene
    for n in ("FFTS_FightCam", "FFTS_IntroCam", "FFTS_Floor", "FFTS_Caption", "FFTS_Key", "FFTS_Fill"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    fight = bpy.data.objects.new("FFTS_FightCam", bpy.data.cameras.new("FFTS_FightCam")); sc.collection.objects.link(fight)
    aim(fight, (3.7, -1.55, 1.20), (0, -GAP / 2, 1.0)); fight.data.lens = 36
    intro = bpy.data.objects.new("FFTS_IntroCam", bpy.data.cameras.new("FFTS_IntroCam")); sc.collection.objects.link(intro)
    aim(intro, (1.25, -3.3, 1.25), (0, 0, 0.98)); intro.data.lens = 42
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0)); floor = bpy.context.active_object; floor.name = "FFTS_Floor"
    fm = bpy.data.materials.get("FFTS_floor") or bpy.data.materials.new("FFTS_floor"); fm.diffuse_color = (0.22, 0.22, 0.24, 1)
    floor.data.materials.append(fm)
    # caption: a text object parented to whichever camera renders
    cu = bpy.data.curves.new("FFTS_Caption", 'FONT'); cap = bpy.data.objects.new("FFTS_Caption", cu); sc.collection.objects.link(cap)
    cu.size = 0.032; cu.align_x = 'LEFT'
    cm = bpy.data.materials.get("FFTS_caption") or bpy.data.materials.new("FFTS_caption"); cm.diffuse_color = (1, 0.85, 0.3, 1)
    cu.materials.append(cm)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = True
    sc.render.resolution_x = 960; sc.render.resolution_y = 540; sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    return fight, intro, cap

def caption(cap, cam, lines):
    cap.data.body = "\n".join(lines)
    hw = 18.0 / cam.data.lens; hh = hw * 9 / 16          # half extents of the frame at 1 m (36 mm sensor, 16:9)
    sc = 0.065 * hh / 0.032
    cap.parent = cam; cap.location = (-hw * 0.95, hh * 0.86, -1.0); cap.rotation_euler = (0, 0, 0)
    cap.scale = (sc, sc, sc)

def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

def boards(rig, man, all_styles, only=None):
    fight, intro, cap = stage()
    opp = opponent(rig, man)
    sc = bpy.context.scene
    out_root = os.path.join(ROOT, "storyboard", "frames"); os.makedirs(out_root, exist_ok=True)
    index = []
    for si, st in enumerate(all_styles):
        prefix = st["Moves"][0]["Id"].split(".")[0]
        if only and prefix not in only: continue
        folder = os.path.join(out_root, "%02d_%s" % (si, prefix)); os.makedirs(folder, exist_ok=True)
        stn = st.get("Stance") or {}
        # intro panel
        rig.animation_data.action = bpy.data.actions["anim.%s.stance_idle" % prefix]; sc.frame_set(0)
        set_opp_visible(opp, False); sc.camera = intro
        caption(cap, intro, ["%s - %s" % (st["DisplayName"].upper(), stn.get("Name", "")),
                             "Defence: %s   %s" % (stn.get("Defense", ""), ("Hybrid of " + " + ".join(st["HybridOf"])) if st.get("HybridOf") else st.get("Discipline", "")),
                             "INTRO / STANCE  |  3/4 hero cam 45mm, slow push-in"])
        render(os.path.join(folder, "000_intro.png"))
        set_opp_visible(opp, True); sc.camera = fight
        for mi, m in enumerate(st["Moves"]):
            act = bpy.data.actions.get(m["AnimationKey"])
            if not act: continue
            rig.animation_data.action = act
            su, ac, rc = m["Startup"], m["Active"], m["Recovery"]
            if m["Kind"] == "ClinchStrike": su, ac, rc = 6, 2, 12
            total = su + ac + rc
            frames = {"a": max(1, int(su * 0.55)), "b": su, "c": min(total - 1, su + ac + max(1, int(rc * 0.4)))}
            for tag, f in frames.items():
                sc.frame_set(f)
                beat = {"a": "ANTICIPATION", "b": "IMPACT", "c": "RECOVERY"}[tag]
                caption(cap, fight, ["%s  |  %s (%s)" % (st["DisplayName"], m["DisplayName"], m["Kind"]),
                                     "%s  frame %d / %d   (startup %d, active %d, recovery %d @60fps)" % (beat, f, total, su, ac, rc),
                                     "dmg %s  range %.2fm%s  |  key %s" % (m["Damage"], m["Range"], "  KNOCKDOWN" if m.get("Knockdown") else "", m["AnimationKey"])])
                name = "%03d_%s_%s.png" % (mi + 1, m["Id"].split(".")[1], tag)
                render(os.path.join(folder, name))
                index.append({"style": st["Id"], "move": m["Id"], "panel": tag, "frame": f, "duration": {"a": frames["a"], "b": max(1, ac), "c": max(1, total - su - ac)}[tag],
                              "file": os.path.relpath(os.path.join(folder, name), ROOT).replace("\\", "/"), "key": m["AnimationKey"]})
    with open(os.path.join(ROOT, "storyboard", "frames_index.json"), "w") as fh: json.dump(index, fh, indent=1)
    rig.animation_data.action = None
    return len(index)

def main(do_export=True, do_boards=True, only=None):
    rig = bpy.data.objects["FFTS_Rig"]; man = bpy.data.objects["FFTS_Mannequin"]
    for o in bpy.data.objects:
        if o.name == "mesh_node": o.hide_set(True); o.hide_render = True
    msgs = []
    if do_export: msgs.append("exported %s" % export(rig, man))
    if do_boards: msgs.append("board frames %d" % boards(rig, man, styles(), only))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blend", "20_fight_boards.blend"))
    return "\n".join(msgs)
