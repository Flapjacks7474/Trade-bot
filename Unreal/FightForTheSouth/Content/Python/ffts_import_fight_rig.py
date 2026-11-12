# FIGHT FOR THE SOUTH - Unreal editor setup (run with Tools > Execute Python Script, or `py <path>` in the console).
#
# 1. Imports <Project>/Import/FFTS_FightRig.fbx (from Blender, Tools/Blender/ffts_fight_rig.py) as a skeletal mesh
#    with all of its animation takes, sampled at 60 fps so frame N == simulation tick N.
# 2. Moves every clip to /Game/FFTS/Anim/<animation key with dots as underscores>, e.g. anim.boxing.jab -> anim_boxing_jab,
#    which is where AFFTSFighterCharacter looks them up.
# 3. Creates /Game/FFTS/Maps/Dojo (an empty level; the game mode builds the arena at runtime).
# Writes a report to <Project>/Saved/ffts_import_report.txt.
import os
import unreal

PROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir())
# The Blender export: <Project>/Import/FFTS_FightRig.fbx, or the art workspace's export folder when the project sits
# in Documents\FightForTheSouth_Art\Unreal\FightForTheSouth (the setup on the owner's PC).
FBX_CANDIDATES = [os.path.join(PROJECT, "Import", "FFTS_FightRig.fbx"),
                  os.path.normpath(os.path.join(PROJECT, "..", "..", "export", "FFTS_FightRig.fbx"))]
FBX = next((p for p in FBX_CANDIDATES if os.path.exists(p)), FBX_CANDIDATES[0])
CHAR_DIR = "/Game/FFTS/Characters"
ANIM_DIR = "/Game/FFTS/Anim"
MAP = "/Game/FFTS/Maps/Dojo"
report = []

def log(msg):
    report.append(msg)
    unreal.log("[FFTS] " + msg)

assets = unreal.EditorAssetLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()

def import_rig():
    if not os.path.exists(FBX):
        log("MISSING " + FBX)
        return False
    # Use the classic FBX importer so the options below apply (Interchange ignores FbxImportUI).
    unreal.SystemLibrary.execute_console_command(None, "Interchange.FeatureFlags.Import.FBX False")
    ui = unreal.FbxImportUI()
    ui.set_editor_property("import_mesh", True)
    ui.set_editor_property("import_as_skeletal", True)
    ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_SKELETAL_MESH)
    ui.set_editor_property("import_animations", True)
    ui.set_editor_property("import_materials", True)
    ui.set_editor_property("import_textures", False)
    ui.set_editor_property("create_physics_asset", True)
    ad = ui.get_editor_property("anim_sequence_import_data")
    ad.set_editor_property("animation_length", unreal.FBXAnimationLengthImportType.FBXALIT_EXPORTED_TIME)
    ad.set_editor_property("use_default_sample_rate", False)
    ad.set_editor_property("custom_sample_rate", 60)
    ad.set_editor_property("import_bone_tracks", True)
    ad.set_editor_property("remove_redundant_keys", False)
    sk = ui.get_editor_property("skeletal_mesh_import_data")
    sk.set_editor_property("import_morph_targets", False)
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", FBX)
    task.set_editor_property("destination_path", CHAR_DIR)
    task.set_editor_property("destination_name", "FFTS_FightRig")
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", ui)
    tools.import_asset_tasks([task])
    log("imported %d objects" % len(task.get_editor_property("imported_object_paths")))
    return True

def organise_clips():
    if not assets.does_directory_exist(ANIM_DIR):
        assets.make_directory(ANIM_DIR)
    moved = 0
    for path in assets.list_assets(CHAR_DIR, recursive=True, include_folder=False):
        data = assets.find_asset_data(path)
        if str(data.asset_class_path.asset_name) != "AnimSequence":
            continue
        name = str(data.asset_name)
        i = name.find("anim_")
        if i < 0:
            continue
        key = name[i:]
        target = "%s/%s" % (ANIM_DIR, key)
        if assets.does_asset_exist(target):
            assets.delete_asset(target)
        if assets.rename_asset(path.split(".")[0], target):
            moved += 1
    log("clips in %s: %d (moved %d)" % (ANIM_DIR, len(assets.list_assets(ANIM_DIR, recursive=False)), moved))
    # 60 fps check on one clip
    jab = unreal.load_asset(ANIM_DIR + "/anim_street_jab")
    if jab:
        log("anim_street_jab length %.4f s (expected 17 frames = 0.2833 s)" % jab.get_play_length())

def make_map():
    les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if assets.does_asset_exist(MAP):
        les.load_level(MAP)
        log("map exists: " + MAP)
        return
    if les.new_level(MAP):
        les.save_current_level()
        log("created map " + MAP)

def main():
    if import_rig():
        organise_clips()
    make_map()
    assets.save_directory("/Game/FFTS", only_if_is_dirty=True, recursive=True)
    out = os.path.join(PROJECT, "Saved", "ffts_import_report.txt")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w") as fh:
        fh.write("\n".join(report))

main()
