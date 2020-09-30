using Skuld.Core.Extensions;
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

		public Die this[int key]
		{
			get => dies[key];
			set => dies[key] = value;
		}
	}
}