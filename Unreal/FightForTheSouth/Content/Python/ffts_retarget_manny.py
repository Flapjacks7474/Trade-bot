# FIGHT FOR THE SOUTH - put the fight clips on Unreal's Manny (run after ffts_import_fight_rig.py).
#
# 1. Copies the UE5 Mannequin character folder (SKM_Manny + materials + IK rig) from the Game Animation Sample
#    project on this PC into this project, keeping its /Game/... path so its internal references still resolve.
#    Animation folders are skipped (we only need the body).
# 2. Builds IK rigs for the Blender fight rig and for Manny, and an IK retargeter between them (auto chains).
# 3. Retargets every clip in /Game/FFTS/Anim onto Manny into /Game/FFTS/AnimManny with the same names, so the game
#    can switch bodies by folder (AFFTSFighterCharacter prefers Manny when /Game/FFTS/AnimManny exists).
# Writes <Project>/Saved/ffts_retarget_report.txt. Safe to re-run: existing assets are reused.
import os
import shutil
import traceback
import unreal

PROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir())
CONTENT = os.path.join(PROJECT, "Content")
DOCS = os.path.normpath(os.path.join(os.path.expanduser("~"), "Documents"))
GASP_ROOTS = [os.path.join(DOCS, "Unreal Projects", "GameAnimationSample", "GameAnimation"),
              os.path.join(DOCS, "Unreal Projects", "GameAnimationSample")]
RIG_MESH = "/Game/FFTS/Characters/FFTS_FightRig"
SRC_ANIM = "/Game/FFTS/Anim"
DST_ANIM = "/Game/FFTS/AnimManny"
RIG_DIR = "/Game/FFTS/Retarget"
SKIP_DIRS = {"animations", "anims", "animation", "motionmatching", "mocap"}
report = []

def log(msg):
    report.append(str(msg))
    unreal.log("[FFTS retarget] " + str(msg))

assets = unreal.EditorAssetLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()
registry = unreal.AssetRegistryHelpers.get_asset_registry()

def to_game_path(file_path):
    rel = os.path.relpath(file_path, CONTENT).replace("\\", "/")
    return "/Game/" + os.path.splitext(rel)[0]

def find_manny_in_project():
    if not assets.does_directory_exist("/Game/Characters"):
        return None
    found = {}
    for path in assets.list_assets("/Game/Characters", recursive=True, include_folder=False):
        data = assets.find_asset_data(path.split(".")[0])
        if str(data.asset_class_path.asset_name) == "SkeletalMesh":
            found[str(data.asset_name)] = path.split(".")[0]
    for name in ("SKM_Manny", "SKM_Manny_Simple"):
        if name in found:
            return found[name]
    return None

def copy_mannequin():
    found = find_manny_in_project()
    if found:
        log("Manny already in project: " + found)
        return found
    for root in GASP_ROOTS:
        content = os.path.join(root, "Content")
        if not os.path.isdir(content):
            continue
        hits = []
        for dirpath, dirnames, filenames in os.walk(content):
            for f in filenames:
                if f in ("SKM_Manny.uasset", "SKM_Manny_Simple.uasset"):
                    hits.append(os.path.join(dirpath, f))
        log("Game Animation Sample at %s, Manny files: %s" % (content, hits))
        if not hits:
            continue
        hit = sorted(hits, key=lambda h: ("Simple" in h, len(h)))[0]
        rel = os.path.relpath(hit, content).split(os.sep)
        # Copy the top character folder, e.g. Content/Characters/Mannequins (or UEFN_Mannequin)
        top = rel[:2] if rel[0].lower() == "characters" else rel[:1]
        src = os.path.join(content, *top)
        dst = os.path.join(CONTENT, *top)
        copied = 0
        for dirpath, dirnames, filenames in os.walk(src):
            dirnames[:] = [d for d in dirnames if d.lower() not in SKIP_DIRS]
            out = os.path.join(dst, os.path.relpath(dirpath, src))
            os.makedirs(out, exist_ok=True)
            for f in filenames:
                if f.endswith((".uasset", ".umap", ".ubulk", ".uexp")):
                    target = os.path.join(out, f)
                    if not os.path.exists(target):
                        shutil.copy2(os.path.join(dirpath, f), target)
                        copied += 1
        log("copied %d files %s -> %s" % (copied, src, dst))
        registry.scan_paths_synchronous(["/Game/" + "/".join(top)], True)
        return to_game_path(os.path.join(CONTENT, *rel))
    log("Game Animation Sample project with SKM_Manny not found under " + str(GASP_ROOTS))
    return None

