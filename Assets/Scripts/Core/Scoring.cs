using System;

namespace BlockDrop.Core
{
    public static class Scoring
    {
        public const int SoftDropPointsPerCell = 1;
        public const int HardDropPointsPerCell = 2;

        private static readonly int[] LineBase = { 0, 100, 300, 500, 800 };

        /// <summary>Points for clearing 1 to 4 rows at once, times the level the clear happened on. Anything else is 0.</summary>
        public static int LineScore(int lines, int level)
        {
            if (lines < 1 || lines > 4 || level < 1) return 0;
            return LineBase[lines] * level;
        }
    }

    public static class Speed
    {
        public const int MaxLevel = 20;
        public const int LinesPerLevel = 10;

        /// <summary>Levels advance every ten cleared rows, capped at MaxLevel.</summary>
        public static int LevelFromLines(int lines)
        {
            if (lines < 0) lines = 0;
            return Math.Min(MaxLevel, 1 + lines / LinesPerLevel);
        }

        /// <summary>Seconds per row of gravity at a level: (0.8 - 0.007 * (level - 1)) ^ (level - 1), floored at 0.02.</summary>
        public static float GravityInterval(int level)
        {
            int l = Math.Max(1, Math.Min(MaxLevel, level));
            double v = Math.Pow(0.8 - (l - 1) * 0.007, l - 1);
            return (float)Math.Max(0.02, v);
        }
    }
}
