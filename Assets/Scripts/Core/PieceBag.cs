namespace BlockDrop.Core
{
    /// <summary>
    /// The 7-bag randomiser: pieces come out in shuffled sets of seven, so every piece appears exactly once
    /// per seven draws and droughts are bounded. Uses the repo's own Rng so a seed gives the same sequence everywhere.
    /// </summary>
    public sealed class PieceBag
    {
        private readonly Rng rng;
        private readonly PieceType[] bag = new PieceType[Shapes.PieceCount];
        private int index = Shapes.PieceCount;

        public PieceBag(Rng rng)
        {
            this.rng = rng;
        }

        public PieceType Next()
        {
            if (index >= bag.Length) Refill();
            return bag[index++];
        }

        private void Refill()
        {
            for (int i = 0; i < bag.Length; i++) bag[i] = (PieceType)i;
            // Fisher-Yates.
            for (int i = bag.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                PieceType tmp = bag[i];
                bag[i] = bag[j];
                bag[j] = tmp;
            }
            index = 0;
        }
    }
}
