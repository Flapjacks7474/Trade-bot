# FIGHT FOR THE SOUTH - Blender fight-animation pipeline (Blender 4.2+ / 5.x).
#
# Builds, in one run:
#   1. FFTS_Rig: a realistic-proportion humanoid skeleton (1.85 m, T-pose) with Mixamo / Unity-Humanoid bone names,
#      so Unity's Humanoid retargeting and Meshy/Mixamo rigs map onto it by name.
#   2. FFTS_Mannequin: a segmented, rigidly skinned fight mannequin on that skeleton (blocking/previs body; the hero mesh
#      replaces it once a T-pose version exists - see ffts_bind_character.py).
#   3. One Action per animation key used by the game content (Assets/_Project/Data/Moves/*.json): every style's stance
#      idle, guard, shuffle forward/back, every move, and the shared reactions.
#
# Timing comes from the move data, at 60 fps = one frame per simulation tick: the strike reaches its target on frame
# `Startup`, holds through `Active`, and returns to the stance by `Startup + Active + Recovery`. So the impact frame
# in the animation is the frame the simulation resolves the hit.
#
# Poses are authored in "character space": l = character's left (+X), f = forward toward the opponent (-Y), u = up.
# Hands and feet are placed by two-bone analytic IK; spine, head and hips by rotations. This is a blocking-quality
# pass (key poses + eased in-betweens), meant to be refined by hand or replaced by mocap per the shot list.
import bpy, json, math, os, urllib.request
from mathutils import Vector, Quaternion, Matrix, Euler

RAW = os.environ.get("FFTS_RAW", "https://raw.githubusercontent.com/Flapjacks7474/Trade-bot/claude/fight-for-south-foundation-mgjm04/")
ROOT = os.path.join(os.path.expanduser("~"), "Documents", "FightForTheSouth_Art")
STYLE_FILES = ["street_fighting", "boxing", "kickboxing", "muay_thai", "karate", "taekwondo", "kung_fu", "wrestling",
               "judo", "mma", "hybrids"]
FPS = 60

def V(l, f, u):
    return Vector((l, -f, u))

# ----------------------------------------------------------------------------------------------- skeleton
BONES = [  # name, head (l,f,u), tail, parent
    ("Hips", (0, 0, 0.98), (0, 0, 1.08), None),
    ("Spine", (0, 0, 1.08), (0, 0, 1.20), "Hips"),
    ("Spine1", (0, 0, 1.20), (0, 0, 1.32), "Spine"),
    ("Spine2", (0, 0, 1.32), (0, 0, 1.48), "Spine1"),
    ("Neck", (0, 0, 1.48), (0, 0.01, 1.58), "Spine2"),
    ("Head", (0, 0.01, 1.58), (0, 0.01, 1.82), "Neck"),
]
for side, s in (("Left", 1), ("Right", -1)):
    BONES += [
        (side + "Shoulder", (s * 0.03, 0, 1.44), (s * 0.17, 0, 1.45), "Spine2"),
        (side + "Arm", (s * 0.17, 0, 1.45), (s * 0.45, 0, 1.45), side + "Shoulder"),
        (side + "ForeArm", (s * 0.45, 0, 1.45), (s * 0.72, 0, 1.45), side + "Arm"),
        (side + "Hand", (s * 0.72, 0, 1.45), (s * 0.90, 0, 1.45), side + "ForeArm"),
        (side + "UpLeg", (s * 0.10, 0, 0.95), (s * 0.10, 0, 0.52), "Hips"),
        (side + "Leg", (s * 0.10, 0, 0.52), (s * 0.10, 0, 0.09), side + "UpLeg"),
        (side + "Foot", (s * 0.10, 0, 0.09), (s * 0.10, 0.14, 0.025), side + "Leg"),
        (side + "ToeBase", (s * 0.10, 0.14, 0.025), (s * 0.10, 0.22, 0.025), side + "Foot"),
    ]
BONE = {b[0]: b for b in BONES}
REST_HEAD = {b[0]: V(*b[1]) for b in BONES}
REST_TAIL = {b[0]: V(*b[2]) for b in BONES}
PARENT = {b[0]: b[3] for b in BONES}
UPPER = 0.28; FORE = 0.27; THIGH = 0.43; SHIN = 0.43

