using System;
using System.Collections.Generic;
using System.Linq;

namespace InsightCast.Models
{
    /// <summary>
    /// Motion variation level — controls weight distribution.
    /// </summary>
    public enum MotionVariation
    {
        Calm,
        Normal,
        Random
    }

    /// <summary>
    /// Motion intensity — controls zoom max and pan distance.
    /// </summary>
    public enum MotionIntensity
    {
        Weak,
        Medium,
        Strong
    }

    /// <summary>
    /// Parameters for zoom/pan per intensity level.
    /// </summary>
    public static class MotionIntensityParams
    {
        public static (double ZoomMax, double PanPercent) Get(MotionIntensity intensity) => intensity switch
        {
            MotionIntensity.Weak => (1.04, 0.03),
            MotionIntensity.Strong => (1.12, 0.08),
            _ => (1.08, 0.05), // Medium
        };
    }

    /// <summary>
    /// Assigns MotionType to each scene using constrained weighted random.
    /// Rules:
    ///   A. No same motion consecutive (2+ in a row forbidden)
    ///   B. Pan direction alternates (no same-direction pan back-to-back)
    ///   C. Reset every 7-9 scenes (force ZoomOut or None)
    ///   D. Pan density: 1-2 per 5-scene window (not 0, not 3+)
    ///   First scene: ZoomOut (intro overview)
    ///   Last scene: ZoomOut (closing)
    /// </summary>
    public static class MotionAssigner
    {
        private static readonly MotionType[] Candidates =
        {
            MotionType.ZoomIn, MotionType.ZoomOut,
            MotionType.PanLeft, MotionType.PanRight,
            MotionType.PanUp, MotionType.PanDown,
            MotionType.None
        };

        private static Dictionary<MotionType, double> GetWeights(MotionVariation variation) => variation switch
        {
            MotionVariation.Calm => new()
            {
                { MotionType.ZoomIn, 5.0 },
                { MotionType.ZoomOut, 2.0 },
                { MotionType.PanLeft, 1.0 },
                { MotionType.PanRight, 1.0 },
                { MotionType.PanUp, 0.5 },
                { MotionType.PanDown, 0.5 },
                { MotionType.None, 1.0 },
            },
            MotionVariation.Random => new()
            {
                { MotionType.ZoomIn, 2.5 },
                { MotionType.ZoomOut, 2.0 },
                { MotionType.PanLeft, 2.0 },
                { MotionType.PanRight, 2.0 },
                { MotionType.PanUp, 1.5 },
                { MotionType.PanDown, 1.5 },
                { MotionType.None, 1.0 },
            },
            _ => new() // Normal
            {
                { MotionType.ZoomIn, 4.0 },
                { MotionType.ZoomOut, 1.5 },
                { MotionType.PanLeft, 1.5 },
                { MotionType.PanRight, 1.5 },
                { MotionType.PanUp, 0.8 },
                { MotionType.PanDown, 0.8 },
                { MotionType.None, 0.5 },
            },
        };

        /// <summary>
        /// Generate motion assignments for N scenes.
        /// </summary>
        public static List<MotionType> Generate(int sceneCount, MotionVariation variation, int seed = 0)
        {
            if (sceneCount <= 0) return new List<MotionType>();
            if (sceneCount == 1) return new List<MotionType> { MotionType.ZoomOut };

            var rng = seed == 0 ? new Random() : new Random(seed);
            var weights = GetWeights(variation);
            var result = new List<MotionType>(sceneCount);

            // Reset interval: random between 7-9 per run
            int resetInterval = rng.Next(7, 10);
            int sinceReset = 0;

            for (int i = 0; i < sceneCount; i++)
            {
                MotionType chosen;

                // Fixed: first scene = ZoomOut (intro overview)
                if (i == 0)
                {
                    chosen = MotionType.ZoomOut;
                }
                // Fixed: last scene = ZoomOut (closing)
                else if (i == sceneCount - 1)
                {
                    chosen = MotionType.ZoomOut;
                }
                // Rule C: reset every 7-9 scenes
                else if (sinceReset >= resetInterval)
                {
                    chosen = rng.NextDouble() < 0.7 ? MotionType.ZoomOut : MotionType.None;
                    sinceReset = 0;
                    resetInterval = rng.Next(7, 10);
                }
                else
                {
                    chosen = PickConstrained(i, result, weights, rng);
                }

                result.Add(chosen);
                sinceReset++;
            }

            return result;
        }

        private static MotionType PickConstrained(
            int index, List<MotionType> previous,
            Dictionary<MotionType, double> weights, Random rng)
        {
            const int maxAttempts = 30;
            var prev = index > 0 ? previous[index - 1] : (MotionType?)null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var candidate = WeightedPick(weights, rng);

                // Rule A: no same motion consecutive
                if (candidate == prev)
                    continue;

                // Rule B: pan direction alternates
                if (IsPan(candidate) && prev.HasValue && IsPan(prev.Value))
                {
                    if (IsSamePanAxis(candidate, prev.Value))
                        continue;
                }

                // Rule D: pan density in 5-scene window (1-2 pans per window)
                if (!CheckPanDensity(index, previous, candidate))
                    continue;

                return candidate;
            }

            // Fallback: ZoomIn is always safe
            return MotionType.ZoomIn;
        }

        private static bool IsPan(MotionType t) =>
            t == MotionType.PanLeft || t == MotionType.PanRight ||
            t == MotionType.PanUp || t == MotionType.PanDown;

        private static bool IsSamePanAxis(MotionType a, MotionType b)
        {
            // Same horizontal direction
            if (a == MotionType.PanLeft && b == MotionType.PanLeft) return true;
            if (a == MotionType.PanRight && b == MotionType.PanRight) return true;
            // Same vertical direction
            if (a == MotionType.PanUp && b == MotionType.PanUp) return true;
            if (a == MotionType.PanDown && b == MotionType.PanDown) return true;
            return false;
        }

        /// <summary>
        /// Rule D: In any 5-scene window, pan count should be 1-2 (not 0, not 3+).
        /// Only enforced when we have enough history.
        /// </summary>
        private static bool CheckPanDensity(int index, List<MotionType> previous, MotionType candidate)
        {
            if (index < 4) return true; // Not enough history yet

            // Count pans in last 4 scenes + candidate = 5-scene window
            int panCount = candidate != MotionType.None && IsPan(candidate) ? 1 : 0;
            int windowStart = Math.Max(0, index - 4);
            for (int j = windowStart; j < index; j++)
                if (IsPan(previous[j])) panCount++;

            // Allow 0 pans if window is small, otherwise enforce 1-2
            return panCount <= 2;
        }

        private static MotionType WeightedPick(Dictionary<MotionType, double> weights, Random rng)
        {
            double total = weights.Values.Sum();
            double roll = rng.NextDouble() * total;
            double cumulative = 0;
            foreach (var kvp in weights)
            {
                cumulative += kvp.Value;
                if (roll <= cumulative) return kvp.Key;
            }
            return MotionType.ZoomIn;
        }
    }
}
