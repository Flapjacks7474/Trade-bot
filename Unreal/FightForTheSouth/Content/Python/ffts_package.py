# FIGHT FOR THE SOUTH - package a Windows test build (Development) with RunUAT, detached from the editor.
# Output: Documents/FightForTheSouth_Art/Builds/Windows/FightForTheSouth.exe ; log: Builds/build_log.txt
import os
import subprocess
import unreal

PROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir())
UPROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.get_project_file_path())
ENGINE = unreal.Paths.convert_relative_path_to_full(unreal.Paths.engine_dir())
ART = os.path.normpath(os.path.join(PROJECT, "..", ".."))
OUT = os.path.join(ART, "Builds")
os.makedirs(OUT, exist_ok=True)
uat = os.path.normpath(os.path.join(ENGINE, "Build", "BatchFiles", "RunUAT.bat"))
args = [uat, "BuildCookRun", "-project=" + os.path.normpath(UPROJECT), "-noP4", "-platform=Win64",
        "-clientconfig=Development", "-build", "-cook", "-stage", "-pak", "-archive",
        "-archivedirectory=" + OUT, "-utf8output", "-unattended", "-nocompileeditor", "-prereqs"]
log = open(os.path.join(OUT, "build_log.txt"), "w")
log.write(" ".join('"%s"' % a if " " in a else a for a in args) + "\n"); log.flush()
flags = 0x00000008 | 0x00000200 | 0x01000000  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB
try:
    p = subprocess.Popen(args, stdout=log, stderr=subprocess.STDOUT, creationflags=flags, cwd=os.path.dirname(uat))
except OSError:
    p = subprocess.Popen(args, stdout=log, stderr=subprocess.STDOUT, creationflags=0x00000008 | 0x00000200, cwd=os.path.dirname(uat))
unreal.log("[FFTS package] started RunUAT pid %d -> %s" % (p.pid, OUT))
