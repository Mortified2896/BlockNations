using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    public enum LearnedDifficulty { Easy, Medium, Hard }

    // Difficulty changes sampling, never information access or the amount of inference.
    // These are initial playtest presets; calibrated checkpoints can replace them later.
    public static class LearnedActionSampling
    {
        public static int Choose(float[] probabilities, ICollection<int> legalChoices,
            LearnedDifficulty difficulty, Random random)
        {
            if (probabilities == null || probabilities.Length != LearnedActionSchema.ActionCount ||
                legalChoices == null || legalChoices.Count == 0 || random == null)
                throw new ArgumentException("A policy decision requires probabilities, legal choices and a random source.");
            double temperature;
            double minimumRelativeProbability;
            switch (difficulty)
            {
                case LearnedDifficulty.Easy: temperature = 1.8; minimumRelativeProbability = 0; break;
                case LearnedDifficulty.Medium: temperature = 0.8; minimumRelativeProbability = 0; break;
                case LearnedDifficulty.Hard: temperature = 0.25; minimumRelativeProbability = 0.7; break;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
            // Stable ordering keeps seeded decisions independent of dictionary iteration.
            var choices = new List<int>(legalChoices);
            choices.Sort();
            double maximum = 0;
            foreach (int choice in choices)
            {
                if (choice < 0 || choice >= probabilities.Length || float.IsNaN(probabilities[choice]) ||
                    float.IsInfinity(probabilities[choice]) || probabilities[choice] < 0)
                    throw new InvalidOperationException("The learned policy returned invalid probabilities.");
                maximum = Math.Max(maximum, probabilities[choice]);
            }
            if (maximum <= 0) throw new InvalidOperationException("The learned policy assigned no probability to a legal action.");
            var weights = new double[choices.Count];
            double total = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                double relative = probabilities[choices[i]] / maximum;
                if (relative >= minimumRelativeProbability)
                    weights[i] = Math.Pow(relative, 1 / temperature);
                total += weights[i];
            }
            double sample = random.NextDouble() * total;
            int lastPositive = choices[0];
            for (int i = 0; i < choices.Count; i++)
            {
                if (weights[i] <= 0) continue;
                lastPositive = choices[i];
                sample -= weights[i];
                if (sample < 0) return choices[i];
            }
            return lastPositive;
        }

        public static float[] Mask(ICollection<int> choices)
        {
            var mask = new float[LearnedActionSchema.ActionCount];
            foreach (int choice in choices)
            {
                if (choice < 0 || choice >= mask.Length) throw new ArgumentOutOfRangeException(nameof(choices));
                mask[choice] = 1;
            }
            return mask;
        }
    }
}
