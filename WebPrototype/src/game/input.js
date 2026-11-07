// Keyboard + touch input merged into one state object read each frame.
export class Input {
  constructor(root) {
    this.keys = new Set(); this.pressed = new Set(); this.held = new Set();
    this.stick = { x: 0, y: 0 }; this.look = { dx: 0, dy: 0 }; this.touch = 'ontouchstart' in window || navigator.maxTouchPoints > 0;
    addEventListener('keydown', (e) => { if (e.repeat) return; const k = e.key.toLowerCase(); this.keys.add(k); this.pressed.add(k); if ([' ', 'tab', 'arrowup', 'arrowdown'].includes(k)) e.preventDefault(); });
    addEventListener('keyup', (e) => this.keys.delete(e.key.toLowerCase()));
    addEventListener('blur', () => { this.keys.clear(); this.held.clear(); this.stick.x = this.stick.y = 0; });
    // Mouse drag on the canvas turns the camera on desktop.
    let drag = null;
    root.addEventListener('mousedown', (e) => { if (e.target === root) drag = { x: e.clientX, y: e.clientY }; });
    addEventListener('mouseup', () => (drag = null));
    addEventListener('mousemove', (e) => { if (!drag) return; this.look.dx += e.clientX - drag.x; this.look.dy += e.clientY - drag.y; drag = { x: e.clientX, y: e.clientY }; });
    // Touch: left half = movement stick, right half = camera drag.
    const stickEl = document.getElementById('stick'), knob = document.getElementById('knob');
    let stickId = null, lookId = null, origin = null, lookLast = null;
    root.addEventListener('touchstart', (e) => {
      for (const t of e.changedTouches) {
        if (t.clientX < innerWidth * 0.45 && stickId === null) {
          stickId = t.identifier; origin = { x: t.clientX, y: t.clientY };
          stickEl.hidden = false; stickEl.style.left = origin.x - 60 + 'px'; stickEl.style.top = origin.y - 60 + 'px'; knob.style.transform = 'translate(0px,0px)';
        } else if (lookId === null) { lookId = t.identifier; lookLast = { x: t.clientX, y: t.clientY }; }
      }
      e.preventDefault();
    }, { passive: false });
    root.addEventListener('touchmove', (e) => {
      for (const t of e.changedTouches) {
        if (t.identifier === stickId) {
          let dx = t.clientX - origin.x, dy = t.clientY - origin.y; const d = Math.hypot(dx, dy), max = 50;
          if (d > max) { dx = dx / d * max; dy = dy / d * max; }
          this.stick.x = dx / max; this.stick.y = -dy / max; knob.style.transform = `translate(${dx}px,${dy}px)`;
        } else if (t.identifier === lookId) { this.look.dx += (t.clientX - lookLast.x) * 1.4; this.look.dy += (t.clientY - lookLast.y) * 1.4; lookLast = { x: t.clientX, y: t.clientY }; }
      }
      e.preventDefault();
    }, { passive: false });
    const end = (e) => {
      for (const t of e.changedTouches) {
        if (t.identifier === stickId) { stickId = null; this.stick.x = this.stick.y = 0; stickEl.hidden = true; }
        if (t.identifier === lookId) lookId = null;
      }
    };
    root.addEventListener('touchend', end); root.addEventListener('touchcancel', end);
  }
  /** Binds an on-screen button to a virtual key (pressed on touch start, held until release). */
  bindButton(el, key) {
    const down = (e) => { e.preventDefault(); e.stopPropagation(); this.held.add(key); this.pressed.add(key); el.classList.add('on'); };
    const up = (e) => { e.preventDefault(); this.held.delete(key); el.classList.remove('on'); };
    el.addEventListener('touchstart', down, { passive: false }); el.addEventListener('touchend', up); el.addEventListener('touchcancel', up);
    el.addEventListener('mousedown', down); el.addEventListener('mouseup', up); el.addEventListener('mouseleave', up);
  }
  down(...ks) { return ks.some((k) => this.keys.has(k) || this.held.has(k)); }
  hit(...ks) { return ks.some((k) => this.pressed.has(k)); }
  axis() {
    let x = this.stick.x, y = this.stick.y;
    if (this.down('a', 'arrowleft')) x -= 1; if (this.down('d', 'arrowright')) x += 1;
    if (this.down('w', 'arrowup')) y += 1; if (this.down('s', 'arrowdown')) y -= 1;
    const m = Math.hypot(x, y); return m > 1 ? { x: x / m, y: y / m } : { x, y };
  }
  endFrame() { this.pressed.clear(); this.look.dx = this.look.dy = 0; }
}
