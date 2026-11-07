// SplitMix64 port of FFTS.Core.DeterministicRandom (BigInt for exact 64-bit math).
const M = (1n << 64n) - 1n;
export class Rng {
  constructor(seed) { this.state = BigInt.asUintN(64, BigInt(seed)); }
  next() {
    this.state = (this.state + 0x9E3779B97F4A7C15n) & M;
    let z = this.state;
    z = ((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n) & M;
    z = ((z ^ (z >> 27n)) * 0x94D049BB133111EBn) & M;
    return z ^ (z >> 31n);
  }
  range(min, maxExcl) { if (maxExcl <= min) return min; return min + Number(this.next() % BigInt(maxExcl - min)); }
  value() { return Number(this.next() >> 40n) / 16777216; }
  chance(p) { if (p <= 0) return false; if (p >= 1) return true; return this.value() < p; }
}
export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const clamp01 = (v) => clamp(v, 0, 1);
export const roundAway = (v) => (v < 0 ? -Math.round(-v) : Math.round(v));
