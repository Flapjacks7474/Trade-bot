// Headless-browser smoke test: boots the built page and drives real gameplay through it.
import { createRequire } from 'node:module';
const { chromium } = createRequire(process.env.PW_ROOT + '/')('playwright');
import { resolve } from 'node:path';
const shots = process.argv[2] || '.';
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
const noFonts = (p) => p.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
await noFonts(page);
// Gameplay test runs on Low graphics: software WebGL here is far slower than any real GPU.
await page.addInitScript(() => { try { if (!sessionStorage.getItem('ffts.smoke')) { localStorage.setItem('ffts.quality', 'low'); localStorage.removeItem('ffts.save.v1'); sessionStorage.setItem('ffts.smoke', '1'); } } catch {} });
const errors = [];
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => { if (m.type() === 'error' && !/fonts\.(googleapis|gstatic)|ERR_|net::/.test(m.text())) errors.push('console: ' + m.text()); });
await page.goto(process.env.BASE_URL || 'file://' + resolve('dist/local.html'), { waitUntil: 'domcontentloaded' });
await page.waitForFunction(() => !document.getElementById('btn-new').disabled, null, { timeout: 60000 });
await page.waitForTimeout(800);
await page.screenshot({ path: shots + '/1-title.png' });
await page.click('#btn-new');
await page.waitForTimeout(800);
const until = async (fn, ms = 60000) => { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await page.evaluate(fn)) return true; await page.waitForTimeout(200); } return false; };
const step = async (label, fn) => { const r = await page.evaluate(fn); console.log(label.padEnd(34), JSON.stringify(r)); return r; };
// Walk to Grandma's house for real (hold W, aim camera).
await step('spawn', () => ({ mode: __ffts.mode, x: Math.round(__ffts.playerState.x), z: Math.round(__ffts.playerState.z), mission: __ffts.game.missions.tracked }));
await page.evaluate(() => { __ffts.playerState.yaw = 0; });
await page.keyboard.down('w'); await page.waitForTimeout(3200); await page.keyboard.up('w');
await step('after walking north', () => ({ z: Math.round(__ffts.playerState.z), objectives: __ffts.game.missions.prog.get('mission.ch1.home_again').done }));
await page.screenshot({ path: shots + '/2-explore.png' });
// Talk to Grandma through the real interaction prompt.
await page.evaluate(() => { const n = __ffts.game.world.npcs.get('npc.grandma'); __ffts.playerState.x = n.x + 1; __ffts.playerState.z = n.z; });
await page.waitForTimeout(400);
await step('interaction prompt', () => ({ current: __ffts.current && __ffts.current.prompt, act: document.getElementById('act-label').textContent, visible: !document.getElementById('act').hidden }));
await page.keyboard.press('e'); await page.waitForTimeout(300);
await page.screenshot({ path: shots + '/3-dialogue.png' });
for (const k of ['1', '1', '2']) { await page.keyboard.press(k); await page.waitForTimeout(150); }
await step('after Grandma', () => ({ overlay: __ffts.overlay, m1: __ffts.game.missions.status('mission.ch1.home_again'), cash: __ffts.game.bank.accounts.cash, tracked: __ffts.game.missions.tracked }));
// Jump story ahead to the Dre spar through the API, then fight with real key presses.
await page.evaluate(() => {
  const g = __ffts.game; const t = (id, ...c) => { const r = g.startDialogue(id); for (const x of c) r.choose(x); };
  t('dialogue.lou.barbershop', 'ask', 'more', 'sharp'); g.trigger('location.southside.corner_store'); g.trigger('location.southside.tone_studio');
  t('dialogue.dre.reunion', 'challenge', 'go'); g.trigger('location.southside.community_gym');
});
await page.evaluate(() => { const n = __ffts.game.world.npcs.get('npc.dre'); __ffts.playerState.x = n.x; __ffts.playerState.z = n.z - 1.5; __ffts.talkTo('npc.dre'); });
await page.waitForTimeout(500);
await step('fight started', () => ({ mode: __ffts.mode, opp: __ffts.fight && __ffts.fight.fighterId, venue: __ffts.fight && __ffts.fight.venue }));
for (let i = 0; i < 400 && await page.evaluate(() => __ffts.mode === 'fight' && !__ffts.fight.sim.isOver); i++) {
  const far = await page.evaluate(() => __ffts.fight.sim.distance > 1.3);
  if (far) { await page.keyboard.down('d'); await page.waitForTimeout(250); await page.keyboard.up('d'); }
  // Wait for the player to be neutral, then throw the L-L-H combo with real key presses.
  await until(() => ['Idle', 'Moving', 'Blocking'].includes(__ffts.fight.sim.A.state) || __ffts.fight.sim.isOver, 3000);
  for (const k of ['j', 'j', 'k']) { await page.keyboard.press(k); await until(() => true, 10); await page.waitForTimeout(260); }
  if (i === 2) await page.screenshot({ path: shots + '/4-fight.png' });
}
const hp = await page.evaluate(() => __ffts.fight && { me: Math.round(__ffts.fight.sim.A.health), dre: Math.round(__ffts.fight.sim.B.health), over: __ffts.fight.sim.isOver, result: __ffts.fight.sim.result });
console.log('fight state'.padEnd(34), JSON.stringify(hp));
await until(() => __ffts.mode === 'explore', 90000);
await step('fight over', () => ({ mode: __ffts.mode, m3: __ffts.game.missions.status('mission.ch1.old_friends'), respect: __ffts.game.rep.face.Respect, dreFriend: __ffts.game.social.get('npc.dre').Friendship }));
// Buy the car through the garage UI and drive.
await page.evaluate(() => { __ffts.game.bank.post('smoke.cash', 'cash', 100000, 'test'); __ffts.openGarage(__ffts.game.data.vehicles.Garages.find((g) => g.Id === 'garage.southside_motors')); });
await page.waitForTimeout(200);
await page.click('#garage-body button'); await page.waitForTimeout(200);
await page.screenshot({ path: shots + '/5-garage.png' });
await step('bought car', () => ({ cars: __ffts.game.garage.cars.map((c) => c.def + '@' + c.garage), toastCount: document.querySelectorAll('.toast').length }));
await page.click('#garage-close');
await page.evaluate(() => { const c = __ffts.game.garage.cars[0]; const rec = __ffts.game.retrieve(c.id); __ffts.enterCar(rec); });
await page.keyboard.down('w'); await page.waitForTimeout(2500); await page.keyboard.up('w');
await step('driving', () => ({ mode: __ffts.mode, kph: Math.round(__ffts.drive.car.kph), x: Math.round(__ffts.drive.car.x), z: Math.round(__ffts.drive.car.z), dmg: Math.round(__ffts.drive.car.damage) }));
await page.screenshot({ path: shots + '/6-driving.png' });
// Race: teleport to the lot, start, let the countdown run, then drive the checkpoints.
await page.evaluate(() => { const d = __ffts.drive; d.car.x = -150; d.car.z = -120; d.car.heading = 90; d.car.speed = 0; __ffts.startRace(__ffts.game.data.races[0]); });
await until(() => __ffts.race && __ffts.race.r.countdown <= 0, 60000);
await page.screenshot({ path: shots + '/7-race.png' });
await page.evaluate(() => { const R = __ffts.race.r; for (const cp of R.def.Checkpoints) { R.player.car.x = cp.X; R.player.car.z = cp.Z; R.player.car.speed = 0; R.step({}, 1 / 60); } });
await until(() => __ffts.game.racing.points > 0, 30000);
await step('race result', () => ({ points: __ffts.game.racing.points, wins: __ffts.game.racing.wins, cash: __ffts.game.bank.accounts.cash }));
// Save + reload persists.
await until(() => !__ffts.race, 60000);
await page.keyboard.press('Escape'); await page.waitForTimeout(200); await page.click('#menu-save'); await page.waitForTimeout(200);
await page.reload({ waitUntil: 'domcontentloaded' }); await page.waitForFunction(() => !document.getElementById('btn-new').disabled, null, { timeout: 60000 });
await step('continue visible after reload', () => ({ continueShown: !document.getElementById('btn-continue').hidden }));
await page.click('#btn-continue'); await page.waitForTimeout(500);
await step('restored', () => ({ m3: __ffts.game.missions.status('mission.ch1.old_friends'), cars: __ffts.game.garage.cars.length, points: __ffts.game.racing.points }));
// Phone layout with touch (close the desktop page first: software WebGL here is single-threaded).
await page.close();
const phone = await browser.newPage({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
await noFonts(phone);
phone.on('pageerror', (e) => errors.push('phone pageerror: ' + e.message));
await phone.goto(process.env.BASE_URL || 'file://' + resolve('dist/local.html'), { waitUntil: 'domcontentloaded' }); await phone.waitForFunction(() => !document.getElementById('btn-new').disabled, null, { timeout: 60000 });
await phone.screenshot({ path: shots + '/8-phone-title.png' });
await phone.tap('#btn-new'); await phone.waitForTimeout(1200);
await phone.screenshot({ path: shots + '/9-phone-play.png' });
const overflow = await phone.evaluate(() => document.documentElement.scrollWidth > innerWidth);
console.log('phone horizontal overflow:', overflow);
console.log('ERRORS:', errors.length ? errors : 'none');
await browser.close();