def build_rig():
    arm_data = bpy.data.armatures.new("FFTS_Rig")
    rig = bpy.data.objects.new("FFTS_Rig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for name, h, t, p in BONES:
        eb = arm_data.edit_bones.new(name)
        eb.head = V(*h); eb.tail = V(*t)
        if p: eb.parent = arm_data.edit_bones[p]; eb.use_connect = (Vector(eb.head) - Vector(arm_data.edit_bones[p].tail)).length < 1e-4
    # predictable rolls: arms roll so local Z points back, legs so local Z points forward
    for eb in arm_data.edit_bones:
        if "Arm" in eb.name or "Hand" in eb.name or "Shoulder" in eb.name: eb.align_roll(Vector((0, 1, 0)))
        elif "Leg" in eb.name or "Foot" in eb.name or "Toe" in eb.name: eb.align_roll(Vector((0, -1, 0)))
        else: eb.align_roll(Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    arm_data.display_type = 'STICK'
    rig.show_in_front = True
    return rig

# ----------------------------------------------------------------------------------------------- mannequin
import bmesh

def _ellipsoid(bm, center, radii, segs=16, rings=10):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    verts = geom["verts"]
    for v in verts:
        v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + center
    return verts

def _limb(bm, a, b, ra, rb, segs=14):
    """Tapered capsule-ish cylinder from a to b (radii ra at a, rb at b), lightly bulged in the middle."""
    d = b - a; L = d.length
    geom = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=ra, radius2=rb, depth=L)
    verts = geom["verts"]
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    for v in verts:
        z = v.co.z / L + 0.5           # 0 at a, 1 at b
        bulge = 1.0 + 0.10 * math.sin(math.pi * max(0.0, min(1.0, z)))
        co = Vector((v.co.x * bulge, v.co.y * bulge, v.co.z))
        v.co = q @ co + (a + b) / 2
    return verts

PART_COLORS = {"skin": (0.36, 0.22, 0.14, 1), "top": (0.85, 0.84, 0.80, 1), "pants": (0.28, 0.30, 0.20, 1),
               "boots": (0.10, 0.09, 0.08, 1), "chain": (0.85, 0.66, 0.25, 1)}

def build_mannequin(rig):
    me = bpy.data.meshes.new("FFTS_Mannequin")
    ob = bpy.data.objects.new("FFTS_Mannequin", me)
    bpy.context.scene.collection.objects.link(ob)
    mats = {}
    for k, c in PART_COLORS.items():
        m = bpy.data.materials.get("FFTS_" + k) or bpy.data.materials.new("FFTS_" + k)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = c
            bsdf.inputs["Roughness"].default_value = 0.35 if k == "chain" else 0.7
            if k == "chain": bsdf.inputs["Metallic"].default_value = 1.0
        m.diffuse_color = c
        me.materials.append(m); mats[k] = len(me.materials) - 1
    parts = []  # (bone, mat, builder)
    H = REST_HEAD; T = REST_TAIL
    P = lambda l, f, u: V(l, f, u)
    parts += [
        ("Hips", "pants", lambda bm: _ellipsoid(bm, P(0, 0, 0.97), (0.175, 0.12, 0.11))),
        ("Spine", "top", lambda bm: _ellipsoid(bm, P(0, 0.005, 1.12), (0.155, 0.11, 0.10))),
        ("Spine1", "top", lambda bm: _ellipsoid(bm, P(0, 0.01, 1.25), (0.165, 0.115, 0.10))),
        ("Spine2", "top", lambda bm: _ellipsoid(bm, P(0, 0.015, 1.38), (0.20, 0.13, 0.12))),
        ("Spine2", "skin", lambda bm: _ellipsoid(bm, P(0, -0.01, 1.42), (0.215, 0.11, 0.07))),   # trapezius/upper back
        ("Neck", "skin", lambda bm: _limb(bm, P(0, 0.0, 1.47), P(0, 0.01, 1.60), 0.060, 0.055)),
        ("Neck", "chain", lambda bm: _ellipsoid(bm, P(0, 0.02, 1.49), (0.085, 0.075, 0.012), 20, 6)),
        ("Head", "skin", lambda bm: _ellipsoid(bm, P(0, 0.015, 1.70), (0.078, 0.095, 0.115))),
        ("Head", "skin", lambda bm: _ellipsoid(bm, P(0, 0.06, 1.64), (0.055, 0.05, 0.045))),       # jaw
    ]
    for side, s in (("Left", 1), ("Right", -1)):
        parts += [
            (side + "Shoulder", "skin", lambda bm, s=s: _ellipsoid(bm, P(s * 0.19, 0, 1.44), (0.075, 0.075, 0.07))),  # deltoid
            (side + "Arm", "skin", lambda bm, n=side: _limb(bm, H[n + "Arm"], T[n + "Arm"], 0.058, 0.045)),
            (side + "ForeArm", "skin", lambda bm, n=side: _limb(bm, H[n + "ForeArm"], T[n + "ForeArm"], 0.045, 0.033)),
            (side + "ForeArm", "skin", lambda bm, n=side: _ellipsoid(bm, H[n + "ForeArm"], (0.045, 0.045, 0.045), 10, 8)),
            (side + "Hand", "skin", lambda bm, s=s: _ellipsoid(bm, P(s * 0.79, 0, 1.445), (0.055, 0.045, 0.035), 12, 8)),
            (side + "UpLeg", "pants", lambda bm, n=side: _limb(bm, H[n + "UpLeg"] + V(0, 0, 0.02), T[n + "UpLeg"], 0.085, 0.058)),
            (side + "Leg", "pants", lambda bm, n=side: _limb(bm, H[n + "Leg"], T[n + "Leg"] + V(0, 0, 0.05), 0.058, 0.050)),
            (side + "Leg", "pants", lambda bm, n=side: _ellipsoid(bm, H[n + "Leg"], (0.06, 0.06, 0.06), 10, 8)),
            (side + "Leg", "pants", lambda bm, s=s: _ellipsoid(bm, P(s * 0.10, -0.02, 0.36), (0.075, 0.07, 0.07), 10, 8)),  # cargo pocket
            (side + "Foot", "boots", lambda bm, s=s: _ellipsoid(bm, P(s * 0.10, 0.06, 0.06), (0.055, 0.13, 0.06), 12, 8)),
            (side + "ToeBase", "boots", lambda bm, s=s: _ellipsoid(bm, P(s * 0.10, 0.17, 0.035), (0.05, 0.06, 0.035), 10, 6)),
        ]
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    groups = {}
    for bone, mat, fn in parts:
        verts = fn(bm)
        bm.faces.ensure_lookup_table()
        if bone not in groups:
            groups[bone] = ob.vertex_groups.new(name=bone).index
        vs = set(verts)
        for v in verts: v[deform][groups[bone]] = 1.0
        for f in bm.faces:
            if all(v in vs for v in f.verts): f.material_index = mats[mat]; f.smooth = True
    bm.to_mesh(me); bm.free()
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
    ob.parent = rig
    return ob

# ----------------------------------------------------------------------------------------------- pose solver
def qchar(yaw=0.0, pitch=0.0, roll=0.0):
    """yaw>0 turns the lead (left) side toward the opponent; pitch>0 leans forward; roll>0 leans to the left."""
    return (Quaternion((0, 0, 1), math.radians(-yaw)) @ Quaternion((1, 0, 0), math.radians(pitch))
            @ Quaternion((0, 1, 0), math.radians(roll)))

def frame_from(yaxis, zhint):
    y = yaxis.normalized()
    z = (zhint - y * zhint.dot(y))
    if z.length < 1e-6:
        z = Vector((0, 0, 1)) - y * y.z
        if z.length < 1e-6: z = Vector((0, 1, 0))
    z.normalize(); x = y.cross(z)
    return Matrix((x, y, z)).transposed().to_quaternion()

def two_bone(root, target, a, b, pole):
    d = target - root; dist = d.length
    dist = max(abs(a - b) + 1e-3, min(dist, (a + b) * 0.999))
    u = d.normalized() if d.length > 1e-6 else Vector((0, -1, 0))
    x = (a * a - b * b + dist * dist) / (2 * dist)
    h = math.sqrt(max(0.0, a * a - x * x))
    w = pole - u * pole.dot(u)
    w = w.normalized() if w.length > 1e-6 else Vector((0, 0, -1))
    mid = root + u * x + w * h
    end = root + u * dist
    return mid, end, w

REST_POLE = {"Arm": V(0, -1, 0), "Leg": V(0, 1, 0)}   # elbows point back, knees forward in the T-pose
REST_Q = {}

def default_pose():
    return {"hips": (0, 0, 0), "hips_rot": (0, 0, 0), "chest": (0, 0, 0), "head": (0, 0, 0),
            "lh": (0.25, 0.25, 1.30), "rh": (-0.25, 0.25, 1.30), "lf": (0.12, 0.0, 0.09), "rf": (-0.12, 0.0, 0.09),
            "lf_yaw": 0, "rf_yaw": 0, "lf_dir": None, "rf_dir": None, "lh_pole": None, "rh_pole": None,
            "lk_pole": None, "rk_pole": None, "lh_twist": 0, "rh_twist": 0}

def solve(pose):
    """Returns {bone: armature-space rotation D (absolute delta from rest)} and the hips offset."""
    D = {}
    hr = qchar(*pose["hips_rot"]); ch = qchar(*pose["chest"]); hd = qchar(*pose["head"])
    D["Hips"] = hr
    ident = Quaternion()
    D["Spine"] = hr @ ident.slerp(ch, 1 / 3)
    D["Spine1"] = hr @ ident.slerp(ch, 2 / 3)
    D["Spine2"] = hr @ ch
    D["Neck"] = hr @ ch @ ident.slerp(hd, 0.4)
    D["Head"] = hr @ ch @ hd
    off = V(*pose["hips"])
    pos = {"Hips": REST_HEAD["Hips"] + off}
    order = ["Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpLeg", "RightUpLeg"]
    for side in ("Left", "Right"): D[side + "Shoulder"] = D["Spine2"]
    for n in order:
        p = PARENT[n]
        pos[n] = pos[p] + D[p] @ (REST_HEAD[n] - REST_HEAD[p])
    for side, s, hk, fk in (("Left", 1, "lh", "lf"), ("Right", -1, "rh", "rf")):
        # arm
        sh = pos[side + "Shoulder"] + D[side + "Shoulder"] @ (REST_HEAD[side + "Arm"] - REST_HEAD[side + "Shoulder"])
        pole = pose.get(hk + "_pole")
        pole = V(*pole) if pole else (V(s * 0.6, -0.5, -1.0))
        tgt = V(*pose[hk])
        elbow, hand, w = two_bone(sh, tgt, UPPER, FORE, pole)
        D[side + "Arm"] = frame_from(elbow - sh, w) @ frame_from(REST_TAIL[side + "Arm"] - REST_HEAD[side + "Arm"], REST_POLE["Arm"]).inverted()
        qf = frame_from(hand - elbow, w) @ frame_from(REST_TAIL[side + "ForeArm"] - REST_HEAD[side + "ForeArm"], REST_POLE["Arm"]).inverted()
        if pose.get(hk + "_twist"): qf = Quaternion((hand - elbow).normalized(), math.radians(pose[hk + "_twist"])) @ qf
        D[side + "ForeArm"] = qf
        D[side + "Hand"] = qf
        # leg
        hip = pos[side + "UpLeg"]
        kp = pose.get(("lk" if s > 0 else "rk") + "_pole")
        kpole = V(*kp) if kp else V(s * 0.15, 1.0, 0.0)
        ankle_t = V(*pose[fk])
        knee, ankle, w2 = two_bone(hip, ankle_t, THIGH, SHIN, kpole)
        D[side + "UpLeg"] = frame_from(knee - hip, w2) @ frame_from(REST_TAIL[side + "UpLeg"] - REST_HEAD[side + "UpLeg"], REST_POLE["Leg"]).inverted()
        D[side + "Leg"] = frame_from(ankle - knee, w2) @ frame_from(REST_TAIL[side + "Leg"] - REST_HEAD[side + "Leg"], REST_POLE["Leg"]).inverted()
        fdir = pose.get(fk + "_dir")
        rest_foot = REST_TAIL[side + "Foot"] - REST_HEAD[side + "Foot"]
        if fdir:
            fd = V(*fdir)
        else:
            yaw = math.radians(pose.get(fk + "_yaw", 0))
            fd = Quaternion((0, 0, 1), yaw * s) @ rest_foot   # positive yaw turns the toes outward
        D[side + "Foot"] = frame_from(fd, V(0, 0, 1) if not fdir else (ankle - knee).normalized() * -1) @ frame_from(rest_foot, V(0, 0, 1)).inverted()
        D[side + "ToeBase"] = D[side + "Foot"]
    return D, off

def apply_pose(rig, pose, frame, prev):
    D, off = solve(pose)
    for pb in rig.pose.bones:
        n = pb.name
        Rb = REST_Q[n]
        Dp = D[PARENT[n]] if PARENT[n] else Quaternion()
        q = Rb.inverted() @ Dp.inverted() @ D[n] @ Rb
        q.normalize()
        if n in prev and prev[n].dot(q) < 0: q.negate()
        prev[n] = q.copy()
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)
    hips = rig.pose.bones["Hips"]
    hips.location = REST_Q["Hips"].inverted() @ off
    hips.keyframe_insert("location", frame=frame, group="Hips")

