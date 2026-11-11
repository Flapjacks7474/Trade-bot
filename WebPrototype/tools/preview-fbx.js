import * as THREE from 'three';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
window.preview = async (url) => {
  const r = new THREE.WebGLRenderer({ antialias: true }); r.setSize(800, 800); document.body.appendChild(r.domElement);
  r.outputColorSpace = THREE.SRGBColorSpace;
  const s = new THREE.Scene(); s.background = new THREE.Color(0x2a2d33);
  const obj = await new FBXLoader().loadAsync(url);
  let info = {};
  obj.traverse((o) => { if (o.isMesh) { info = { attrs: Object.keys(o.geometry.attributes) }; if (o.geometry.attributes.color) o.material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.8 }); else o.material = new THREE.MeshStandardMaterial({ color: 0xbbbbbb, roughness: 0.7 }); } });
  const box = new THREE.Box3().setFromObject(obj), c = box.getCenter(new THREE.Vector3()), h = box.getSize(new THREE.Vector3()).y;
  obj.position.sub(c); s.add(obj);
  s.add(new THREE.HemisphereLight(0xffffff, 0x444444, 1.2)); const d = new THREE.DirectionalLight(0xffffff, 2); d.position.set(1, 2, 3); s.add(d);
  const cam = new THREE.PerspectiveCamera(35, 1, 1, 5000); cam.position.set(0, 0, h * 1.9); cam.lookAt(0, 0, 0);
  r.render(s, cam); const shots = [r.domElement.toDataURL()];
  obj.rotation.y = Math.PI / 2; r.render(s, cam); shots.push(r.domElement.toDataURL());
  return { info, shots };
};
