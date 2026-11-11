// Captures screenshots at a given quality and time of day, and reports frame rate (software GL here, so relative only).
import { createRequire } from 'node:module';
const { chromium } = createRequire(process.env.PW_ROOT + '/')('playwright');
const [,, out, quality = 'high', w = '1280', h = '720'] = process.argv;
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: +w, height: +h } });
await page.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
await page.addInitScript((q) => { try { localStorage.setItem('ffts.quality', q); localStorage.removeItem('ffts.save.v1'); } catch {} }, quality);
const errors = []; page.on('pageerror', (e) => errors.push(e.message));
await page.goto(process.env.BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForFunction(() => !document.getElementById('btn-new').disabled, null, { timeout: 120000 });
await page.waitForTimeout(2500);
await page.screenshot({ path: `${out}/${quality}-title.png` });
await page.click('#btn-new');
const fps = () => page.evaluate(() => new Promise((r) => { let n = 0; const t0 = performance.now(); const f = () => { n++; if (performance.now() - t0 < 3000) requestAnimationFrame(f); else r((n / 3).toFixed(1)); }; requestAnimationFrame(f); }));
const shot = async (name, setup, wait = 4000) => { await page.evaluate(setup); await page.waitForTimeout(wait); await page.screenshot({ path: `${out}/${quality}-${name}.png` }); };
await shot('street-morning', () => { const s = __ffts.playerState; s.x = -40; s.z = 6; s.heading = Math.PI / 2; s.yaw = Math.PI / 2; s.pitch = 0.2; __ffts.game.minutes = 9 * 60; });
console.log(quality, 'fps', await fps());
await shot('shops-afternoon', () => { const s = __ffts.playerState; s.x = 15; s.z = -8; s.heading = Math.PI; s.yaw = Math.PI; s.pitch = 0.18; __ffts.game.minutes = 15 * 60 + 30; });
await shot('sunset', () => { const s = __ffts.playerState; s.x = 60; s.z = 8; s.heading = -Math.PI / 2; s.yaw = -Math.PI / 2; s.pitch = 0.15; __ffts.game.minutes = 19 * 60 + 15; });
await shot('night', () => { const s = __ffts.playerState; s.x = 30; s.z = 8; s.heading = Math.PI / 2; s.yaw = Math.PI / 2; s.pitch = 0.2; __ffts.game.minutes = 22 * 60 + 30; });
await shot('grandma', () => { const s = __ffts.playerState; s.x = -60; s.z = 26; s.heading = 0; s.yaw = 0; s.pitch = 0.25; __ffts.game.minutes = 10 * 60; });
// A fight, mid-punch.
await page.evaluate(() => { __ffts.game.minutes = 16 * 60; const s = __ffts.playerState; s.x = -55; s.z = 10; __ffts.startFight('fighter.rell', 'Street'); });
await page.waitForTimeout(2500);
await page.keyboard.press('j'); await page.waitForTimeout(350);
await page.screenshot({ path: `${out}/${quality}-fight.png` });
console.log('errors:', errors.length ? errors : 'none');
await browser.close();
