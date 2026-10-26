using Kingsboard.Shared.Models;
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
        /// The stored game board's definition (at most one)
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
        /// Every kind of card in the game
        /// </summary>
        public DbSet<CardRecord> Cards => Set<CardRecord>();

        /// <summary>
        /// Which pawn holds each copy of a card, and where
        /// </summary>
        public DbSet<PawnCardRecord> PawnCards => Set<PawnCardRecord>();

        /// <summary>
        /// The stored chat messages
        /// </summary>
        public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();

        /// <summary>
        /// Sets up the tables. Every row's key is its <see cref="BaseModel.Id"/>, numbered by the database, and every
        /// <see cref="PublicBaseModel.PublicId"/> is unique. Records hold no links to each other's objects, only ids:
        /// a board holds one tile per position and its tiles are deleted with it, and a pawn's cards are deleted with it.
        /// A card definition can't be deleted while a pawn holds a copy.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BoardRecord>(board =>
            {
                board.HasIndex(b => b.PublicId).IsUnique();
            });

            modelBuilder.Entity<BoardTileRecord>(tile =>
            {
                tile.HasOne<BoardRecord>()
                    .WithMany()
                    .HasForeignKey(t => t.BoardRecordId)
                    .OnDelete(DeleteBehavior.Cascade);
                tile.HasIndex(t => new { t.BoardRecordId, t.X, t.Y, t.Z }).IsUnique();
            });

            modelBuilder.Entity<PawnRecord>(pawn =>
            {
                pawn.HasIndex(p => p.PublicId).IsUnique();
            });

            modelBuilder.Entity<CardRecord>(card =>
            {
                card.HasIndex(c => c.DefinitionId).IsUnique();
            });

            modelBuilder.Entity<PawnCardRecord>(pawnCard =>
            {
                pawnCard.HasIndex(c => c.PublicId).IsUnique();
                pawnCard.HasOne<PawnRecord>()
                    .WithMany()
                    .HasForeignKey(c => c.PawnRecordId)
                    .OnDelete(DeleteBehavior.Cascade);
                pawnCard.HasOne<CardRecord>()
                    .WithMany()
                    .HasForeignKey(c => c.CardRecordId)
                    .OnDelete(DeleteBehavior.Restrict);
                pawnCard.HasIndex(c => new { c.PawnRecordId, c.Location, c.Order });
            });

            modelBuilder.Entity<ChatMessageRecord>(message =>
            {
                message.HasIndex(m => m.Sequence).IsUnique();
            });
        }
    }
}