# ----------------------------------------------------------------------------------------------- stances
def stance(blade=20, drop=0.04, width=0.18, stride=0.30, lean=8, lead=(0.12, 0.30, 1.35), rear=(-0.12, 0.18, 1.40),
           head=(0, 0, 0), lead_yaw=15, rear_yaw=45, shift=(0, 0), bob=0.012, bob_hz=1.0, sway=0.0, heel=False,
           lead_pole=None, rear_pole=None, chest_yaw=0):
    return dict(blade=blade, drop=drop, width=width, stride=stride, lean=lean, lead=lead, rear=rear, head=head,
                lead_yaw=lead_yaw, rear_yaw=rear_yaw, shift=shift, bob=bob, bob_hz=bob_hz, sway=sway, heel=heel,
                lead_pole=lead_pole, rear_pole=rear_pole, chest_yaw=chest_yaw)

STANCES = {
    "street": stance(),
    "boxing": stance(blade=40, drop=0.07, width=0.16, stride=0.36, lean=12, lead=(0.06, 0.20, 1.08), rear=(-0.07, 0.13, 1.52),
                     head=(10, 8, 0), sway=0.025, bob_hz=1.2, chest_yaw=8),
    "kickboxing": stance(blade=20, drop=0.05, width=0.20, stride=0.32, lean=6, lead=(0.09, 0.14, 1.60), rear=(-0.09, 0.11, 1.60),
                         head=(0, 6, 0), lead_pole=(0.2, -0.2, -1), rear_pole=(-0.2, -0.2, -1)),
    "muay_thai": stance(blade=10, drop=0.02, width=0.22, stride=0.28, lean=-3, lead=(0.15, 0.32, 1.58), rear=(-0.13, 0.24, 1.55),
                        shift=(0, -0.05), heel=True, bob=0.02, bob_hz=0.8),
    "karate": stance(blade=60, drop=0.12, width=0.10, stride=0.56, lean=0, lead=(0.06, 0.48, 1.30), rear=(-0.16, -0.02, 1.08),
                     lead_yaw=0, rear_yaw=60, bob=0.006),
    "taekwondo": stance(blade=70, drop=0.03, width=0.08, stride=0.42, lean=-4, lead=(0.10, 0.26, 1.15), rear=(-0.06, 0.14, 1.25),
                        bob=0.045, bob_hz=2.0, rear_yaw=70),
    "kung_fu": stance(blade=30, drop=0.18, width=0.22, stride=0.36, lean=0, lead=(0.03, 0.42, 1.30), rear=(-0.02, 0.26, 1.24),
                      lead_yaw=0, rear_yaw=40, bob=0.008, lead_pole=(0.3, 0, -1), rear_pole=(-0.3, 0, -1)),
    "wrestling": stance(blade=15, drop=0.25, width=0.32, stride=0.34, lean=32, lead=(0.21, 0.48, 1.00), rear=(-0.21, 0.42, 1.02),
                        head=(0, -24, 0), lead_yaw=20, rear_yaw=30, bob=0.01, sway=0.02),
    "judo": stance(blade=10, drop=0.04, width=0.26, stride=0.14, lean=5, lead=(0.21, 0.42, 1.30), rear=(-0.15, 0.42, 1.42),
                   lead_yaw=10, rear_yaw=15, sway=0.02, bob_hz=0.7),
    "mma": stance(blade=25, drop=0.08, width=0.25, stride=0.32, lean=10, lead=(0.10, 0.26, 1.42), rear=(-0.10, 0.18, 1.46),
                  head=(0, 6, 0), bob=0.015, sway=0.012),
    "southside_hands": stance(blade=35, drop=0.06, width=0.17, stride=0.34, lean=10, lead=(0.05, 0.18, 1.12), rear=(-0.07, 0.13, 1.52),
                              head=(8, 8, 0), sway=0.03, bob_hz=1.3, chest_yaw=10),
    "dirty_boxing": stance(blade=20, drop=0.11, width=0.24, stride=0.28, lean=20, lead=(0.08, 0.22, 1.42), rear=(-0.08, 0.18, 1.45),
                           head=(0, 12, 0), bob=0.012),
    "clinch_king": stance(blade=8, drop=0.03, width=0.22, stride=0.26, lean=-2, lead=(0.18, 0.40, 1.52), rear=(-0.16, 0.36, 1.50),
                          heel=True, bob=0.02, bob_hz=0.8),
    "tricking": stance(blade=60, drop=0.05, width=0.10, stride=0.42, lean=-2, lead=(0.10, 0.30, 1.35), rear=(-0.08, 0.16, 1.40),
                       bob=0.05, bob_hz=2.2, rear_yaw=65),
    "flowing_water": stance(blade=25, drop=0.10, width=0.22, stride=0.30, lean=0, lead=(0.05, 0.36, 1.25), rear=(-0.03, 0.24, 1.32),
                            lead_yaw=5, bob=0.01, sway=0.02, bob_hz=0.6),
    "cage_pressure": stance(blade=10, drop=0.08, width=0.26, stride=0.28, lean=12, lead=(0.09, 0.14, 1.58), rear=(-0.09, 0.12, 1.58),
                            head=(0, 8, 0), bob=0.012),
}

def stance_pose(st, t=0.0):
    p = default_pose()
    bob = st["bob"] * math.sin(2 * math.pi * st["bob_hz"] * t)
    sway = st["sway"] * math.sin(math.pi * st["bob_hz"] * t)
    p["hips"] = (st["shift"][0] + sway, st["shift"][1], -st["drop"] - abs(bob))
    p["hips_rot"] = (st["blade"] * 0.8, 0, 0)
    p["chest"] = (st["blade"] * 0.25 + st["chest_yaw"], st["lean"], 0)
    p["head"] = (-st["blade"] * 0.9 - st["chest_yaw"] + st["head"][0], -st["lean"] * 0.7 + st["head"][1], st["head"][2])
    w = st["width"] / 2; s = st["stride"] / 2
    p["lf"] = (w, s, 0.09 + (0.03 if st["heel"] else 0)); p["rf"] = (-w, -s, 0.09)
    p["lf_yaw"] = st["lead_yaw"] - st["blade"] * 0.5; p["rf_yaw"] = st["rear_yaw"]
    lx, lfw, lu = st["lead"]; rx, rfw, ru = st["rear"]
    p["lh"] = (lx + sway, lfw, lu - abs(bob)); p["rh"] = (rx + sway, rfw, ru - abs(bob))
    p["lh_pole"] = st["lead_pole"]; p["rh_pole"] = st["rear_pole"]
    return p

# ----------------------------------------------------------------------------------------------- move templates
def P(base, **kw):
    """Copy a pose and apply edits. Tuple fields given as deltas use the '_d' suffix (e.g. hips_d=(0,0.1,0))."""
    p = dict(base)
    for k, v in kw.items():
        if k.endswith("_d"):
            key = k[:-2]; p[key] = tuple(a + b for a, b in zip(p[key], v))
        else:
            p[k] = v
    return p

def hand_key(side): return "lh" if side > 0 else "rh"
def foot_key(side): return "lf" if side > 0 else "rf"
GUARD_CHIN = {1: (0.08, 0.14, 1.52), -1: (-0.08, 0.14, 1.52)}

def straight(base, side=1, height=1.55, reach=0.80, drop=0.0, lunge=0.0, palm=False):
    hk = hand_key(side); other = hand_key(-side)
    ant = P(base, chest_d=(-side * 8, 0, 0))
    imp = P(base, chest_d=(side * 28, 4, -side * 4), hips_rot_d=(side * 14, 0, 0), hips_d=(0, 0.04 + lunge, -drop),
            head_d=(-side * 10, 0, side * 6))
    imp[hk] = (side * 0.03, reach + lunge, height)
    imp[other] = GUARD_CHIN[-side]
    if lunge: imp["lf"] = (imp["lf"][0], imp["lf"][1] + lunge, imp["lf"][2])
    if side < 0: imp["rf_yaw"] = imp["rf_yaw"] - 35
    if palm: imp[hk + "_twist"] = 90
    return [(0.55, ant), (1.0, imp)]

def hook(base, side=1, height=1.52, drop=0.02, body=False):
    hk = hand_key(side); other = hand_key(-side)
    h = 1.15 if body else height
    ant = P(base, chest_d=(-side * 15, 0, 0), hips_d=(0, 0, -drop))
    ant[hk] = (side * 0.30, 0.18, h)
    imp = P(base, chest_d=(side * 38, 6, -side * 6), hips_rot_d=(side * 20, 0, 0), hips_d=(0, 0.02, -drop - (0.08 if body else 0)))
    imp[hk] = (-side * 0.06, 0.42, h); imp[hk + "_pole"] = (side * 1.0, 0.0, 0.25)
    imp[other] = GUARD_CHIN[-side]
    fol = P(imp, chest_d=(side * 10, 0, 0)); fol[hk] = (-side * 0.20, 0.30, h)
    return [(0.5, ant), (1.0, imp), ("hold", fol)]

def uppercut(base, side=-1, height=1.62):
    hk = hand_key(side); other = hand_key(-side)
    ant = P(base, hips_d=(0, 0, -0.07), chest_d=(-side * 10, 6, side * 8)); ant[hk] = (side * 0.12, 0.22, 1.12)
    imp = P(base, hips_d=(0, 0.03, 0.01), chest_d=(side * 22, -6, -side * 6), hips_rot_d=(side * 14, 0, 0))
    imp[hk] = (side * 0.02, 0.36, height); imp[hk + "_pole"] = (side * 0.3, 0.3, -1); imp[other] = GUARD_CHIN[-side]
    return [(0.5, ant), (1.0, imp)]

