// Renderer, physical sky, image-based lighting, post-processing (bloom + AA), quality presets and time-of-day lighting.
import * as THREE from 'three';
import { Sky } from 'three/examples/jsm/objects/Sky.js';
import { EXRLoader } from 'three/examples/jsm/loaders/EXRLoader.js';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { nightMaterials } from './world3d.js';

export const QUALITY = {
  high: { pixelRatio: 2, shadows: 2048, bloom: true, label: 'High' },
  medium: { pixelRatio: 1.5, shadows: 1024, bloom: true, label: 'Medium' },
  low: { pixelRatio: 1, shadows: 0, bloom: false, label: 'Low' }
};

export function createGraphics(container, quality) {
  const renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: 'high-performance' });
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 0.9;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  container.appendChild(renderer.domElement);
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(58, 1, 0.1, 1200);

  const sky = new Sky(); sky.scale.setScalar(4000); scene.add(sky);
  const su = sky.material.uniforms; su.turbidity.value = 3.5; su.rayleigh.value = 1.1; su.mieCoefficient.value = 0.0025; su.mieDirectionalG.value = 0.8;
  const hemi = new THREE.HemisphereLight(0xbcd3ff, 0x4a4036, 0.35); scene.add(hemi);
  const sun = new THREE.DirectionalLight(0xfff0dc, 2.6); sun.castShadow = true;
  Object.assign(sun.shadow.camera, { left: -55, right: 55, top: 55, bottom: -55, near: 1, far: 260 }); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03;
  scene.add(sun, sun.target);
  scene.fog = new THREE.FogExp2(0xb7c6d4, 0.0013);

  // Image-based lighting from the CC0 Poly Haven map (falls back to plain lights if it can't load).
  const pmrem = new THREE.PMREMGenerator(renderer);
  let envTex = null;
  new EXRLoader().load('assets/env/city.exr', (t) => { t.mapping = THREE.EquirectangularReflectionMapping; envTex = pmrem.fromEquirectangular(t).texture; scene.environment = envTex; t.dispose(); }, undefined, () => {});

  const composer = new EffectComposer(renderer, new THREE.WebGLRenderTarget(1, 1, { type: THREE.HalfFloatType, samples: 4 }));
  composer.addPass(new RenderPass(scene, camera));
  const bloom = new UnrealBloomPass(new THREE.Vector2(256, 256), 0.35, 0.55, 0.88); composer.addPass(bloom);
  composer.addPass(new OutputPass());

  const g = { renderer, scene, camera, sun, hemi, sky, composer, bloom, quality: null, dayFactor: 1 };
  g.setQuality = (q) => {
    g.quality = QUALITY[q] ? q : 'medium'; const Q = QUALITY[g.quality];
    renderer.setPixelRatio(Math.min(devicePixelRatio, Q.pixelRatio));
    renderer.shadowMap.enabled = Q.shadows > 0; sun.castShadow = Q.shadows > 0;
    if (Q.shadows) { sun.shadow.mapSize.set(Q.shadows, Q.shadows); sun.shadow.map?.dispose(); sun.shadow.map = null; }
    bloom.enabled = Q.bloom; g.resize();
    scene.traverse((o) => { if (o.material) [].concat(o.material).forEach((m) => (m.needsUpdate = true)); });
  };
  g.resize = () => {
    const w = container.clientWidth, h = container.clientHeight;
    renderer.setSize(w, h); composer.setSize(w, h); composer.setPixelRatio(renderer.getPixelRatio());
    bloom.resolution.set(w / 2, h / 2); camera.aspect = w / h; camera.updateProjectionMatrix();
  };
  addEventListener('resize', g.resize);
  g.setQuality(quality);

  const sunDir = new THREE.Vector3(), night = new THREE.Color(0x0b1120), dusk = new THREE.Color(0xd98a5a), day = new THREE.Color(0xb7c6d4);
  /** Moves the sun with the in-game hour and blends sky, fog, light, exposure, bloom and emissive night lighting. */
  g.setTime = (hour, focusX, focusZ) => {
    const t = (hour - 6) / 14; // sunrise ~6:00, sunset ~20:00 (Southern summer)
    const elev = Math.sin(Math.PI * t) * 64, az = 100 + t * 160; // degrees
    const phi = THREE.MathUtils.degToRad(90 - elev), theta = THREE.MathUtils.degToRad(az);
    sunDir.setFromSphericalCoords(1, phi, theta);
    su.sunPosition.value.copy(sunDir);
    const dayF = THREE.MathUtils.clamp((elev + 4) / 18, 0, 1); g.dayFactor = dayF;
    const duskF = THREE.MathUtils.clamp(1 - Math.abs(elev - 6) / 12, 0, 1) * (elev > -6 ? 1 : 0);
    sky.visible = dayF > 0.02 || duskF > 0.05;
    scene.background = dayF < 0.05 ? night.clone().lerp(dusk, duskF * 0.3) : null;
    scene.fog.color.copy(night).lerp(day, dayF).lerp(dusk, duskF * 0.5);
    scene.fog.density = 0.0013 + (1 - dayF) * 0.0015;
    const lightDir = elev > 2 ? sunDir : new THREE.Vector3(-0.3, 0.8, 0.4).normalize(); // moonlight at night
    sun.position.set(focusX + lightDir.x * 120, lightDir.y * 120, focusZ + lightDir.z * 120); sun.target.position.set(focusX, 0, focusZ);
    sun.intensity = 0.55 + dayF * 2.4; sun.color.set(dayF > 0.3 ? 0xfff0dc : 0x9fb4ff).lerp(new THREE.Color(0xffb27a), duskF * 0.7);
    hemi.intensity = 0.38 + dayF * 0.2;
    scene.environment = dayF > 0.15 ? envTex : null; // city reflections by day; dark streets at night
    // The physical sky is very bright near the horizon: keep exposure and bloom low while it's visible.
    renderer.toneMappingExposure = sky.visible ? 0.52 + dayF * 0.2 : 1.25;
    bloom.strength = sky.visible ? 0.18 + (1 - dayF) * 0.15 : 0.85;
    bloom.threshold = sky.visible ? 0.95 : 0.8;
    const lit = 1 - dayF;
    for (const n of nightMaterials) {
      if (n.opacityOnly) n.mat.opacity = n.base * lit;
      else n.mat.emissiveIntensity = Math.max(n.min || 0, n.base * lit);
    }
  };
  g.render = () => (QUALITY[g.quality].bloom ? composer.render() : renderer.render(scene, camera));
  return g;
}