def get_or_create(name, folder, cls, factory):
    path = folder + "/" + name
    if assets.does_asset_exist(path):
        return unreal.load_asset(path), False
    return tools.create_asset(name, folder, cls, factory), True

def make_ik_rig(name, mesh):
    rig, new = get_or_create(name, RIG_DIR, unreal.IKRigDefinition, unreal.IKRigDefinitionFactory())
    c = unreal.IKRigController.get_controller(rig)
    if new or not c.get_retarget_chains():
        c.set_skeletal_mesh(mesh)
        ok = c.apply_auto_generated_retarget_definition()
        try:
            c.apply_auto_fbik()
        except Exception as e:  # FBIK is only needed for IK goals; retargeting works without it
            log("auto FBIK skipped on %s: %s" % (name, e))
        log("%s: auto retarget definition %s, %d chains" % (name, ok, len(c.get_retarget_chains())))
    assets.save_asset(RIG_DIR + "/" + name)
    return rig

def _call(rc, name, *args):
    fn = getattr(rc, name, None)
    if fn is None:
        return "missing"
    try:
        return fn(*args)
    except Exception as e:
        return "error: %s" % e

def tame_translation_ops(rc):
    """The Blender rig stores bone translations in metres under a 100x armature scale, which the pelvis-motion, IK and
    root-motion ops turn into an 80 m pelvis offset. Keep rotations only: pelvis rotation, FK chains, curves."""
    for i in range(rc.get_num_retarget_ops()):
        name = str(rc.get_op_name(i))
        if name in ("Run IK Rig", "Root Motion"):
            log("disable op %s: %s" % (name, _call(rc, "set_retarget_op_enabled", i, False)))
        elif name == "Pelvis Motion":
            opc = _call(rc, "get_op_controller", i)
            if isinstance(opc, str) or opc is None:
                log("pelvis op controller: %s" % opc)
                continue
            log("pelvis op controller API: " + ", ".join(m for m in dir(opc) if not m.startswith("_")))
            settings = _call(opc, "get_settings")
            if isinstance(settings, str) or settings is None:
                log("pelvis settings: %s" % settings)
                continue
            done = False
            for prop in ("translation_alpha", "translation_alpha_x", "scale_vertical"):
                try:
                    settings.set_editor_property(prop, 0.0)
                    done = True
                    log("pelvis %s = 0" % prop)
                except Exception:
                    pass
            if done:
                log("set pelvis settings: %s" % _call(opc, "set_settings", settings))
            else:
                log("pelvis settings has no translation alpha; disabling op: %s" % _call(rc, "set_retarget_op_enabled", i, False))
    log("ops now: " + ", ".join("%s=%s" % (rc.get_op_name(i), rc.get_retarget_op_enabled(i)) for i in range(rc.get_num_retarget_ops())))

def make_retargeter(src_rig, tgt_rig, src_mesh, tgt_mesh):
    rtg, new = get_or_create("RTG_FightRig_to_Manny", RIG_DIR, unreal.IKRetargeter, unreal.IKRetargetFactory())
    rc = unreal.IKRetargeterController.get_controller(rtg)
    S, T = unreal.RetargetSourceOrTarget.SOURCE, unreal.RetargetSourceOrTarget.TARGET
    log("controller API: " + ", ".join(m for m in dir(rc) if any(k in m for k in ("op", "chain", "rig", "mesh", "align"))))
    rc.set_ik_rig(S, src_rig)
    rc.set_ik_rig(T, tgt_rig)
    for side, mesh in ((S, src_mesh), (T, tgt_mesh)):
        log("set_preview_mesh: %s" % _call(rc, "set_preview_mesh", side, mesh))
    # 5.6+: chain mapping lives in retarget ops; an asset made from Python starts with none.
    n_ops = _call(rc, "get_num_retarget_ops")
    log("retarget ops before: %s" % n_ops)
    if n_ops == 0 or isinstance(n_ops, str):
        log("add_default_ops: %s" % _call(rc, "add_default_ops"))
    log("retarget ops after: %s" % _call(rc, "get_num_retarget_ops"))
    log("assign_ik_rig_to_all_ops: %s" % _call(rc, "assign_ik_rig_to_all_ops", T, tgt_rig))
    log("auto_map_chains: %s" % _call(rc, "auto_map_chains", unreal.AutoMapChainType.FUZZY, True))
    # Same-named chains (both rigs use the auto-generated names) are mapped explicitly in case fuzzy mapping skipped them.
    src_names = {str(c.get_editor_property("chain_name")) for c in unreal.IKRigController.get_controller(src_rig).get_retarget_chains()}
    unmapped = []
    for c in unreal.IKRigController.get_controller(tgt_rig).get_retarget_chains():
        name = str(c.get_editor_property("chain_name"))
        cur = _call(rc, "get_source_chain", name)
        if str(cur) in ("None", "") or isinstance(cur, str) and cur.startswith("error"):
            if name in src_names:
                _call(rc, "set_source_chain", name, name)
            cur = _call(rc, "get_source_chain", name)
            if str(cur) in ("None", ""):
                unmapped.append(name)
    log("chains still unmapped on Manny: %s" % unmapped)
    for side in (T,):
        log("auto_align_all_bones: %s" % _call(rc, "auto_align_all_bones", side))
    tame_translation_ops(rc)
    assets.save_asset(RIG_DIR + "/RTG_FightRig_to_Manny")
    return rtg