def overhand(base, side=-1):
    hk = hand_key(side); other = hand_key(-side)
    ant = P(base, chest_d=(-side * 15, -6, side * 6)); ant[hk] = (side * 0.22, -0.05, 1.66)
    imp = P(base, chest_d=(side * 38, 22, -side * 12), hips_rot_d=(side * 22, 0, 0), hips_d=(0, 0.06, -0.06))
    imp[hk] = (side * 0.0, 0.62, 1.45); imp[hk + "_pole"] = (side * 1, 0, 0.6); imp[other] = GUARD_CHIN[-side]
    return [(0.5, ant), (1.0, imp)]

def body_shot(base, side=1):
    hk = hand_key(side); other = hand_key(-side)
    ant = P(base, hips_d=(0, 0, -0.06), chest_d=(-side * 10, 8, 0))
    imp = P(base, hips_d=(0, 0.04, -0.13), chest_d=(side * 26, 14, -side * 10), hips_rot_d=(side * 12, 0, 0))
    imp[hk] = (side * 0.02, 0.46, 1.12); imp[other] = GUARD_CHIN[-side]
    return [(0.5, ant), (1.0, imp)]

def elbow_strike(base, side=1, vertical=False):
    hk = hand_key(side); other = hand_key(-side)
    ant = P(base, chest_d=(-side * 18, 0, 0))
    imp = P(base, chest_d=(side * 48, 8, -side * 6), hips_rot_d=(side * 20, 0, 0), hips_d=(0, 0.05, 0))
    imp[hk] = (-side * 0.04, 0.12, 1.62 if vertical else 1.52); imp[hk + "_pole"] = (0, 1, 0.2 if not vertical else 1)
    imp[other] = GUARD_CHIN[-side]
    return [(0.5, ant), (1.0, imp)]

def front_kick(base, side=1, height=1.0, reach=0.88, push=False):
    fk = foot_key(side)
    ant = P(base, chest_d=(0, -8, 0)); ant[fk] = (side * 0.10, 0.28, 0.62); ant[fk + "_dir"] = (0, 0.4, -1)
    imp = P(base, chest_d=(0, -16, 0), hips_d=(0, 0.06 if push else 0.03, 0.02)); imp[fk] = (side * 0.07, reach, height)
    imp[fk + "_dir"] = (0, 1, 0.25 if not push else 0.6)
    ret = P(ant)
    return [(0.55, ant), (1.0, imp), ("recover", ret)]

def round_kick(base, side=-1, height=1.10, sweep=0.05, yaw=70, low=False):
    fk = foot_key(side); hk = hand_key(side)
    h = 0.48 if low else height
    ant = P(base, hips_rot_d=(side * yaw * 0.4, 0, 0)); ant[fk] = (side * 0.25, 0.18, 0.62 if not low else 0.35)
    imp = P(base, hips_rot_d=(side * yaw, 0, 0), chest_d=(side * 10, -4, -side * 24), hips_d=(-side * 0.04, 0.03, 0.01))
    imp[fk] = (-side * sweep, 0.62, h); imp[fk + "_dir"] = (-side * 1, 0.35, 0.1); imp[fk[0] + "k_pole"] = (0, 0, 1)
    imp[hk] = (side * 0.30, -0.25, 1.08)
    # standing foot pivots outward
    st = foot_key(-side); imp[st + "_yaw"] = imp[st + "_yaw"] + 60
    return [(0.55, ant), (1.0, imp), ("recover", P(ant))]

def side_kick(base, side=1, height=1.0):
    fk = foot_key(side)
    ant = P(base, hips_rot_d=(side * 60, 0, 0)); ant[fk] = (side * 0.05, 0.25, 0.75); ant[fk + "_dir"] = (0, 0.3, -1)
    imp = P(base, hips_rot_d=(side * 85, -10, 0), chest_d=(side * 5, -28, -side * 30), hips_d=(0, -0.04, 0.02))
    imp[fk] = (0, 0.98, height); imp[fk + "_dir"] = (0, 1, 0.1); imp[fk[0] + "k_pole"] = (0, 0, 1)
    return [(0.55, ant), (1.0, imp), ("recover", P(ant))]

def spin(base, side, degrees, impact_fraction, strike):
    """A spinning move: hips and chest yaw advance through `degrees` in quaternion-safe steps of at most 60 degrees.
    `strike(pose)` edits the pose at the impact step; later steps land in the recovery."""
    keys = []
    steps = max(3, int(math.ceil(abs(degrees) / 60.0)))
    hit = max(1, min(steps, int(round(impact_fraction * steps))))
    for i in range(1, steps + 1):
        ang = degrees * i / steps
        p = P(base, hips_rot_d=(ang, 0, 0), chest_d=(ang * 0.15, 0, 0), head_d=(-ang * 0.1, 0, 0))
        if i == hit:
            strike(p); keys.append((1.0, p))
        elif i < hit:
            keys.append((0.95 * i / hit, p))
        else:
            keys.append(("recover%.2f" % ((i - hit) / max(1, steps - hit)), p))
    return keys

def spin_kick(base, side=-1, height=1.2, degrees=None, back=False, low=False):
    fk = foot_key(side)
    deg = degrees if degrees is not None else 360
    def strike(p):
        if back:
            p[fk] = (0.0, 0.96, height); p[fk + "_dir"] = (0, 1, -0.2); p[fk[0] + "k_pole"] = (0, 0, -1)
            p["chest"] = (p["chest"][0], 25, p["chest"][2])
        else:
            p[fk] = (-side * 0.15, 0.66 if not low else 0.75, height if not low else 0.10)
            p[fk + "_dir"] = (-side, 0.3, 0); p["chest"] = (p["chest"][0], -10 if not low else 40, -side * 25)
            if low: p["hips"] = (p["hips"][0], p["hips"][1], -0.45)
    turn = 1 if side < 0 else -1   # rear-leg spins turn the back toward the opponent first
    if back:
        return spin(base, side, 360 * turn, 0.5, strike)
    return spin(base, side, deg * turn, 0.75, strike)

def spin_strike(base, side=1, height=1.55, elbow=False):
    hk = hand_key(side)
    def strike(p):
        p[hk] = (side * 0.10, 0.60 if not elbow else 0.15, height); p[hk + "_pole"] = (0, 1, 0) if elbow else (side, 0, 0)
    return spin(base, side, -360 * side, 0.6, strike)

def knee(base, side=-1, flying=False, clinch=False):
    fk = foot_key(side); other = foot_key(-side)
    ant = P(base, hips_d=(0, 0, -0.06), chest_d=(0, 5, 0))
    if clinch or flying:
        ant["lh"] = (0.10, 0.42, 1.58); ant["rh"] = (-0.10, 0.42, 1.58)
    imp = P(base, hips_d=(0, 0.14, 0.30 if flying else 0.02), chest_d=(side * 8, -12, 0), hips_rot_d=(side * 10, -10, 0))
    imp[fk] = (side * 0.10, 0.12, 0.80 if not flying else 1.05); imp[fk[0] + "k_pole"] = (0, 1, 0.6)
    if flying: imp[other] = (-side * 0.10, -0.20, 0.55)
    if clinch or flying:
        imp["lh"] = (0.10, 0.42, 1.48); imp["rh"] = (-0.10, 0.42, 1.48)
    return [(0.5, ant), (1.0, imp), ("recover", P(ant))]

def sweep(base, side=1, low=0.12, grab=True):
    fk = foot_key(side)
    ant = P(base);
    if grab: ant["lh"] = (0.15, 0.42, 1.40); ant["rh"] = (-0.12, 0.40, 1.45)
    imp = P(ant, hips_rot_d=(-side * 20, 0, 0), chest_d=(-side * 15, 10, side * 8))
    imp[fk] = (-side * 0.10, 0.50, low); imp[fk + "_dir"] = (-side, 0.5, 0)
    return [(0.5, ant), (1.0, imp)]

def grab(base, height=1.40, reach=0.48, level=0.0, lean=12):
    ant = P(base, chest_d=(0, lean * 0.5, 0)); ant["lh"] = (0.25, 0.30, height); ant["rh"] = (-0.25, 0.30, height)
    imp = P(base, chest_d=(0, lean, 0), hips_d=(0, 0.10, -level)); imp["lh"] = (0.12, reach, height); imp["rh"] = (-0.12, reach, height)
    return [(0.5, ant), (1.0, imp)]

def shoot(base, single=False):
    ant = P(base, hips_d=(0, 0.02, -0.25), chest_d=(0, 30, 0))
    imp = P(base, hips_d=(0, 0.32, -0.46), chest_d=(0, 58, 0), head_d=(0, -40, 0))
    imp["lh"] = (0.16, 0.74, 0.62); imp["rh"] = (-0.16 if not single else 0.02, 0.72, 0.62 if not single else 0.45)
    imp["lf"] = (0.12, 0.62, 0.09); imp["rf"] = (-0.12, -0.10, 0.12); imp["lk_pole"] = (0, 1, 0.5)
    return [(0.5, ant), (1.0, imp)]

