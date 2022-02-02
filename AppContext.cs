using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server
{
    public class AppContext : DbContext
    {
        public AppContext(DbContextOptions<AppContext> options)
            : base(options)
        { }

        public DbSet<User> Users { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
    }
}
