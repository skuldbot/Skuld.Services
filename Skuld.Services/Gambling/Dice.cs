using Skuld.Core.Extensions;
using System.Collections.Generic;

namespace Skuld.Services.Gambling
{
    public class Dice
    {
        private readonly List<Die> dies;
        public int DieAmount { get { return dies.Count; } }

        public Dice()
        {
            dies = new List<Die>();
        }

        public Dice(int amount) : this()
        {
            for (int x = 0; x < amount; x++)
            {
                dies.Add(new Die());
            }
        }

        public void SetDice(int amount)
        {
            dies.Clear();

            for (int x = 0; x < amount; x++)
            {
                dies.Add(new Die());
            }
        }

        public ulong GetSumOfFaces()
        {
            ulong amount = 0;

            foreach (var die in dies)
            {
                amount = amount.Add(die.Face);
            }

            return amount;
        }

        public ushort[] GetFaces()
        {
            List<ushort> Face = new List<ushort>();
            foreach (var die in dies)
            {
                Face.Add(die.Face);
            }
            return Face.ToArray();
        }

        public Die[] GetDies()
            => dies.ToArray();

        public Die[] Roll()
        {
            dies.ForEach(x => x.Roll());

            return dies.ToArray();
        }
    }
}