using System;
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

		public Dice(int amount, ulong maxValue) : this()
		{
			for (int x = 0; x < amount; x++)
			{
				dies.Add(new Die(maxValue));
			}
		}

		public Dice SetDice(int amount)
		{
			dies.Clear();

			for (int x = 0; x < amount; x++)
			{
				dies.Add(new Die());
			}

			return this;
		}

		public Dice SetDieValue(int index, ushort amount)
		{
			if (index < DieAmount)
			{
				dies[index].SetFace(amount);
			}
			else
			{
				throw new IndexOutOfRangeException("Index is over or equal to length of array");
			}

			return this;
		}

		public ulong GetSumOfFaces()
		{
			ulong sum = 0;
			int i;
			for (i = 0; i < dies.Count; i++)
			{
				sum += dies[i].Face;
			}
			return sum;
		}

		public ushort[] GetFaces()
		{
			ushort[] faces = Array.Empty<ushort>();

			for (int f = 0; f < dies.Count; f++)
			{
				faces[f] = dies[f].Face;
			}

			return faces;
		}

		public Die[] GetDies()
			=> dies.ToArray();

		public Die[] Roll()
		{
			dies.ForEach(x => x.Roll());

			return dies.ToArray();
		}

		public Die this[int key]
		{
			get => dies[key];
			set => dies[key] = value;
		}
	}
}