def clinch_hold(p):
    p["lh"] = (0.10, 0.40, 1.50); p["rh"] = (-0.10, 0.40, 1.50); return p

def clinch_strike(base, kind="knee", side=-1):
    c = clinch_hold(P(base, chest_d=(0, 10, 0)))
    if kind == "knee": return knee(c, side, clinch=True)
    if kind == "uppercut":
        hk = hand_key(side); imp = P(c, chest_d=(side * 15, -5, 0)); imp[hk] = (side * 0.02, 0.30, 1.56)
        return [(1.0, imp)]
    if kind == "headbutt":
        imp = P(c, chest_d=(0, 22, 0), head_d=(0, 30, 0), hips_d=(0, 0.06, 0)); return [(1.0, imp)]
    if kind == "choke":
        imp = P(c, chest_d=(0, -8, 0)); imp["lh"] = (0.05, 0.34, 1.46); imp["rh"] = (-0.05, 0.34, 1.46); return [(1.0, imp)]
    if kind == "elbow": return elbow_strike(c, side, vertical=True)
    return [(1.0, c)]

def throw(base, kind="hip"):
    g = clinch_hold(P(base))
    if kind in ("hip", "seoi", "spiral", "dump", "lock"):
        turn = 170 if kind in ("hip", "seoi") else 110
        mid = P(g, hips_rot_d=(turn * 0.5, 0, 0), hips_d=(0, 0.12, -0.10))
        imp = P(g, hips_rot_d=(turn, 0, 0), chest_d=(0, 40, 0), hips_d=(0, 0.18, -0.22))
        imp["lh"] = (0.10, 0.10, 1.10); imp["rh"] = (-0.05, 0.45, 0.80)
        return [(0.4, mid), (1.0, imp)]
    if kind == "suplex":
        ant = P(g, hips_d=(0, 0.10, -0.16), chest_d=(0, 10, 0)); ant["lh"] = (0.10, 0.30, 1.22); ant["rh"] = (-0.10, 0.30, 1.22)
        imp = P(g, hips_d=(0, 0.06, -0.10), chest_d=(0, -65, 0), head_d=(0, -20, 0)); imp["lh"] = (0.10, -0.25, 1.62); imp["rh"] = (-0.10, -0.25, 1.62)
        return [(0.5, ant), (1.0, imp)]
    if kind == "powerbomb":
        ant = P(g, hips_d=(0, 0.05, -0.05)); ant["lh"] = (0.15, 0.20, 1.92); ant["rh"] = (-0.15, 0.20, 1.92)
        imp = P(g, hips_d=(0, 0.20, -0.32), chest_d=(0, 40, 0)); imp["lh"] = (0.15, 0.62, 0.60); imp["rh"] = (-0.15, 0.62, 0.60)
        return [(0.5, ant), (1.0, imp)]
    if kind == "drive":
        imp = P(g, hips_d=(0, 0.30, -0.10), chest_d=(0, 30, 0)); imp["lh"] = (0.15, 0.68, 1.30); imp["rh"] = (-0.15, 0.68, 1.30)
        imp["lf"] = (0.12, 0.50, 0.09)
        return [(1.0, imp)]
    if kind == "sacrifice":
        imp = P(g, hips_d=(0, 0.10, -0.65), chest_d=(0, -70, 0)); imp["lf"] = (0.05, 0.40, 0.95); imp["lf_dir"] = (0, 0.2, 1)
        return [(1.0, imp)]
    if kind == "reversal":
        imp = P(g, hips_rot_d=(90, 0, 0), chest_d=(30, 0, 0)); imp["lh"] = (0.25, 0.45, 1.30); imp["rh"] = (-0.05, 0.55, 1.35)
        return [(0.6, P(g, hips_rot_d=(40, 0, 0))), (1.0, imp)]
    if kind == "shove":
        imp = P(g, hips_d=(0, 0.10, 0)); imp["lh"] = (0.14, 0.62, 1.40); imp["rh"] = (-0.14, 0.62, 1.40); return [(1.0, imp)]
    return [(1.0, g)]

def counter(base, then):
    parry = P(base); parry["lh"] = (0.18, 0.36, 1.58); parry["lh_pole"] = (1, 0, -0.2)
    keys = then(base)
    return [(0.35, parry)] + keys

def taunt(base, kind):
    keys = []
    if kind == "beckon":
        for i, fr in enumerate((0.2, 0.4, 0.6, 0.8)):
            p = P(base); p["lh"] = (0.10, 0.48, 1.30 + (0.08 if i % 2 else 0)); p["lh_twist"] = 90; keys.append((fr, p))
    elif kind == "gloves_down":
        p = P(base, chest_d=(0, -6, 0), head_d=(0, -10, 0)); p["lh"] = (0.28, 0.05, 0.95); p["rh"] = (-0.28, 0.05, 0.95)
        keys = [(0.2, p), (0.8, p)]
    elif kind == "touch":
        p = P(base); p["lh"] = (0.03, 0.50, 1.32); p["rh"] = (-0.03, 0.50, 1.32); keys = [(0.3, p), (0.7, p)]
    elif kind == "flex":
        p = P(base, chest_d=(0, -10, 0)); p["lh"] = (0.32, 0.02, 1.68); p["rh"] = (-0.32, 0.02, 1.68)
        p["lh_pole"] = (0.4, 0, -1); p["rh_pole"] = (-0.4, 0, -1); keys = [(0.25, p), (0.8, p)]
    elif kind == "bow":
        p = P(base, chest_d=(0, 35, 0), head_d=(0, 15, 0)); p["lh"] = (0.02, 0.28, 1.30); p["rh"] = (-0.02, 0.28, 1.32)
        keys = [(0.3, p), (0.7, p)]
    elif kind == "bounce":
        for i, fr in enumerate((0.15, 0.3, 0.45, 0.6, 0.75)):
            keys.append((fr, P(base, hips_d=(0, 0, 0.05 if i % 2 == 0 else -0.03))))
    elif kind == "kiai":
        p = P(base, chest_d=(0, -6, 0), head_d=(0, -8, 0)); p["lh"] = (0.16, -0.02, 1.08); p["rh"] = (-0.16, -0.02, 1.08)
        keys = [(0.25, p), (0.75, p)]
    return keys

def ground(base, kind):
    if kind == "stomp":
        up = P(base); up["rf"] = (-0.08, 0.42, 0.50)
        dn = P(base, hips_d=(0, 0.06, -0.04), chest_d=(0, 12, 0)); dn["rf"] = (-0.05, 0.55, 0.09)
        return [(0.6, up), (1.0, dn)]
    if kind == "kneel_punch":
        imp = P(base, hips_d=(0, 0.20, -0.45), chest_d=(0, 40, 0)); imp["rh"] = (-0.02, 0.62, 0.22); imp["lf"] = (0.15, 0.40, 0.09)
        imp["rf"] = (-0.12, -0.25, 0.12); return [(0.5, P(base, hips_d=(0, 0.1, -0.2))), (1.0, imp)]
    if kind == "elbow_drop":
        imp = P(base, hips_d=(0, 0.35, -0.70), chest_d=(0, 75, 0)); imp["rh"] = (-0.05, 0.40, 0.60); imp["rh_pole"] = (0, 0, -1)
        return [(0.5, P(base, hips_d=(0, 0, 0.1))), (1.0, imp)]
    if kind == "armbar":
        imp = P(base, hips_d=(0, 0.25, -0.70), chest_d=(0, -40, 0)); imp["lh"] = (0.06, 0.32, 0.65); imp["rh"] = (-0.06, 0.32, 0.65)
        imp["lf"] = (0.10, 0.70, 0.35); imp["rf"] = (-0.10, 0.70, 0.40); return [(0.5, P(base, hips_d=(0, 0.1, -0.3))), (1.0, imp)]
    if kind == "axe":
        up = P(base, chest_d=(0, -12, 0)); up["lf"] = (0.05, 0.42, 1.70); up["lf_dir"] = (0, 0.2, 1)
        dn = P(base, chest_d=(0, 15, 0)); dn["lf"] = (0.05, 0.62, 0.25); dn["lf_dir"] = (0, 0.3, -1)
        return [(0.6, up), (1.0, dn)]
    return [(1.0, base)]

def flurry(base, hits=6, height=1.48):
    keys = []
    for i in range(hits):
        side = 1 if i % 2 == 0 else -1
        p = P(base, chest_d=(side * 18, 5, 0), hips_d=(0, 0.04, 0))
        p[hand_key(side)] = (side * 0.03, 0.70, height - 0.05 * (i % 3)); p[hand_key(-side)] = GUARD_CHIN[-side]
        keys.append(((i + 1) / hits, p))
    return keys

def combo(*parts):
    """Chains templates: each part (fraction_span, keys) is squeezed into its slice of the startup."""
    out = []; start = 0.0
    for span, keys in parts:
        for t, p in keys:
            if isinstance(t, str): continue
            out.append((start + span * t, p))
        start += span
    out[-1] = (1.0, out[-1][1])
    return out

