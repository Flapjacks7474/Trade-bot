import * as THREE from 'three';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
window.inspect = async (url) => {
  const obj = await new FBXLoader().loadAsync(url);
  const out = { meshes: [], bones: [], materials: [], animations: obj.animations.map((a) => ({ name: a.name, dur: a.duration, tracks: a.tracks.length })) };
  obj.traverse((o) => {
    if (o.isMesh) out.meshes.push({ name: o.name, skinned: !!o.isSkinnedMesh, verts: o.geometry.attributes.position.count, mats: [].concat(o.material).map((m) => m.name) });
    if (o.isBone) out.bones.push(o.name);
  });
  obj.traverse((o) => { if (o.isMesh) [].concat(o.material).forEach((m) => out.materials.push({ name: m.name, type: m.type, map: !!m.map, mapImg: m.map?.image ? (m.map.image.width + 'x' + m.map.image.height) : null, normal: !!m.normalMap, color: m.color?.getHexString() })); });
  const box = new THREE.Box3().setFromObject(obj); out.size = box.getSize(new THREE.Vector3()).toArray().map((v) => +v.toFixed(2)); out.min = box.min.toArray().map((v) => +v.toFixed(2));
  return out;
};
