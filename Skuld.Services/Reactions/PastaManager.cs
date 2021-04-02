using Microsoft.EntityFrameworkCore;
using Skuld.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services
{
	public static class PastaManager
	{
		public static List<Pasta> GetPastas()
		{
			using var database = new SkuldDbContextFactory().CreateDbContext();

			return database.Pastas.AsNoTracking().ToList();
		}

		public static Pasta GetPasta(string title)
		{
			using var database = new SkuldDbContextFactory().CreateDbContext();

			return database.Pastas.AsNoTracking().ToList().FirstOrDefault(p => p.Name.ToLowerInvariant().Equals(title.ToLowerInvariant()));
		}

		public static async Task<bool> AddUpvoteAsync(Pasta pasta, User reactor)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			var vote = Database.PastaVotes.ToList().FirstOrDefault(x => x.VoterId == reactor.Id && x.PastaId == pasta.Id);
			if (vote is null)
			{
				Database.PastaVotes.Add(new PastaVotes
				{
					PastaId = pasta.Id,
					Upvote = true,
					VoterId = reactor.Id
				});

				await Database.SaveChangesAsync().ConfigureAwait(false);

				return true;
			}

			return false;
		}

		public static async Task<bool> AddDownvoteAsync(Pasta pasta, User reactor)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			var vote = Database.PastaVotes.ToList().FirstOrDefault(x => x.VoterId == reactor.Id && x.PastaId == pasta.Id);
			if (vote is null)
			{
				Database.PastaVotes.Add(new PastaVotes
				{
					PastaId = pasta.Id,
					Upvote = false,
					VoterId = reactor.Id
				});

				await Database.SaveChangesAsync().ConfigureAwait(false);

				return true;
			}

			return false;
		}

		public static async Task<bool> DeletePastaAsync(Pasta pasta, User user)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			if (pasta.IsOwner(user))
			{
				Database.Pastas.Remove(pasta);
				await Database.SaveChangesAsync().ConfigureAwait(false);

				return true;
			}

			return false;
		}
	}
}
