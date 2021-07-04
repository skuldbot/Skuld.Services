using System;
using System.Threading;
using System.Threading.Tasks;

namespace Skuld.Services
{
	public abstract class BackgroundService : IBackgroundService
	{
		public int TimeoutDelay = 5000;
		public bool HasStarted = false;
		CancellationTokenSource TokenSource;
		private bool disposedValue;

		public void Start()
		{
			if (HasStarted)
			{
				throw new InvalidOperationException("Cannot start a started service");
			}

			TokenSource = new CancellationTokenSource();

			Task.Run(async () =>
			{
				Thread.CurrentThread.IsBackground = true;
				while (true)
				{
					RunAsync(TokenSource.Token).Wait(TokenSource.Token);
					await Task.Delay(TimeoutDelay).ConfigureAwait(false);
				}
			}, TokenSource.Token);

			HasStarted = true;
		}

		public void Stop()
		{
			if (!HasStarted)
			{
				throw new InvalidOperationException("Cannot stop a stopped service");
			}

			TokenSource.Cancel();
		}

		public abstract Task RunAsync(CancellationToken cancelToken);

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					TokenSource.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override finalizer
				// TODO: set large fields to null
				disposedValue = true;
			}
		}

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
