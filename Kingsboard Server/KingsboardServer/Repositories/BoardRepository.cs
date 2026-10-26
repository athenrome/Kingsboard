using Kingsboard.Shared.Models.Board;
using KingsboardServer.Data;
using KingsboardServer.Data.Records;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Stores the game board in the SQLite database through Entity Framework, so it outlives a server restart.
    /// Thread-safe: each call uses its own short-lived <see cref="KingsboardDbContext"/>.
    /// </summary>
    public class BoardRepository : IBoardRepository
    {
        private readonly IDbContextFactory<KingsboardDbContext> _contextFactory;

        /// <summary>
        /// Creates a repository that opens database sessions from <paramref name="contextFactory"/>
        /// </summary>
        public BoardRepository(IDbContextFactory<KingsboardDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Reads the stored board with all its tiles, or null if no board has been saved yet
        /// </summary>
        public BoardRecord? GetBoard()
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            return context.Boards
                .AsNoTracking()
                .Include(b => b.Tiles)
                .FirstOrDefault();
        }

        /// <summary>
        /// Stores a board, with its tiles, in place of the current one
        /// </summary>
        public void ReplaceBoard(BoardRecord board)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            // A board holds tens of thousands of tiles; there's nothing to compare against, so skip change detection
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            using var transaction = context.Database.BeginTransaction();

            context.BoardTiles.ExecuteDelete();
            context.Boards.ExecuteDelete();
            context.Boards.Add(board);
            context.SaveChanges();

            transaction.Commit();
        }

        /// <summary>
        /// Saves one change to the stored board in a single transaction: deletes the tiles at <paramref name="removed"/>,
        /// stores <paramref name="placed"/> (in place of any tile already at the same position) and sets the board's version.
        /// Throws if the board isn't stored.
        /// </summary>
        public void SaveTileChanges(Guid boardId, long version, IReadOnlyCollection<BoardTileRecord> placed, IReadOnlyCollection<BoardPosition> removed)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();

            int boardRecordId = context.Boards
                .Where(b => b.BoardId == boardId)
                .Select(b => (int?)b.Id)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Board {boardId} isn't stored");

            context.Boards
                .Where(b => b.Id == boardRecordId)
                .ExecuteUpdate(setters => setters.SetProperty(b => b.Version, version));

            // A position holds at most one tile, so clear every position being placed on as well as those being removed
            IEnumerable<BoardPosition> cleared = removed.Concat(placed.Select(t => new BoardPosition(t.X, t.Y, t.Z))).Distinct();
            foreach (BoardPosition position in cleared)
            {
                context.BoardTiles
                    .Where(t => t.BoardRecordId == boardRecordId && t.X == position.X && t.Y == position.Y && t.Z == position.Z)
                    .ExecuteDelete();
            }

            foreach (BoardTileRecord tile in placed)
            {
                tile.BoardRecordId = boardRecordId;
            }
            context.BoardTiles.AddRange(placed);
            context.SaveChanges();

            transaction.Commit();
        }
    }
}