# ----------------------------------------------------------------------------------------------- move -> template
def T(fn, **kw): return (fn, kw)

TEMPLATES = {
    # street
    "street.jab": T(straight, side=1), "street.cross": T(straight, side=-1), "street.haymaker": T(overhand, side=-1),
    "street.hook": T(hook, side=1), "street.front_kick": T(front_kick, side=-1), "street.roundhouse": T(round_kick, side=-1),
    "street.clinch": T(grab), "street.clinch_knee": T(clinch_strike, kind="knee"), "street.hip_toss": T(throw, kind="hip"),
    "street.wall_slam": T(throw, kind="drive"), "street.reversal_suplex": T(throw, kind="suplex"),
    "street.counter_hook": T(counter, then=lambda b: hook(b, side=1)), "street.uppercut": T(uppercut, side=-1),
    "street.southside_signature": T(lambda b: combo((0.35, hook(b, side=1)), (0.65, overhand(b, side=-1)))),
    "street.stomp": T(ground, kind="stomp"), "street.taunt_come_on": T(taunt, kind="beckon"),
    "street.finisher_send_off": T(lambda b: combo((0.25, straight(b, 1)), (0.25, straight(b, -1)), (0.2, hook(b, 1)), (0.3, overhand(b, -1)))),
    # boxing
    "boxing.jab": T(straight, side=1, reach=0.82), "boxing.cross": T(straight, side=-1, reach=0.84), "boxing.body_shot": T(body_shot, side=-1),
    "boxing.lead_hook": T(hook, side=1), "boxing.uppercut": T(uppercut, side=-1), "boxing.overhand": T(overhand, side=-1),
    "boxing.flurry": T(flurry, hits=6), "boxing.tie_up": T(grab, height=1.45, reach=0.40), "boxing.short_uppercut": T(clinch_strike, kind="uppercut"),
    "boxing.shove_off": T(throw, kind="shove"), "boxing.corner_pin": T(throw, kind="drive"),
    "boxing.pull_counter": T(counter, then=lambda b: straight(P(b, chest_d=(0, -12, 0)), side=-1)), "boxing.spin_out": T(throw, kind="reversal"),
    "boxing.gloves_down": T(taunt, kind="gloves_down"),
    "boxing.closer": T(lambda b: combo((0.3, flurry(b, hits=4)), (0.3, hook(b, 1)), (0.4, uppercut(b, -1, height=1.70)))),
    # kickboxing
    "kickboxing.jab": T(straight, side=1), "kickboxing.cross": T(straight, side=-1), "kickboxing.hook": T(hook, side=1),
    "kickboxing.low_kick": T(round_kick, side=-1, low=True, yaw=50), "kickboxing.body_kick": T(round_kick, side=-1, height=1.05),
    "kickboxing.spinning_back_fist": T(spin_strike, side=-1), "kickboxing.head_kick": T(round_kick, side=-1, height=1.62, yaw=85),
    "kickboxing.clinch": T(grab), "kickboxing.clinch_knee": T(clinch_strike, kind="knee"), "kickboxing.catch_sweep": T(sweep, side=1),
    "kickboxing.rope_fling": T(throw, kind="shove"), "kickboxing.check_hook": T(counter, then=lambda b: hook(b, side=1)),
    "kickboxing.pivot_out": T(throw, kind="reversal"), "kickboxing.glove_tap": T(taunt, kind="touch"),
    "kickboxing.dutch_finish": T(lambda b: combo((0.2, straight(b, 1)), (0.2, straight(b, -1)), (0.2, hook(b, 1)), (0.4, round_kick(b, -1, height=1.62, yaw=85)))),
    # muay thai
    "muay_thai.teep": T(front_kick, side=1, push=True, height=1.05), "muay_thai.jab": T(straight, side=1), "muay_thai.cross": T(straight, side=-1),
    "muay_thai.elbow": T(elbow_strike, side=-1), "muay_thai.roundhouse": T(round_kick, side=-1, height=1.20, yaw=80),
    "muay_thai.flying_knee": T(knee, side=-1, flying=True), "muay_thai.plum": T(grab, height=1.58, reach=0.42),
    "muay_thai.knee": T(clinch_strike, kind="knee"), "muay_thai.dump": T(throw, kind="dump"), "muay_thai.pin_knee": T(throw, kind="drive"),
    "muay_thai.catch_kick": T(counter, then=lambda b: round_kick(b, side=-1, low=True)), "muay_thai.swim_out": T(throw, kind="reversal"),
    "muay_thai.wai_khru": T(taunt, kind="bow"),
    "muay_thai.spinning_elbow": T(lambda b: combo((0.35, elbow_strike(b, 1)), (0.65, spin_strike(b, -1, elbow=True)))),
    # karate
    "karate.kizami": T(straight, side=1, reach=0.88), "karate.gyaku": T(straight, side=-1, reach=0.86, height=1.45),
    "karate.oi_zuki": T(straight, side=-1, lunge=0.35, height=1.50), "karate.mae_geri": T(front_kick, side=-1, height=1.05),
    "karate.shuto": T(hook, side=1, height=1.58), "karate.mawashi": T(round_kick, side=-1, height=1.45, yaw=80),
    "karate.ushiro": T(spin_kick, side=-1, back=True, height=1.05), "karate.kuzushi": T(grab, height=1.35),
    "karate.hiza": T(clinch_strike, kind="knee"), "karate.ashi_barai": T(sweep, side=1), "karate.age_uke": T(counter, then=lambda b: straight(b, side=-1)),
    "karate.tai_sabaki": T(throw, kind="reversal"), "karate.kakato": T(ground, kind="axe"), "karate.kiai": T(taunt, kind="kiai"),
    "karate.ikken": T(lambda b: combo((0.6, [(1.0, P(b, hips_d=(0, -0.05, -0.05)))]), (0.4, straight(b, side=-1, lunge=0.40, height=1.48)))),
    # taekwondo
    "taekwondo.cut_kick": T(front_kick, side=1, height=0.85, reach=0.80), "taekwondo.back_fist": T(spin_strike, side=1),
    "taekwondo.dollyo": T(round_kick, side=-1, height=1.35, yaw=85), "taekwondo.yeop": T(side_kick, side=1, height=1.10),
    "taekwondo.dwi": T(spin_kick, side=-1, back=True, height=1.15), "taekwondo.tornado": T(spin_kick, side=1, height=1.65),
    "taekwondo.collar_grab": T(grab, height=1.48), "taekwondo.short_knee": T(clinch_strike, kind="knee"),
    "taekwondo.shove_hook": T(throw, kind="shove"), "taekwondo.step_back_kick": T(counter, then=lambda b: spin_kick(b, side=-1, back=True)),
    "taekwondo.spin_away": T(throw, kind="reversal"), "taekwondo.axe_kick": T(ground, kind="axe"), "taekwondo.bounce": T(taunt, kind="bounce"),
    "taekwondo.five_forty": T(spin_kick, side=1, height=1.75, degrees=720),
    # kung fu
    "kung_fu.chain_punch": T(flurry, hits=3, height=1.42), "kung_fu.flurry": T(flurry, hits=5, height=1.40),
    "kung_fu.snap_kick": T(front_kick, side=1, height=0.55, reach=0.80), "kung_fu.palm": T(straight, side=-1, palm=True, height=1.45),
    "kung_fu.tiger_claw": T(overhand, side=-1), "kung_fu.crescent": T(round_kick, side=-1, height=1.55, yaw=40),
    "kung_fu.dragon_tail": T(spin_kick, side=-1, low=True, degrees=360), "kung_fu.seize_wrist": T(grab, height=1.30, reach=0.52),
    "kung_fu.elbow_chain": T(clinch_strike, kind="elbow"), "kung_fu.lock_takedown": T(throw, kind="lock"),
    "kung_fu.redirect_wall": T(throw, kind="drive"), "kung_fu.deflect": T(counter, then=lambda b: straight(b, side=-1, palm=True)),
    "kung_fu.wrist_turn": T(throw, kind="reversal"), "kung_fu.falling_palm": T(ground, kind="kneel_punch"), "kung_fu.beckon": T(taunt, kind="beckon"),
    "kung_fu.hundred_fists": T(flurry, hits=12, height=1.45),
    # wrestling
    "wrestling.shiver": T(straight, side=1, reach=0.60, height=1.50), "wrestling.chop": T(hook, side=-1, height=1.40),
    "wrestling.elbow_smash": T(elbow_strike, side=-1, vertical=True), "wrestling.big_boot": T(front_kick, side=-1, height=1.45, push=True),
    "wrestling.lariat": T(lambda b: [(0.5, P(b, hips_d=(0, 0.2, 0))), (1.0, P(hook(b, side=-1, height=1.55)[1][1], hips_d=(0, 0.35, 0)))]),
    "wrestling.double_leg": T(shoot), "wrestling.headbutt": T(clinch_strike, kind="headbutt"), "wrestling.suplex": T(throw, kind="suplex"),
    "wrestling.wall_powerbomb": T(throw, kind="powerbomb"), "wrestling.duck_under": T(counter, then=lambda b: grab(P(b, hips_d=(0, 0, -0.2)), height=1.2)),
    "wrestling.switch": T(throw, kind="reversal"), "wrestling.elbow_drop": T(ground, kind="elbow_drop"), "wrestling.flex": T(taunt, kind="flex"),
    "wrestling.powerbomb": T(throw, kind="powerbomb"),
    # judo
    "judo.grip_jab": T(straight, side=1, reach=0.70), "judo.palm_push": T(straight, side=-1, palm=True, reach=0.70, height=1.40),
    "judo.atemi": T(straight, side=-1, height=1.50), "judo.de_ashi": T(sweep, side=1, grab=False), "judo.kouchi": T(sweep, side=-1),
    "judo.kumikata": T(grab, height=1.42, reach=0.45), "judo.collar_choke": T(clinch_strike, kind="choke"), "judo.seoi_nage": T(throw, kind="seoi"),
    "judo.osoto_wall": T(throw, kind="drive"), "judo.kaeshi": T(counter, then=lambda b: throw(b, kind="reversal")),
    "judo.tomoe_nage": T(throw, kind="sacrifice"), "judo.armbar": T(ground, kind="armbar"), "judo.fix_grip": T(taunt, kind="bow"),
    "judo.rear_choke": T(lambda b: combo((0.5, throw(b, kind="reversal")), (0.5, clinch_strike(b, kind="choke")))),
    # mma
    "mma.jab": T(straight, side=1), "mma.cross": T(straight, side=-1), "mma.hook": T(hook, side=1), "mma.overhand": T(overhand, side=-1),
    "mma.leg_kick": T(round_kick, side=-1, low=True, yaw=45), "mma.head_kick": T(round_kick, side=-1, height=1.62, yaw=85),
    "mma.superman": T(lambda b: combo((0.5, front_kick(b, side=-1, height=0.3, reach=0.2)), (0.5, straight(b, side=-1, lunge=0.3)))),
    "mma.single_leg": T(shoot, single=True), "mma.dirty_uppercut": T(clinch_strike, kind="uppercut"), "mma.body_lock": T(throw, kind="dump"),
    "mma.cage_slam": T(throw, kind="drive"), "mma.counter_cross": T(counter, then=lambda b: straight(b, side=-1)),
    "mma.whizzer": T(throw, kind="reversal"), "mma.ground_pound": T(ground, kind="kneel_punch"), "mma.touch_gloves": T(taunt, kind="touch"),
    "mma.ground_finale": T(lambda b: combo((0.4, shoot(b)), (0.6, ground(b, kind="kneel_punch")))),
    # hybrids
    "southside_hands.double_hook": T(lambda b: combo((0.45, hook(b, side=1, body=True)), (0.55, hook(b, side=1)))),
    "southside_hands.shoulder_roll": T(counter, then=lambda b: straight(P(b, chest_d=(20, 0, 0)), side=-1)),
    "southside_hands.block_party": T(lambda b: combo((0.6, flurry(b, hits=4)), (0.4, uppercut(b, -1)))),
    "southside_hands.last_call": T(lambda b: combo((0.3, hook(b, 1, body=True)), (0.3, hook(b, 1)), (0.4, overhand(b, -1)))),
    "dirty_boxing.collar_tie": T(grab, height=1.62, reach=0.36), "dirty_boxing.tie_uppercut": T(clinch_strike, kind="uppercut"),
    "dirty_boxing.uppercut_suplex": T(lambda b: combo((0.4, uppercut(b, -1)), (0.6, throw(b, kind="suplex")))),
    "dirty_boxing.uppercut_lariat": T(lambda b: combo((0.4, uppercut(b, -1)), (0.6, hook(b, side=-1, height=1.55)))),
    "dirty_boxing.bell_ringer": T(lambda b: combo((0.25, uppercut(b, -1)), (0.25, uppercut(b, 1)), (0.5, throw(b, kind="suplex")))),
    "clinch_king.knee_barrage": T(clinch_strike, kind="knee"), "clinch_king.knee_lift": T(lambda b: combo((0.5, clinch_strike(b, "knee")), (0.5, throw(b, kind="suplex")))),
    "clinch_king.skip_knee": T(knee, side=-1), "clinch_king.knee_powerbomb": T(lambda b: combo((0.4, knee(b, -1, clinch=True)), (0.6, throw(b, kind="powerbomb")))),
    "tricking.blitz": T(straight, side=-1, lunge=0.25), "tricking.spinning_hook": T(spin_kick, side=-1, height=1.55),
    "tricking.butterfly": T(spin_kick, side=1, height=1.45, degrees=360), "tricking.double_spin": T(spin_kick, side=-1, height=1.70, degrees=720),
    "flowing_water.redirect_grip": T(grab, height=1.35, reach=0.50), "flowing_water.sleeve_strike": T(clinch_strike, kind="elbow"),
    "flowing_water.spiral_throw": T(throw, kind="spiral"), "flowing_water.yielding": T(counter, then=lambda b: straight(b, side=-1, palm=True)),
    "flowing_water.tide": T(lambda b: combo((0.4, flurry(b, hits=4)), (0.6, throw(b, kind="spiral")))),
    "cage_pressure.switch_kick": T(round_kick, side=1, height=1.20, yaw=75), "cage_pressure.cage_cut": T(lambda b: combo((0.4, hook(b, 1)), (0.6, round_kick(b, -1, height=1.2)))),
    "cage_pressure.cage_closer": T(lambda b: combo((0.3, round_kick(b, 1, height=1.2)), (0.3, hook(b, 1)), (0.4, overhand(b, -1)))),
}

