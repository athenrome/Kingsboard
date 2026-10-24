using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using GrpcBoard;
using Kingsboard.Networking;
using UnityEngine;
using BoardPosition = Kingsboard.Shared.Models.Board.BoardPosition;
using BoardRegion = Kingsboard.Shared.Models.Board.BoardRegion;
using BoardSection = Kingsboard.Board.BoardSection;
using MapTile = Kingsboard.Shared.Models.Board.MapTile;
using TileType = Kingsboard.Shared.Models.Enumerations.TileType;

namespace Kingsboard.Board
{
    /// <summary>
    /// Manages the gRPC communication with the server's board. The board is too big to send whole, so the client views
    /// one section at a time and asks for another with <see cref="RequestSectionAsync"/>. Nothing outside this class knows
    /// about gRPC: the rest of the game only sees <see cref="BoardSection"/>, <see cref="MapTile"/> and the events below.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class BoardGrpcHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;
        private const double RequestTimeoutSeconds = 10;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private GrpcBoard.BoardService.BoardServiceClient _client;
        private string _sessionId;
        private BoardRegion _region;

        /// <summary>
        /// Largest width or depth of a section the server allows (0 until joined)
        /// </summary>
        public int MaxSectionSize { get; private set; }

        /// <summary>
        /// Width and depth of the server's default section (0 until joined)
        /// </summary>
        public int DefaultSectionSize { get; private set; }

        /// <summary>
        /// Raised with a whole section when we connect, when a requested section arrives, and whenever the server
        /// recreates the board. It replaces any section received before.
        /// </summary>
        public event Action<BoardSection> SectionReceived;

        /// <summary>
        /// Raised when tiles inside the current section change: the board id, the new board version, the tiles placed
        /// or replaced, and the positions whose tiles were removed
        /// </summary>
        public event Action<string, long, IReadOnlyList<MapTile>, IReadOnlyList<BoardPosition>> TilesChanged;

        /// <summary>
        /// Raised when the connection to the board is lost. It reconnects by itself, back to the same section.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Starts viewing the board at <paramref name="region"/> (or the server's default section if null),
        /// reconnecting to the latest section whenever the connection drops
        /// </summary>
        public async void Connect(BoardRegion region = null)
        {
            CancellationToken token = _shutdown.Token;
            _client = new GrpcBoard.BoardService.BoardServiceClient(GrpcConnection.Channel);
            _region = region;
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    JoinBoardStreamRequest request = new JoinBoardStreamRequest();
                    if (_region is BoardRegion current)
                    {
                        request.Region = ToProto(current);
                    }

                    using AsyncServerStreamingCall<BoardEvent> call = _client.JoinBoardStream(request, cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        BoardEvent boardEvent = call.ResponseStream.Current;

                        switch (boardEvent.PayloadCase)
                        {
                            case BoardEvent.PayloadOneofCase.Joined:
                                _sessionId = boardEvent.Joined.SessionId;
                                MaxSectionSize = boardEvent.Joined.MaxSectionSize;
                                DefaultSectionSize = boardEvent.Joined.DefaultSectionSize;
                                logFailure = true;
                                break;

                            case BoardEvent.PayloadOneofCase.Section:
                                BoardSection section = ToBoardSection(boardEvent.Section);
                                // Remember where we are, so a reconnect comes back to the same section
                                _region = section.Region;
                                SectionReceived?.Invoke(section);
                                break;

                            case BoardEvent.PayloadOneofCase.TilesChanged:
                                GrpcBoard.TilesChangedEvent changed = boardEvent.TilesChanged;
                                TilesChanged?.Invoke(changed.BoardId, changed.Version, changed.Tiles.Select(ToMapTile).ToList(), changed.Removed.Select(ToBoardPosition).ToList());
                                break;
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

                if (_sessionId != null)
                {
                    _sessionId = null;
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
        /// Asks the server for a different section; it arrives through <see cref="SectionReceived"/>.
        /// The server moves a region that hangs over the board's edge back onto it.
        /// Returns null when the request was accepted, otherwise the reason it wasn't.
        /// </summary>
        public async Task<string> RequestSectionAsync(BoardRegion region)
        {
            if (_sessionId == null)
            {
                return "Not connected to the board";
            }

            try
            {
                SetBoardSectionRequest request = new SetBoardSectionRequest() { SessionId = _sessionId, Region = ToProto(region) };
                await _client.SetBoardSectionAsync(request, deadline: DateTime.UtcNow.AddSeconds(RequestTimeoutSeconds), cancellationToken: _shutdown.Token);
                return null;
            }
            catch (RpcException ex)
            {
                return string.IsNullOrEmpty(ex.Status.Detail) ? "Section could not be loaded" : ex.Status.Detail;
            }
        }

        /// <summary>
        /// Stops viewing the board (closing the stream is what tells the server we've gone)
        /// </summary>
        public void Dispose()
        {
            _shutdown.Cancel();
            _sessionId = null;
        }

        private static BoardSection ToBoardSection(GrpcBoard.BoardSection message)
        {
            BoardSection section = new BoardSection()
            {
                Id = message.BoardId,
                Version = message.Version,
                BoardWidth = message.BoardWidth,
                BoardDepth = message.BoardDepth,
                BoardHeight = message.BoardHeight,
                Region = ToBoardRegion(message.Region)
            };
            BoardService.PlaceTiles(section, message.Tiles.Select(ToMapTile));
            return section;
        }

        private static MapTile ToMapTile(Tile tile)
        {
            return new MapTile(ToBoardPosition(tile.Position), ToTileType(tile.Type));
        }

        private static BoardRegion ToBoardRegion(GrpcBoard.BoardRegion region)
        {
            if (region == null)
            {
                return new BoardRegion(new BoardPosition(0, 0, 0), 0, 0);
            }

            return new BoardRegion(ToBoardPosition(region.Origin), region.Width, region.Depth);
        }

        private static BoardPosition ToBoardPosition(GrpcBoard.BoardPosition position)
        {
            // A missing message field arrives as null; proto3 treats that as all-zero
            if (position == null)
            {
                return new BoardPosition(0, 0, 0);
            }

            return new BoardPosition(position.X, position.Y, position.Z);
        }

        private static GrpcBoard.BoardRegion ToProto(BoardRegion region)
        {
            return new GrpcBoard.BoardRegion()
            {
                Origin = new GrpcBoard.BoardPosition() { X = region.Origin.X, Y = region.Origin.Y },
                Width = region.Width,
                Depth = region.Depth
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
