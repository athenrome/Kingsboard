using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcBoard;
using KingsboardServer.Models.Board;
using KingsboardServer.Services.Interfaces;
using BoardPosition = KingsboardServer.Models.Board.BoardPosition;
using BoardRegion = KingsboardServer.Models.Board.BoardRegion;
using BoardSection = KingsboardServer.Models.Board.BoardSection;
using ProtoBoardPosition = GrpcBoard.BoardPosition;
using ProtoBoardRegion = GrpcBoard.BoardRegion;
using ProtoBoardSection = GrpcBoard.BoardSection;
using ProtoTileType = GrpcBoard.TileType;
using TileType = KingsboardServer.Models.Enumerations.TileType;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles board requests from clients (gRPC). Which section each client sees lives in <see cref="IBoardViewService"/>,
    /// and all board rules live in <see cref="IBoardService"/>.
    /// </summary>
    public class BoardGrpcHandler : GrpcBoard.BoardService.BoardServiceBase
    {
        private readonly IBoardViewService _viewService;
        private readonly ITileService _tileService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the view and tile services and server lifetime it uses
        /// </summary>
        public BoardGrpcHandler(IBoardViewService viewService, ITileService tileService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _viewService = viewService;
            _tileService = tileService;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Joins the client to the board view and streams its section: a `joined` event first, then the section,
        /// then changes inside it and any new sections, until the client disconnects (which leaves the view)
        /// </summary>
        public override async Task JoinBoardStream(JoinBoardStreamRequest request, IServerStreamWriter<BoardEvent> responseStream, ServerCallContext context)
        {
            BoardRegion region = request.Region is null ? _viewService.DefaultRegion : FromProto(request.Region);

            BoardViewSubscription subscription;
            try
            {
                subscription = _viewService.Join(region);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            try
            {
                await responseStream.WriteAsync(new BoardEvent()
                {
                    Joined = new BoardJoined()
                    {
                        SessionId = subscription.SessionId.ToString(),
                        MaxSectionSize = _viewService.MaxSectionSize,
                        DefaultSectionSize = _viewService.DefaultSectionSize
                    }
                });

                // Only this call writes to the stream (gRPC doesn't allow concurrent writes)
                await foreach (BoardViewEvent viewEvent in subscription.Events.ReadAllAsync(token))
                {
                    if (viewEvent.Section is BoardSection section)
                    {
                        await responseStream.WriteAsync(new BoardEvent() { Section = ToProto(section) });
                    }
                    else if (viewEvent.Change is TilesChange change)
                    {
                        await responseStream.WriteAsync(new BoardEvent() { TilesChanged = ToProto(change) });
                    }
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
                _logger.Debug("Board stream for {SessionId} closed: {Reason}", subscription.SessionId, ex.Message);
            }
            finally
            {
                _viewService.Leave(subscription.SessionId);
            }
        }

        /// <summary>
        /// Moves a joined client to a different section; the section is sent on its stream.
        /// Fails with FAILED_PRECONDITION if the session isn't joined, or INVALID_ARGUMENT if the region is missing or the wrong size.
        /// </summary>
        public override Task<Empty> SetBoardSection(SetBoardSectionRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.SessionId, out Guid sessionId))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Missing or invalid session id. Join the board stream first."));
            }
            if (request.Region is null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "A region is required"));
            }

            bool joined;
            try
            {
                joined = _viewService.SetRegion(sessionId, FromProto(request.Region));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            if (!joined)
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "You are not viewing the board. Join the board stream first."));
            }
            return Task.FromResult(new Empty());
        }

        /// <summary>
        /// Returns one section of the board. Fails with UNAVAILABLE if the game hasn't created a board yet,
        /// or INVALID_ARGUMENT if the region is the wrong size.
        /// </summary>
        public override Task<ProtoBoardSection> GetBoardSection(ProtoBoardRegion request, ServerCallContext context)
        {
            BoardSection? section;
            try
            {
                section = _viewService.GetSection(FromProto(request));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            if (section is null)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "The board hasn't been created yet"));
            }
            return Task.FromResult(ToProto(section));
        }

        private ProtoBoardSection ToProto(BoardSection section)
        {
            ProtoBoardSection message = new ProtoBoardSection()
            {
                BoardId = section.BoardId.ToString(),
                Version = section.Version,
                BoardWidth = section.BoardWidth,
                BoardDepth = section.BoardDepth,
                BoardHeight = section.BoardHeight,
                Region = ToProto(section.Region)
            };
            message.Tiles.AddRange(section.Tiles.Select(ToProto));
            return message;
        }

        private TilesChanged ToProto(TilesChange change)
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

        private static ProtoBoardRegion ToProto(BoardRegion region)
        {
            return new ProtoBoardRegion()
            {
                Origin = ToProto(region.Origin),
                Width = region.Width,
                Depth = region.Depth
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

        private static BoardRegion FromProto(ProtoBoardRegion region)
        {
            // A missing origin arrives as null; proto3 treats that as (0, 0, 0)
            BoardPosition origin = region.Origin is null ? new BoardPosition(0, 0, 0) : new BoardPosition(region.Origin.X, region.Origin.Y, 0);
            return new BoardRegion(origin, region.Width, region.Depth);
        }
    }
}