REACTIONS = {  # name: (frames, builder(base) -> keys at absolute fractions)
    "react.hit_head_light": (18, lambda b: [(0.25, P(b, chest_d=(0, -10, 0), head_d=(8, -20, 0), hips_d=(0, -0.05, 0)))]),
    "react.hit_head_heavy": (26, lambda b: [(0.25, P(b, chest_d=(-15, -18, 8), head_d=(25, -30, 10), hips_d=(0, -0.12, 0)))]),
    "react.hit_body": (22, lambda b: [(0.3, P(b, chest_d=(0, 28, 0), head_d=(0, 10, 0), hips_d=(0, -0.08, -0.06)))]),
    "react.block_impact": (10, lambda b: [(0.3, P(b, chest_d=(0, -6, 0), hips_d=(0, -0.05, -0.02)))]),
    "react.guard_break": (75, lambda b: [(0.15, P(b, chest_d=(0, -15, 10), lh=(0.28, 0.0, 1.05), rh=(-0.28, 0.0, 1.05))),
                                         (0.5, P(b, chest_d=(10, 5, -8), head_d=(0, 15, 10), lh=(0.25, 0.1, 1.0), rh=(-0.25, 0.1, 1.0))),
                                         (0.85, P(b, chest_d=(-5, 0, 6), lh=(0.25, 0.1, 1.05), rh=(-0.25, 0.1, 1.05)))]),
    "react.knockdown": (70, lambda b: [(0.12, P(b, chest_d=(0, -30, 0), hips_d=(0, -0.2, -0.2))),
                                       (0.3, P(b, hips_d=(0, -0.6, -0.84), hips_rot_d=(0, -80, 0), chest_d=(0, 0, 0), lh=(0.40, -0.5, 0.15), rh=(-0.40, -0.5, 0.15),
                                               lf=(0.12, 0.35, 0.09), rf=(-0.12, 0.30, 0.09))),
                                       (1.0, P(b, hips_d=(0, -0.6, -0.84), hips_rot_d=(0, -82, 0), lh=(0.45, -0.55, 0.12), rh=(-0.45, -0.55, 0.12),
                                               lf=(0.14, 0.38, 0.09), rf=(-0.14, 0.36, 0.09)))]),
    "react.get_up": (24, lambda b: [(0.0, P(b, hips_d=(0, -0.6, -0.84), hips_rot_d=(0, -82, 0), lh=(0.45, -0.55, 0.12), rh=(-0.45, -0.55, 0.12),
                                            lf=(0.14, 0.38, 0.09), rf=(-0.14, 0.36, 0.09))),
                                    (0.5, P(b, hips_d=(0, -0.1, -0.5), chest_d=(0, 40, 0), lh=(0.2, 0.2, 0.15), rf=(-0.12, -0.2, 0.09)))]),
    "react.dodge_back": (20, lambda b: [(0.3, P(b, hips_d=(0, -0.35, -0.03), chest_d=(0, -18, 0))), (0.6, P(b, hips_d=(0, -0.40, -0.02), chest_d=(0, -10, 0)))]),
    "react.slip": (14, lambda b: [(0.4, P(b, hips_d=(0.10, 0, -0.08), chest_d=(10, 12, 20), head_d=(0, 0, 10)))]),
    "react.grappled": (60, lambda b: [(0.2, P(b, chest_d=(0, 12, 0), lh=(0.10, 0.30, 1.35), rh=(-0.10, 0.30, 1.35))),
                                      (0.6, P(b, chest_d=(4, 16, -6), lh=(0.12, 0.30, 1.30), rh=(-0.08, 0.32, 1.38)))]),
    "react.thrown": (40, lambda b: [(0.25, P(b, hips_d=(0, 0.2, 0.25), hips_rot_d=(0, 60, 0))), (0.6, P(b, hips_d=(0, 0.6, -0.5), hips_rot_d=(0, 150, 0))),
                                    (1.0, P(b, hips_d=(0, 0.9, -0.84), hips_rot_d=(0, 95, 0), lh=(0.45, 1.4, 0.12), rh=(-0.45, 1.4, 0.12),
                                            lf=(0.14, 0.1, 0.09), rf=(-0.14, 0.15, 0.09)))]),
    "react.stunned": (90, lambda b: [(0.25, P(b, chest_d=(10, 6, 8), head_d=(0, 15, 12))), (0.5, P(b, chest_d=(-10, 6, -8), head_d=(0, 10, -12))),
                                     (0.75, P(b, chest_d=(10, 6, 8), head_d=(0, 15, 12)))]),
    "react.defeated": (60, lambda b: [(0.4, P(b, hips_d=(0, -0.5, -0.84), hips_rot_d=(0, -86, 0), lh=(0.55, -0.4, 0.10), rh=(-0.50, -0.6, 0.10),
                                              lf=(0.18, 0.40, 0.09), rf=(-0.10, 0.36, 0.09))),
                                      (1.0, P(b, hips_d=(0, -0.5, -0.86), hips_rot_d=(0, -88, 0), lh=(0.55, -0.4, 0.10), rh=(-0.50, -0.6, 0.10),
                                              lf=(0.18, 0.40, 0.09), rf=(-0.10, 0.36, 0.09)))]),
}

