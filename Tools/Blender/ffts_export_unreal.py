# FIGHT FOR THE SOUTH - export the fight rig for Unreal at real scale (centimetres, no armature scale).
# Headless:  blender -b Documents\FightForTheSouth_Art\blend\10_fight_rig.blend --python ffts_export_unreal.py
# Writes Documents\FightForTheSouth_Art\export\FFTS_FightRig_UE.fbx. The .blend is not saved (changes are in memory).
#
# The rig is built in metres. Exported as is, Unreal gets every bone translation in metres under a 100x scaled armature
# node, which breaks IK retargeting onto Manny (pelvis ends up ~80 m away). Here the scene is switched to centimetre
# units, the armature + body are scaled x100 and the scale applied (bone rest pose and mesh in cm), and every location
# key in every action is multiplied by 100, so the FBX carries plain centimetres at scale 1.
import bpy, os, sys

ROOT = os.path.join(os.path.expanduser("~"), "Documents", "FightForTheSouth_Art")
OUT = os.path.join(ROOT, "export", "FFTS_FightRig_UE.fbx")
K = 100.0

def fcurves(action):
    legacy = getattr(action, "fcurves", None)
    if legacy is not None:
        yield from legacy
        return
    for layer in getattr(action, "layers", []):      # Blender 4.4+ layered actions
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                yield from bag.fcurves

def scale_location_keys():
    n = 0
    for act in bpy.data.actions:
        for fc in fcurves(act):
            if fc.data_path.endswith("location"):
                for kp in fc.keyframe_points:
                    kp.co[1] *= K; kp.handle_left[1] *= K; kp.handle_right[1] *= K
                fc.update(); n += 1
    return n

def main():
    rig = bpy.data.objects["FFTS_Rig"]; man = bpy.data.objects["FFTS_Mannequin"]
    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    if rig.animation_data:
        rig.animation_data.action = None
    for pb in rig.pose.bones:   # export from the rest pose
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    before = rig.data.bones["Hips"].head_local.z
    us = bpy.context.scene.unit_settings
    us.system = 'METRIC'; us.scale_length = 1.0 / K; us.length_unit = 'CENTIMETERS'
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True); man.select_set(True); bpy.context.view_layer.objects.active = rig
    rig.scale = tuple(s * K for s in rig.scale)
    bpy.context.view_layer.update()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    curves = scale_location_keys()
    print("FFTS_SCALE Hips rest z %.3f -> %.3f, scaled %d location curves" % (before, rig.data.bones["Hips"].head_local.z, curves))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, add_leaf_bones=False,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', global_scale=1.0,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             armature_nodetype='NULL', primary_bone_axis='Y', secondary_bone_axis='X')
    print("FFTS_EXPORT_OK %s %d actions" % (OUT, len(bpy.data.actions)))

try:
    main()
except Exception as e:
    import traceback; traceback.print_exc()
    print("FFTS_EXPORT_FAILED %s" % e)
    sys.exit(1)
