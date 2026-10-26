using KingsboardServer.Data.Records;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Data
{
    /// <summary>
    /// The Entity Framework session for the server's SQLite database. It is the only way the server reads or writes
    /// the database. Only repositories use it, each creating a short-lived instance per operation through
    /// <see cref="IDbContextFactory{TContext}"/>, since an instance must not be shared between threads.
    /// </summary>
    public class KingsboardDbContext : DbContext
    {
        /// <summary>
        /// Creates a session with the options set up in <c>Program.cs</c> (the SQLite connection string)
        /// </summary>
        public KingsboardDbContext(DbContextOptions<KingsboardDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// The stored game board (at most one)
        /// </summary>
        public DbSet<BoardRecord> Boards => Set<BoardRecord>();

        /// <summary>
        /// Every tile on the stored board
        /// </summary>
        public DbSet<BoardTileRecord> BoardTiles => Set<BoardTileRecord>();

        /// <summary>
        /// The stored pawns
        /// </summary>
        public DbSet<PawnRecord> Pawns => Set<PawnRecord>();

        /// <summary>
        /// Every card the stored pawns hold
        /// </summary>
        public DbSet<CardRecord> Cards => Set<CardRecord>();

        /// <summary>
        /// The steps of the stored pawns' planned paths
        /// </summary>
        public DbSet<PawnPathStepRecord> PawnPathSteps => Set<PawnPathStepRecord>();

        /// <summary>
        /// The stored chat messages
        /// </summary>
        public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();

        /// <summary>
        /// Sets up the tables: ids are GUIDs the server assigns, a board holds one tile per position, and a board's
        /// tiles and a pawn's cards and path steps are deleted with it
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BoardRecord>(board =>
            {
                board.HasKey(b => b.Id);
                board.Property(b => b.Id).ValueGeneratedNever();
                board.HasMany(b => b.Tiles)
                    .WithOne()
                    .HasForeignKey(t => t.BoardId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BoardTileRecord>(tile =>
            {
                tile.HasKey(t => t.Id);
                tile.Property(t => t.Id).ValueGeneratedNever();
                tile.HasIndex(t => new { t.BoardId, t.X, t.Y, t.Z }).IsUnique();
            });

            modelBuilder.Entity<PawnRecord>(pawn =>
            {
                pawn.HasKey(p => p.Id);
                pawn.Property(p => p.Id).ValueGeneratedNever();
                pawn.HasMany(p => p.Cards)
                    .WithOne()
                    .HasForeignKey(c => c.PawnId)
                    .OnDelete(DeleteBehavior.Cascade);
                pawn.HasMany(p => p.PlannedPath)
                    .WithOne()
                    .HasForeignKey(s => s.PawnId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CardRecord>(card =>
            {
                card.HasKey(c => c.Id);
                card.Property(c => c.Id).ValueGeneratedNever();
                card.HasIndex(c => new { c.PawnId, c.Pile, c.Order });
            });

            modelBuilder.Entity<PawnPathStepRecord>(step =>
            {
                step.HasKey(s => s.Id);
                step.Property(s => s.Id).ValueGeneratedNever();
                step.HasIndex(s => new { s.PawnId, s.Order });
            });

            modelBuilder.Entity<ChatMessageRecord>(message =>
            {
                message.HasKey(m => m.Id);
                message.Property(m => m.Id).ValueGeneratedNever();
                message.HasIndex(m => m.Sequence).IsUnique();
            });
        }
    }
}