# ----------------------------------------------------------------------------------------------- action building
def new_action(rig, name, frames):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    try:
        act.use_frame_range = True; act.frame_start = 0; act.frame_end = max(1, frames)
    except Exception:
        pass
    return act

def bake_keys(rig, name, frames, keys, base, start=None, end=None):
    """keys: list of (frame, pose). The base stance (or start/end overrides) is keyed at 0 and at the last frame."""
    act = new_action(rig, name, frames)
    prev = {}
    all_keys = [(0, start or base)] + sorted(keys, key=lambda k: k[0]) + [(frames, end or base)]
    used = set()
    for f, p in all_keys:
        f = int(round(f))
        if f in used: f += 1
        used.add(f)
        apply_pose(rig, p, f, prev)
    return act

def move_keys(move, base, keys):
    """Maps template fractions onto ticks: 1.0 = first active frame; 'hold' keys land at the end of active,
    'recover' keys in the first third of recovery."""
    su = move["Startup"]; ac = move["Active"]; rc = move["Recovery"]
    out = []
    for t, p in keys:
        if isinstance(t, str):
            if t.startswith("hold"): out.append((su + ac, p))
            else:
                frac = float(t[7:]) if len(t) > 7 else 0.5
                out.append((su + ac + max(1, rc * (0.15 + 0.45 * frac)), p))
        else:
            out.append((max(1, su * t), p))
    # hold the impact pose through the active frames
    imp = [p for t, p in keys if t == 1.0]
    if imp and ac > 1: out.append((su + ac - 1 if su + ac - 1 > su else su + 1, imp[-1]))
    return out

def build_all(rig, styles):
    report = []
    scene = bpy.context.scene
    scene.render.fps = FPS
    for prefix, st in STANCES.items():
        base = stance_pose(st)
        # idle loop: 2 seconds, keyed every 10 frames
        frames = 120
        keys = [(f, stance_pose(st, f / FPS)) for f in range(10, frames, 10)]
        bake_keys(rig, "anim.%s.stance_idle" % prefix, frames, keys, base)
        g = P(base, chest_d=(0, 6, 0), hips_d=(0, 0, -0.02)); g["lh"] = (0.08, 0.16, 1.58); g["rh"] = (-0.08, 0.14, 1.58)
        bake_keys(rig, "anim.%s.stance_guard" % prefix, 30, [(6, g), (24, g)], base)
        # shuffle forward / back (lead foot then rear foot), 30 frames, ends where it began relative to the hips
        sf = P(base, hips_d=(0, 0.12, 0)); sf["lf"] = (sf["lf"][0], sf["lf"][1] + 0.22, 0.12)
        bake_keys(rig, "anim.%s.shuffle_forward" % prefix, 30, [(8, sf), (16, P(base, hips_d=(0, 0.2, 0), lf=(base["lf"][0], base["lf"][1] + 0.22, 0.09))),
                                                                 (24, P(base, hips_d=(0, 0.22, 0), lf=(base["lf"][0], base["lf"][1] + 0.22, 0.09),
                                                                        rf=(base["rf"][0], base["rf"][1] + 0.22, 0.12)))], base)
        sb = P(base, hips_d=(0, -0.12, 0)); sb["rf"] = (sb["rf"][0], sb["rf"][1] - 0.22, 0.12)
        bake_keys(rig, "anim.%s.shuffle_back" % prefix, 30, [(8, sb), (16, P(base, hips_d=(0, -0.2, 0), rf=(base["rf"][0], base["rf"][1] - 0.22, 0.09))),
                                                             (24, P(base, hips_d=(0, -0.22, 0), rf=(base["rf"][0], base["rf"][1] - 0.22, 0.09),
                                                                    lf=(base["lf"][0], base["lf"][1] - 0.22, 0.12)))], base)
    missing = []
    for s in styles:
        for m in s["Moves"]:
            key = m["AnimationKey"]; mid = m["Id"]
            prefix = mid.split(".")[0]
            st = STANCES.get(prefix) or STANCES["street"]
            base = stance_pose(st)
            tpl = TEMPLATES.get(mid)
            if tpl is None:
                missing.append(mid); continue
            fn, kw = tpl
            keys = fn(base, **kw)
            if m["Kind"] == "ClinchStrike":
                # Clinch strikes resolve instantly in the simulation and repeat on the 20-tick clinch cooldown,
                # so the clip fills that cooldown with the impact on tick 6.
                m = dict(m, Startup=6, Active=2, Recovery=12)
            total = m["Startup"] + m["Active"] + m["Recovery"]
            if m["Kind"] == "Taunt":
                abs_keys = [(max(1, total * t), p) for t, p in keys]
            else:
                abs_keys = move_keys(m, base, keys)
            bake_keys(rig, key, total, abs_keys, base)
            report.append("%s %d frames impact@%d" % (key, total, m["Startup"]))
    neutral = stance_pose(STANCES["street"])
    for name, (frames, fn) in REACTIONS.items():
        raw = fn(neutral)
        start = raw[0][1] if raw[0][0] == 0 else None
        lying = name in ("react.knockdown", "react.defeated", "react.thrown")
        end = raw[-1][1] if lying else None
        mid = [(max(1, frames * t), p) for t, p in raw if 0 < t < 1.0 or (t == 1.0 and not lying)]
        bake_keys(rig, "anim." + name, frames, mid, neutral, start=start, end=end)
    rig.animation_data.action = None
    return report, missing

def load_styles():
    styles = []
    for f in STYLE_FILES:
        with urllib.request.urlopen(RAW + "Assets/_Project/Data/Moves/%s.json" % f) as r:
            styles += json.loads(r.read().decode("utf-8"))["Styles"]
    return styles

def main():
    os.makedirs(os.path.join(ROOT, "blend"), exist_ok=True)
    for o in list(bpy.data.objects):
        if o.name.startswith("FFTS_"): bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.name.startswith("anim."): bpy.data.actions.remove(a)
    rig = build_rig()
    for b in rig.data.bones: REST_Q[b.name] = b.matrix_local.to_quaternion()
    man = build_mannequin(rig)
    styles = load_styles()
    report, missing = build_all(rig, styles)
    rig.pose.bones["Hips"].location = (0, 0, 0)
    for pb in rig.pose.bones: pb.rotation_quaternion = Quaternion()
    bpy.context.scene.frame_set(0)
    path = os.path.join(ROOT, "blend", "10_fight_rig.blend")
    bpy.ops.wm.save_as_mainfile(filepath=path)
    summary = "styles=%d actions=%d missing_templates=%s\nsaved %s" % (
        len(styles), len([a for a in bpy.data.actions if a.name.startswith("anim.")]), missing, path)
    with open(os.path.join(ROOT, "fight_rig_report.txt"), "w") as fh: fh.write(summary + "\n" + "\n".join(report))
    t = bpy.data.texts.get("FFTS_RIG_REPORT") or bpy.data.texts.new("FFTS_RIG_REPORT")
    t.clear(); t.write(summary + "\n" + "\n".join(report))
    for a in bpy.context.screen.areas:
        if a.type == 'TEXT_EDITOR': a.spaces[0].text = t; a.spaces[0].show_word_wrap = True; a.spaces[0].top = 0
    return summary

if __name__ == "__main__":
    print(main())