REDO_CLIPS = False  # set True after changing the retargeter to rebuild /Game/FFTS/AnimManny

def retarget_clips(rtg, src_mesh, tgt_mesh):
    if REDO_CLIPS and assets.does_directory_exist(DST_ANIM):
        assets.delete_directory(DST_ANIM)
        log("deleted old " + DST_ANIM)
    if not assets.does_directory_exist(DST_ANIM):
        assets.make_directory(DST_ANIM)
    todo = []
    for path in assets.list_assets(SRC_ANIM, recursive=False, include_folder=False):
        name = path.split("/")[-1].split(".")[0]
        if not assets.does_asset_exist(DST_ANIM + "/" + name):
            todo.append(assets.find_asset_data(path.split(".")[0]))
    log("clips to retarget: %d" % len(todo))
    if not todo:
        return
    op = unreal.IKRetargetBatchOperation
    log("duplicate_and_retarget: " + (op.duplicate_and_retarget.__doc__ or "").split("\n")[0])
    try:  # 5.8: (..., search, replace, prefix, suffix, target_path, include_referenced_assets)
        made = op.duplicate_and_retarget(todo, src_mesh, tgt_mesh, rtg, search="", replace="", prefix="", suffix="_MannyRT",
                                         target_path=DST_ANIM, include_referenced_assets=False)
    except TypeError as e:
        log("retrying without target_path: %s" % e)
        made = op.duplicate_and_retarget(todo, src_mesh, tgt_mesh, rtg, search="", replace="", prefix="", suffix="_MannyRT",
                                         include_referenced_assets=False)
    moved = 0
    for folder in (SRC_ANIM, DST_ANIM):
        for path in assets.list_assets(folder, recursive=False, include_folder=False):
            name = path.split("/")[-1].split(".")[0]
            if name.endswith("_MannyRT"):
                if assets.rename_asset(path.split(".")[0], DST_ANIM + "/" + name[: -len("_MannyRT")]):
                    moved += 1
    log("retargeted %d, moved %d into %s" % (len(made) if made else 0, moved, DST_ANIM))
    assets.save_directory(DST_ANIM, only_if_is_dirty=True, recursive=True)

def main():
    try:
        manny_path = copy_mannequin()
        if not manny_path:
            return
        manny = unreal.load_asset(manny_path)
        rig_mesh = unreal.load_asset(RIG_MESH)
        log("Manny mesh: %s (%s), fight rig mesh: %s" % (manny_path, manny, rig_mesh))
        if not manny or not rig_mesh:
            return
        if not assets.does_directory_exist(RIG_DIR):
            assets.make_directory(RIG_DIR)
        src_rig = make_ik_rig("IK_FightRig", rig_mesh)
        tgt_rig = make_ik_rig("IK_FFTS_Manny", manny)
        rtg = make_retargeter(src_rig, tgt_rig, rig_mesh, manny)
        retarget_clips(rtg, rig_mesh, manny)
        jab = unreal.load_asset(DST_ANIM + "/anim_street_jab")
        if jab:
            log("Manny anim_street_jab length %.4f s (expected 0.2833)" % jab.get_play_length())
        log("MANNY_MESH=" + manny_path)
    except Exception:
        log(traceback.format_exc())
    finally:
        out = os.path.join(PROJECT, "Saved", "ffts_retarget_report.txt")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, "w") as fh:
            fh.write("\n".join(report))

main()
