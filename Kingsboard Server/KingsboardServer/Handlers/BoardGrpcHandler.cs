using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcBoard;
using KingsboardServer.Models.Board;
using KingsboardServer.Services.Interfaces;
using ProtoTileType = GrpcBoard.TileType;
using TileType = KingsboardServer.Models.Enumerations.TileType;
using ProtoBoardPosition = GrpcBoard.BoardPosition;
using BoardPosition = KingsboardServer.Models.Board.BoardPosition;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles board requests from clients (gRPC). All board logic lives in <see cref="IBoardService"/>.
    /// </summary>
    public class BoardGrpcHandler : BoardService.BoardServiceBase
    {
        // Per-client buffer of board events. If a client falls this far behind, its queued
        // changes are thrown away and it is sent a fresh snapshot instead, so it never misses a change.
        private const int OutboxCapacity = 256;

        private readonly IBoardService _boardService;
        private readonly ITileService _tileService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the board and tile services and server lifetime it streams from
        /// </summary>
        public BoardGrpcHandler(IBoardService boardService, ITileService tileService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _boardService = boardService;
            _tileService = tileService;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Returns the whole current board. Fails with UNAVAILABLE if the game hasn't created a board yet.
        /// </summary>
        public override Task<BoardState> GetBoard(Empty request, ServerCallContext context)
        {
            GameBoard? board = _boardService.GetBoard();
            if (board is null)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "The board hasn't been created yet"));
            }
            return Task.FromResult(ToBoardState(board));
        }

        /// <summary>
        /// Streams the board to one client: a full snapshot first, then tile changes and any new boards, until the client disconnects
        /// </summary>
        public override async Task JoinBoardStream(Empty request, IServerStreamWriter<BoardEvent> responseStream, ServerCallContext context)
        {
            // Holds GameBoard snapshots and TilesChange updates, in the order they happened
            Channel<object> outbox = System.Threading.Channels.Channel.CreateBounded<object>(new BoundedChannelOptions(OutboxCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full, which triggers a resync
                SingleReader = true,
                SingleWriter = false
            });
            int resyncNeeded = 0;

            // gRPC doesn't allow concurrent writes to one stream, so board events are only queued here
            // and this call is the single writer to the client.
            void Enqueue(object item)
            {
                if (!outbox.Writer.TryWrite(item))
                {
                    Interlocked.Exchange(ref resyncNeeded, 1);
                }
            }
            void OnBoardCreated(object? sender, GameBoard board) => Enqueue(board);
            void OnTilesChanged(object? sender, TilesChange change) => Enqueue(change);

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            // Subscribe before taking the snapshot so no change can fall between them.
            // Changes already included in the snapshot are skipped below by version.
            _boardService.BoardCreated += OnBoardCreated;
            _boardService.TilesChanged += OnTilesChanged;
            _logger.Information("Client {Peer} joined the board stream", context.Peer);

            Guid? sentBoardId = null;
            long sentVersion = 0;

            try
            {
                GameBoard? current = _boardService.GetBoard();
                if (current is not null)
                {
                    Enqueue(current);
                }

                await foreach (object item in outbox.Reader.ReadAllAsync(token))
                {
                    object next = item;

                    if (Interlocked.Exchange(ref resyncNeeded, 0) == 1)
                    {
                        // The client fell behind and changes were dropped: throw away the backlog and resend the whole board
                        while (outbox.Reader.TryRead(out _)) { }
                        GameBoard? latest = _boardService.GetBoard();
                        if (latest is null)
                        {
                            continue;
                        }
                        _logger.Warning("Client {Peer} fell behind on the board stream; resending the board", context.Peer);
                        next = latest;
                    }

                    switch (next)
                    {
                        case GameBoard board when board.Id != sentBoardId || board.Version > sentVersion:
                            sentBoardId = board.Id;
                            sentVersion = board.Version;
                            await responseStream.WriteAsync(new BoardEvent() { Board = ToBoardState(board) });
                            break;

                        // Skip changes for a board the client doesn't have, or already included in what it was sent
                        case TilesChange change when change.BoardId == sentBoardId && change.Version > sentVersion:
                            sentVersion = change.Version;
                            await responseStream.WriteAsync(new BoardEvent() { TilesChanged = ToTilesChanged(change) });
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
            }
            finally
            {
                _boardService.BoardCreated -= OnBoardCreated;
                _boardService.TilesChanged -= OnTilesChanged;
                outbox.Writer.TryComplete();
                _logger.Information("Client {Peer} left the board stream", context.Peer);
            }
        }

        private BoardState ToBoardState(GameBoard board)
        {
            BoardState state = new BoardState()
            {
                BoardId = board.Id.ToString(),
                Version = board.Version,
                Width = board.Width,
                Depth = board.Depth,
                Height = board.Height
            };
            // Only positions that hold a tile are sent; everything else is empty
            state.Tiles.AddRange(board.Tiles.OfType<MapTile>().Select(ToProto));
            return state;
        }

        private TilesChanged ToTilesChanged(TilesChange change)
        {
            TilesChanged message = new TilesChanged()
            {
                BoardId = change.BoardId.ToString(),
                Version = change.Version
            };
            message.Tiles.AddRange(change.Placed.Select(ToProto));
            message.Removed.AddRange(change.Removed.Select(ToProto));
            return message;
        }

        private Tile ToProto(MapTile tile)
        {
            return new Tile()
            {
                Position = ToProto(tile.Position),
                Type = ToProto(tile.Type),
                Passable = _tileService.IsPassable(tile)
            };
        }

        private static ProtoBoardPosition ToProto(BoardPosition position)
        {
            return new ProtoBoardPosition()
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z
            };
        }

        private static ProtoTileType ToProto(TileType type) => type switch
        {
            TileType.Water => ProtoTileType.Water,
            TileType.Plains => ProtoTileType.Plains,
            TileType.Forest => ProtoTileType.Forest,
            TileType.Hills => ProtoTileType.Hills,
            TileType.Mountain => ProtoTileType.Mountain,
            TileType.Stone => ProtoTileType.Stone,
            _ => ProtoTileType.Unspecified
        };
    }
}
