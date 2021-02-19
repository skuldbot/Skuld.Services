using Skuld.Core;
using System;
using System.Globalization;

namespace Skuld.Services.Gambling
{
	/// <summary>
	/// Singular instance of a Die
	/// </summary>
	public class Die
	{
		/// <summary>
		/// Current "landed face"</br>Returns 0 if not rolled
		/// </summary>
		public ushort Face { get; private set; } = 0;
		public ulong MaxValue { get; private set; }

		/// <summary>
		/// Sets by default <see cref="MaxValue"/> to 6
		/// </summary>
		public Die()
		{
			MaxValue = 6;
		}

		/// <summary>
		/// Sets the MaxValue on construction
		/// </summary>
		/// <param name="maxValue">Maximum roll potential (inclusive)</param>
		public Die(ulong maxValue)
		{
			MaxValue = maxValue;
		}

		/// <summary>
		/// Rolls the die
		/// </summary>
		/// <returns>The current instance of <see cref="Die"/></returns>
		public Die Roll()
		{
			Face = (ushort)SkuldRandom.Next(1L, MaxValue + 1);
			return this;
		}

		/// <summary>
		/// Sets the face value
		/// </summary>
		/// <param name="value">new value to set as the face</param>
		/// <returns>The current instance of <see cref="Die"/></returns>
		public Die SetFace(ushort value)
		{
			Face = value;
			return this;
		}

		public static bool operator ==(Die left, Die right)
			=> left.Face == right.Face;

		public static bool operator !=(Die left, Die right)
			=> !(left == right);

		public static bool operator ==(ushort value, Die die)
			=> value == die.Face;

		public static bool operator !=(ushort value, Die die)
			=> !(value == die);

		public static bool Equals(Die left, Die right)
			=> left == right;

		public override bool Equals(object obj)
			=> base.Equals(obj);

		public override int GetHashCode()
			=> base.GetHashCode();

		public override string ToString()
			=> Convert.ToString(Face, CultureInfo.InvariantCulture);
	}
}