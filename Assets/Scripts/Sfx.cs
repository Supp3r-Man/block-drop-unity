using UnityEngine;

namespace BlockDrop
{
    /// <summary>Sound effects synthesised at start-up (sine/saw beeps), so there are no audio files.</summary>
    public sealed class Sfx
    {
        private const int SampleRate = 44100;

        private readonly AudioSource source;
        private readonly AudioClip move;
        private readonly AudioClip rotate;
        private readonly AudioClip hold;
        private readonly AudioClip lockPiece;
        private readonly AudioClip clear;
        private readonly AudioClip fourRows;
        private readonly AudioClip levelUp;
        private readonly AudioClip gameOver;

        public Sfx(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.5f;

            move = Tone("move", new[] { 400f }, 0.03f, false);
            rotate = Tone("rotate", new[] { 600f, 750f }, 0.035f, false);
            hold = Tone("hold", new[] { 500f, 350f }, 0.05f, false);
            lockPiece = Tone("lock", new[] { 140f }, 0.08f, true);
            clear = Tone("clear", new[] { 660f, 880f, 990f }, 0.07f, false);
            fourRows = Tone("fourrows", new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.08f, false);
            levelUp = Tone("levelup", new[] { 392f, 523f, 659f, 784f }, 0.07f, false);
            gameOver = Tone("gameover", new[] { 392f, 330f, 262f, 196f }, 0.2f, true);
        }

        public void Move() { Play(move); }
        public void Rotate() { Play(rotate); }
        public void Hold() { Play(hold); }
        public void Lock() { Play(lockPiece); }
        public void Clear(int lines) { Play(lines >= 4 ? fourRows : clear); }
        public void LevelUp() { Play(levelUp); }
        public void GameOver() { Play(gameOver); }

        private void Play(AudioClip clip)
        {
            if (clip != null) source.PlayOneShot(clip);
        }

        /// <summary>One note per frequency, each noteSeconds long, with a quick fade so notes do not click.</summary>
        private static AudioClip Tone(string name, float[] freqs, float noteSeconds, bool saw)
        {
            int perNote = Mathf.RoundToInt(noteSeconds * SampleRate);
            var data = new float[perNote * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
            {
                double phase = 0.0;
                double step = freqs[n] / SampleRate;
                for (int i = 0; i < perNote; i++)
                {
                    phase += step;
                    phase -= System.Math.Floor(phase);
                    float wave = saw ? (float)(phase * 2.0 - 1.0) : Mathf.Sin((float)(phase * 2.0 * System.Math.PI));
                    float t = (float)i / perNote;
                    float env = Mathf.Min(1f, t * 20f) * (1f - t);
                    data[n * perNote + i] = wave * env * 0.4f;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
