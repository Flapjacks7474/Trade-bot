# FIGHT FOR THE SOUTH - one-shot rebuild of the fight assets at real scale:
# 1. runs Blender headless on blend/10_fight_rig.blend with tools/ffts_export_unreal.py -> export/FFTS_FightRig_UE.fbx
# 2. deletes /Game/FFTS/Characters, Anim, Retarget, AnimManny (built from the old metre-scaled export)
# 3. runs ffts_import_fight_rig.py, then ffts_retarget_manny.py (which now keeps pelvis height, IK and root motion)
# Writes <Project>/Saved/ffts_rebuild_report.txt (plus the two scripts' own reports).
import glob
import os
import subprocess
import time
import traceback
import unreal

PROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir())
ART = os.path.normpath(os.path.join(PROJECT, "..", ".."))
BLEND = os.path.join(ART, "blend", "10_fight_rig.blend")
EXPORT_SCRIPT = os.path.join(ART, "tools", "ffts_export_unreal.py")
FBX = os.path.join(ART, "export", "FFTS_FightRig_UE.fbx")
HERE = os.path.join(PROJECT, "Content", "Python")
report = []

def log(msg):
    report.append(str(msg))
    unreal.log("[FFTS rebuild] " + str(msg))

def blender_exe():
    pats = [r"C:\Program Files\Blender Foundation\*\blender.exe",
            os.path.join(os.path.expanduser("~"), r"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\*\blender.exe"),
            r"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe"]
    hits = []
    for p in pats:
        hits += glob.glob(p)
    return sorted(hits)[-1] if hits else None

def run_blender():
    exe = blender_exe()
    log("blender: %s" % exe)
    if not exe:
        return False
    before = os.path.getmtime(FBX) if os.path.exists(FBX) else 0
    t0 = time.time()
    r = subprocess.run([exe, "-b", BLEND, "--factory-startup", "--python", EXPORT_SCRIPT],
                       capture_output=True, text=True, timeout=1800)
    tail = [l for l in (r.stdout + r.stderr).splitlines() if "FFTS_" in l or "Error" in l or "Traceback" in l]
    log("blender exit %s in %.0fs: %s" % (r.returncode, time.time() - t0, tail[-8:]))
    ok = os.path.exists(FBX) and os.path.getmtime(FBX) > before
    log("FBX %s (%s bytes)" % ("written" if ok else "NOT written", os.path.getsize(FBX) if os.path.exists(FBX) else 0))
    return ok

def run_script(name):
    path = os.path.join(HERE, name)
    log("running " + name)
    exec(compile(open(path).read(), path, "exec"), {"__name__": "__main__", "__file__": path})

def main():
    try:
        if not run_blender():
            return
        lib = unreal.EditorAssetLibrary
        for d in ("/Game/FFTS/AnimManny", "/Game/FFTS/Retarget", "/Game/FFTS/Anim", "/Game/FFTS/Characters"):
            if lib.does_directory_exist(d):
                log("delete %s: %s" % (d, lib.delete_directory(d)))
        run_script("ffts_import_fight_rig.py")
        run_script("ffts_retarget_manny.py")
        log("done")
    except Exception:
        log(traceback.format_exc())
    finally:
        with open(os.path.join(PROJECT, "Saved", "ffts_rebuild_report.txt"), "w") as fh:
            fh.write("\n".join(report))

main()
