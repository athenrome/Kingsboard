using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcPawn;
using KingsboardServer.Events.Pawns;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Models.Pawns;
using KingsboardServer.Services.Interfaces;
using BoardPosition = KingsboardServer.Models.Board.BoardPosition;
using Pawn = KingsboardServer.Models.Pawns.Pawn;
using PawnMovement = KingsboardServer.Models.Pawns.PawnMovement;
using ProtoBoardPosition = GrpcBoard.BoardPosition;
using ProtoPawn = GrpcPawn.Pawn;
using ProtoPawnMovement = GrpcPawn.PawnMovement;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles pawn requests from clients (gRPC). All pawn logic lives in <see cref="IPawnService"/>.
    /// </summary>
    public class PawnGrpcHandler : PawnService.PawnServiceBase
    {
        // Per-client buffer of pawn events. If a client falls this far behind, its queued events are thrown away
        // and it is sent a fresh snapshot instead, so it never ends up with stale pawns.
        private const int OutboxCapacity = 64;

        private readonly IPawnService _pawnService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the pawn service and server lifetime it streams from
        /// </summary>
        public PawnGrpcHandler(IPawnService pawnService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _pawnService = pawnService;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Returns every pawn
        /// </summary>
        public override Task<PawnList> GetPawns(Empty request, ServerCallContext context)
        {
            PawnList list = new PawnList();
            list.Pawns.AddRange(_pawnService.GetPawns().Select(ToProto));
            return Task.FromResult(list);
        }

        /// <summary>
        /// Streams the pawns to one client: a snapshot first, then every spawn, plan and move, until the client disconnects
        /// </summary>
        public override async Task JoinPawnStream(Empty request, IServerStreamWriter<PawnEvent> responseStream, ServerCallContext context)
        {
            Channel<PawnEvent> outbox = System.Threading.Channels.Channel.CreateBounded<PawnEvent>(new BoundedChannelOptions(OutboxCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full, which triggers a resync
                SingleReader = false, // The resync drains the queue while this call is reading it
                SingleWriter = false
            });

            // Runs under the pawn lock, so the snapshot used for a resync is consistent with the events around it
            void Queue(PawnEvent pawnEvent)
            {
                if (outbox.Writer.TryWrite(pawnEvent))
                {
                    return;
                }

                while (outbox.Reader.TryRead(out _)) { }
                outbox.Writer.TryWrite(ToSnapshot(_pawnService.GetPawns()));
                _logger.Warning("Client {Peer} fell behind on the pawn stream; resending every pawn", context.Peer);
            }
            void OnPawnsChanged(object? sender, PawnsChangeEvent change) => Queue(ToEvent(change));

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            // Subscribe and take the snapshot together, so no change falls between them
            _pawnService.ReadPawns(pawns =>
            {
                _pawnService.PawnsChanged += OnPawnsChanged;
                Queue(ToSnapshot(pawns));
            });
            _logger.Information("Client {Peer} joined the pawn stream", context.Peer);

            try
            {
                // Only this call writes to the stream (gRPC doesn't allow concurrent writes)
                await foreach (PawnEvent pawnEvent in outbox.Reader.ReadAllAsync(token))
                {
                    await responseStream.WriteAsync(pawnEvent);
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
                _logger.Debug("Pawn stream for {Peer} closed: {Reason}", context.Peer, ex.Message);
            }
            finally
            {
                _pawnService.PawnsChanged -= OnPawnsChanged;
                outbox.Writer.TryComplete();
                _logger.Information("Client {Peer} left the pawn stream", context.Peer);
            }
        }

        private static PawnEvent ToSnapshot(IReadOnlyList<Pawn> pawns)
        {
            PawnsSnapshotEvent snapshot = new PawnsSnapshotEvent();
            snapshot.Pawns.AddRange(pawns.Select(ToProto));
            return new PawnEvent() { Snapshot = snapshot };
        }

        private static PawnEvent ToEvent(PawnsChangeEvent change)
        {
            switch (change.Reason)
            {
                case PawnChangeReason.Spawned:
                    PawnsSpawnedEvent spawned = new PawnsSpawnedEvent();
                    spawned.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Spawned = spawned };

                case PawnChangeReason.Planned:
                    PawnsPlannedEvent planned = new PawnsPlannedEvent() { TurnId = change.TurnId };
                    planned.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Planned = planned };

                default:
                    PawnsMovedEvent moved = new PawnsMovedEvent() { TurnId = change.TurnId };
                    moved.Movements.AddRange(change.Movements.Select(ToProto));
                    moved.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Moved = moved };
            }
        }

        private static ProtoPawn ToProto(Pawn pawn)
        {
            ProtoPawn message = new ProtoPawn()
            {
                Id = pawn.Id.ToString(),
                Name = pawn.Name,
                Colour = pawn.Colour,
                Position = ToProto(pawn.Position)
            };
            message.PlannedPath.AddRange(pawn.PlannedPath.Select(ToProto));
            return message;
        }

        private static ProtoPawnMovement ToProto(PawnMovement movement)
        {
            ProtoPawnMovement message = new ProtoPawnMovement()
            {
                PawnId = movement.PawnId.ToString(),
                Position = ToProto(movement.Position),
                Blocked = movement.Blocked
            };
            message.Path.AddRange(movement.Path.Select(ToProto));
            return message;
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
    }
}
