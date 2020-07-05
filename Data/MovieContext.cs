using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SampleApp.Models;

namespace SampleApp.Data
{
    public sealed class MovieContext : DbContext
    {
        public MovieContext(DbContextOptions<MovieContext> options)
            : base(options)
        {
            var conn = (SqlConnection)Database.GetDbConnection();
            // This will disable msi auth for local db not suggested for production
            if (!conn.ConnectionString.Contains("Trusted_Connection=True"))
            {
                conn.AccessToken = new AzureServiceTokenProvider().GetAccessTokenAsync("https://database.windows.net/")
                    .Result;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Movie>().HasData(
                new Movie
                {
                    Id = 1,
                    Title = "The Shawshank Redemption",
                    Genre = "Drama",
                    ReleaseDate = new DateTime(1994, 10, 14)
                },
                new Movie
                {
                    Id = 2,
                    Title = "The Godfather",
                    Genre = "Drama",
                    ReleaseDate = new DateTime(1972, 3, 24)
                },
                new Movie
                {
                    Id = 3,
                    Title = "The Godfather: Part II",
                    Genre = "Drama",
                    ReleaseDate = new DateTime(1974, 12, 18)
                },
                new Movie
                {
                    Id = 4,
                    Title = "The Dark Knight",
                    Genre = "Action",
                    ReleaseDate = new DateTime(2008, 7, 18)
                }
            );
        }

        public DbSet<Movie> Movies { get; set; }
    }
}
