using System;
using System.Threading;
using System.Threading.Tasks;

namespace Skuld.Services
{
	public interface IBackgroundService : IDisposable
	{
		public void Start();

		public void Stop();
	}
}
