using System;

namespace FFTS.Core
{
    public interface IRandom
    {
        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        int Range(int minInclusive, int maxExclusive);
        /// <summary>Uniform float in [0, 1).</summary>
        float Value();
        bool Chance(float probability);
    }

    /// <summary>
    /// SplitMix64-based generator. Deterministic and serializable, so combat, AI and world
    /// simulation can be replayed in tests and restored exactly from save data.
    /// </summary>
    public sealed class DeterministicRandom : IRandom
    {
        public ulong State { get; private set; }

        public DeterministicRandom(ulong seed) { State = seed; }

        private ulong NextUInt64()
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                ulong z = State;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)(NextUInt64() % span));
        }

        public float Value() => (NextUInt64() >> 40) / (float)(1UL << 24);

        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return Value() < probability;
        }

        public void Restore(ulong state) { State = state; }
    }

    public static class MathUtil
    {
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Abs(float v) => v < 0 ? -v : v;
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
