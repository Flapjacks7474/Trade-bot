# Writes <Project>/Saved/ffts_retarget_inspect.txt: the IK rigs' chains and the retargeter's chain mapping.
import os
import unreal

PROJECT = unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir())
out = []
for name in ("IK_FightRig", "IK_FFTS_Manny"):
    rig = unreal.load_asset("/Game/FFTS/Retarget/" + name)
    c = unreal.IKRigController.get_controller(rig)
    out.append("== %s root=%s" % (name, c.get_retarget_root()))
    for ch in c.get_retarget_chains():
        props = {}
        for k in ("chain_name", "start_bone", "end_bone", "ik_goal_name"):
            try:
                props[k] = str(ch.get_editor_property(k))
            except Exception:
                pass
        out.append("  " + (str(props) if props else str(ch)))
rtg = unreal.load_asset("/Game/FFTS/Retarget/RTG_FightRig_to_Manny")
rc = unreal.IKRetargeterController.get_controller(rtg)
tgt = unreal.IKRigController.get_controller(unreal.load_asset("/Game/FFTS/Retarget/IK_FFTS_Manny"))
out.append("== mapping (target <- source)")
for ch in tgt.get_retarget_chains():
    cn = ch.get_editor_property("chain_name")
    try:
        src = rc.get_source_chain(cn)
    except Exception as e:
        src = "ERR %s" % e
    out.append("  %s <- %s" % (cn, src))
mesh = unreal.load_asset("/Game/FFTS/Characters/FFTS_FightRig")
skel = mesh.get_editor_property("skeleton")
out.append("== fight rig bones")
try:
    out.append("  " + ", ".join(str(b) for b in unreal.SkeletalMeshEditorSubsystem.get_bone_names(mesh)) if hasattr(unreal, "SkeletalMeshEditorSubsystem") else "")
except Exception as e:
    out.append("  bones: %s" % e)
with open(os.path.join(PROJECT, "Saved", "ffts_retarget_inspect.txt"), "w") as fh:
    fh.write("\n".join(out))

# Bone positions in one retargeted clip vs the fight rig's original.
pose_lines = ["== bone poses (frame 0 / 8)"]
for path, bones in (("/Game/FFTS/AnimManny/anim_street_jab", ("root", "pelvis", "hand_r", "foot_l")),
                    ("/Game/FFTS/Anim/anim_street_jab", ("Hips", "RightHand", "LeftFoot"))):
    anim = unreal.load_asset(path)
    if not anim:
        pose_lines.append("  missing " + path)
        continue
    for b in bones:
        for frame in (0, 8):
            try:
                t = unreal.AnimationLibrary.get_bone_pose_for_frame(anim, b, frame, False)
                pose_lines.append("  %s %s f%d loc=%s" % (path.split("/")[-2], b, frame, t.translation))
            except Exception as e:
                pose_lines.append("  %s %s: %s" % (path, b, e))
for name in ("SKM_Manny",):
    m = unreal.load_asset("/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny")
    pose_lines.append("  Manny bounds: %s" % (m.get_bounds() if m else None))
rig = unreal.load_asset("/Game/FFTS/Characters/FFTS_FightRig")
pose_lines.append("  FightRig bounds: %s" % (rig.get_bounds() if rig else None))
rc2 = unreal.IKRetargeterController.get_controller(unreal.load_asset("/Game/FFTS/Retarget/RTG_FightRig_to_Manny"))
for i in range(rc2.get_num_retarget_ops()):
    pose_lines.append("  op %d: %s enabled=%s" % (i, rc2.get_op_name(i), rc2.get_retarget_op_enabled(i)))
with open(os.path.join(PROJECT, "Saved", "ffts_retarget_inspect.txt"), "a") as fh:
    fh.write("\n" + "\n".join(pose_lines))
