# FFTS Blender pipeline, step 1: fetch source assets and inspect the hero model.
# Run inside Blender (Text Editor > Run Script). Creates the art workspace in Documents\FightForTheSouth_Art.
import bpy, os, urllib.request, json, bmesh

ROOT = os.path.join(os.path.expanduser("~"), "Documents", "FightForTheSouth_Art")
RAW = "https://raw.githubusercontent.com/Flapjacks7474/Trade-bot/claude/fight-for-south-foundation-mgjm04/"
FILES = {
    "source/player.fbx": "WebPrototype/assets/characters/player.fbx",
    "source/Mannequin_Medium.glb": "WebPrototype/assets/characters/Mannequin_Medium.glb",
}
for sub in ("source", "blend", "export", "storyboard", "renders", "backup"):
    os.makedirs(os.path.join(ROOT, sub), exist_ok=True)

log = []
for local, remote in FILES.items():
    dst = os.path.join(ROOT, local)
    if not os.path.exists(dst):
        urllib.request.urlretrieve(RAW + remote, dst)
    log.append("%s  %d bytes" % (local, os.path.getsize(dst)))

# Clean scene
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "source/player.fbx"))

for ob in bpy.context.scene.objects:
    line = "OBJ %s type=%s parent=%s" % (ob.name, ob.type, ob.parent.name if ob.parent else "-")
    if ob.type == 'MESH':
        me = ob.data
        bm = bmesh.new(); bm.from_mesh(me)
        nonmanifold = sum(1 for e in bm.edges if not e.is_manifold)
        loose = sum(1 for v in bm.verts if not v.link_edges)
        islands = 0
        seen = set()
        for v in bm.verts:
            if v.index in seen: continue
            islands += 1
            stack = [v]
            while stack:
                x = stack.pop()
                if x.index in seen: continue
                seen.add(x.index)
                stack.extend(e.other_vert(x) for e in x.link_edges)
        bm.free()
        dims = ob.dimensions
        line += " verts=%d faces=%d tris~%d uv=%d vcol=%d mats=%s dims=(%.3f, %.3f, %.3f) scale=%s nonmanifold_edges=%d loose=%d islands=%d shapekeys=%s groups=%d mods=%s" % (
            len(me.vertices), len(me.polygons), sum(len(p.vertices) - 2 for p in me.polygons), len(me.uv_layers), len(me.color_attributes),
            [m.name if m else None for m in me.materials], dims.x, dims.y, dims.z, tuple(round(s, 4) for s in ob.scale),
            nonmanifold, loose, islands, bool(me.shape_keys), len(ob.vertex_groups), [m.type for m in ob.modifiers])
    if ob.type == 'ARMATURE':
        line += " bones=%d" % len(ob.data.bones)
    log.append(line)
for m in bpy.data.materials:
    nodes = [n.type for n in m.node_tree.nodes] if m.use_nodes and m.node_tree else []
    imgs = [n.image.name for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image] if m.node_tree else []
    log.append("MAT %s nodes=%s images=%s" % (m.name, nodes, imgs))
log.append("ACTIONS %s" % [a.name for a in bpy.data.actions])
log.append("IMAGES %s" % [i.name for i in bpy.data.images])

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blend/00_hero_inspect.blend"))
report = "\n".join(log)
with open(os.path.join(ROOT, "inspect_report.txt"), "w") as f: f.write(report)
t = bpy.data.texts.new("FFTS_REPORT"); t.write(report)
# Show the report in the biggest area
area = max(bpy.context.screen.areas, key=lambda a: a.width * a.height)
area.type = 'TEXT_EDITOR'; area.spaces[0].text = t
area.spaces[0].show_word_wrap = True
