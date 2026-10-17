using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcBoard;
using Kingsboard.Networking;
using UnityEngine;
using BoardPosition = Kingsboard.Board.BoardPosition;
using TileType = Kingsboard.Board.TileType;

namespace Kingsboard.Board
{
    /// <summary>
    /// All communication with the server's board goes through here. Nothing outside this class
    /// knows about gRPC: the rest of the game only sees <see cref="GameBoard"/>, <see cref="MapTile"/> and the events below.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class BoardGrpcHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _connected;

        /// <summary>
        /// Raised with the whole board when we connect, and whenever the server recreates the board.
        /// It replaces any board received before.
        /// </summary>
        public event Action<GameBoard> BoardReceived;

        /// <summary>
        /// Raised when tiles change: the board id, the new board version, the tiles placed or replaced,
        /// and the positions whose tiles were removed
        /// </summary>
        public event Action<string, long, IReadOnlyList<MapTile>, IReadOnlyList<BoardPosition>> TilesChanged;

        /// <summary>
        /// Raised when the connection to the board is lost. It reconnects by itself.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Starts listening for the board, reconnecting whenever the connection drops.
        /// After each reconnect the server sends the whole board again.
        /// </summary>
        public async void Connect()
        {
            CancellationToken token = _shutdown.Token;
            GrpcBoard.BoardService.BoardServiceClient client = new GrpcBoard.BoardService.BoardServiceClient(GrpcConnection.Channel);
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<BoardEvent> call = client.JoinBoardStream(new Empty(), cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        BoardEvent boardEvent = call.ResponseStream.Current;

                        if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.Board)
                        {
                            _connected = true;
                            logFailure = true;
                            BoardReceived?.Invoke(ToGameBoard(boardEvent.Board));
                        }
                        else if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.TilesChanged)
                        {
                            GrpcBoard.TilesChanged changed = boardEvent.TilesChanged;
                            TilesChanged?.Invoke(changed.BoardId, changed.Version, changed.Tiles.Select(ToMapTile).ToList(), changed.Removed.Select(ToBoardPosition).ToList());
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    // Only log the first failure, not every retry while the server is down
                    if (logFailure)
                    {
                        Debug.LogWarning($"[Board] Not connected to the board server, retrying: {ex.Message}");
                        logFailure = false;
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (_connected)
                {
                    _connected = false;
                    Disconnected?.Invoke();
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Stops listening (closing the stream is what tells the server we've gone)
        /// </summary>
        public void Dispose()
        {
            _shutdown.Cancel();
        }

        private static GameBoard ToGameBoard(BoardState state)
        {
            GameBoard board = new GameBoard()
            {
                Id = state.BoardId,
                Version = state.Version,
                Width = state.Width,
                Depth = state.Depth,
                Height = state.Height
            };
            board.PlaceTiles(state.Tiles.Select(ToMapTile));
            return board;
        }

        private static MapTile ToMapTile(Tile tile)
        {
            return new MapTile()
            {
                Position = ToBoardPosition(tile.Position),
                Type = ToTileType(tile.Type),
                IsPassable = tile.Passable
            };
        }

        private static BoardPosition ToBoardPosition(GrpcBoard.BoardPosition position)
        {
            // A missing message field arrives as null; proto3 treats that as all-zero
            if (position == null)
            {
                return new BoardPosition();
            }

            return new BoardPosition()
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z
            };
        }

        private static TileType ToTileType(GrpcBoard.TileType type)
        {
            switch (type)
            {
                case GrpcBoard.TileType.Water: return TileType.Water;
                case GrpcBoard.TileType.Plains: return TileType.Plains;
                case GrpcBoard.TileType.Forest: return TileType.Forest;
                case GrpcBoard.TileType.Hills: return TileType.Hills;
                case GrpcBoard.TileType.Mountain: return TileType.Mountain;
                case GrpcBoard.TileType.Stone: return TileType.Stone;
                default: return TileType.Unknown;
            }
        }
    }
}
