using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcTurnTimer;
using KingsboardServer.Models;
using KingsboardServer.Services.Interfaces;
using ModelTurnPhase = KingsboardServer.Models.Enumerations.TurnPhase;
using ProtoTurnPhase = GrpcTurnTimer.TurnPhase;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles turn timer requests from clients (gRPC). Broadcasts the global turn timer (turn started, each phase started, turn ended) to every connected client.
    /// </summary>
    public class TurnTimerGrpcHandler : TurnTimerService.TurnTimerServiceBase
    {
        // Per-client buffer. If a client falls this far behind, its oldest events are dropped
        // rather than blocking the turn loop or other clients.
        private const int OutboxCapacity = 32;

        private readonly ITurnService _turnService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the turn service and server lifetime it uses
        /// </summary>
        public TurnTimerGrpcHandler(ITurnService turnService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _turnService = turnService;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Returns the turn in progress (turn_id 0 if no turn has started yet)
        /// </summary>
        public override Task<TurnState> GetCurrentTurn(Empty request, ServerCallContext context)
        {
            Turn? turn = _turnService.GetCurrentTurn();
            return Task.FromResult(turn is null ? new TurnState() : ToTurnState(turn));
        }

        /// <summary>
        /// Streams turn events to one client: a sync for the current turn, then every turn start and end, until the client disconnects
        /// </summary>
        public override async Task JoinTurnStream(Empty request, IServerStreamWriter<TurnEvent> responseStream, ServerCallContext context)
        {
            Channel<TurnEvent> outbox = System.Threading.Channels.Channel.CreateBounded<TurnEvent>(new BoundedChannelOptions(OutboxCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            // gRPC doesn't allow concurrent writes to one stream, so the turn loop only queues events
            // and this call is the single writer to the client.
            void OnTurnStarted(object? sender, Turn turn) => outbox.Writer.TryWrite(CreateEvent(TurnEventType.Started, turn));
            void OnTurnEnded(object? sender, Turn turn) => outbox.Writer.TryWrite(CreateEvent(TurnEventType.Ended, turn));
            void OnPhaseStarted(object? sender, Turn turn) => outbox.Writer.TryWrite(CreateEvent(TurnEventType.PhaseStarted, turn));

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            _turnService.TurnStarted += OnTurnStarted;
            _turnService.TurnEnded += OnTurnEnded;
            _turnService.PhaseStarted += OnPhaseStarted;
            _logger.Information("Client {Peer} joined the turn stream", context.Peer);

            try
            {
                // Let late joiners sync to the turn already in progress.
                // (Clients should ignore events for a turn id older than the one they're on.)
                Turn? current = _turnService.GetCurrentTurn();
                if (current is not null)
                {
                    outbox.Writer.TryWrite(CreateEvent(TurnEventType.Sync, current));
                }

                await foreach (TurnEvent turnEvent in outbox.Reader.ReadAllAsync(token))
                {
                    await responseStream.WriteAsync(turnEvent);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
            }
            finally
            {
                _turnService.TurnStarted -= OnTurnStarted;
                _turnService.TurnEnded -= OnTurnEnded;
                _turnService.PhaseStarted -= OnPhaseStarted;
                outbox.Writer.TryComplete();
                _logger.Information("Client {Peer} left the turn stream", context.Peer);
            }
        }

        private TurnEvent CreateEvent(TurnEventType type, Turn turn)
        {
            return new TurnEvent()
            {
                Type = type,
                Turn = ToTurnState(turn)
            };
        }

        private TurnState ToTurnState(Turn turn)
        {
            return new TurnState()
            {
                TurnId = turn.Id,
                StartedAt = Timestamp.FromDateTimeOffset(turn.StartedAt),
                EndsAt = Timestamp.FromDateTimeOffset(turn.EndsAt),
                TurnDurationMs = (int)_turnService.GetDuration(turn).TotalMilliseconds,
                TimeRemainingMs = (int)_turnService.GetTimeRemaining(turn).TotalMilliseconds,
                Phase = ToProto(turn.CurrentPhase)
            };
        }

        private static ProtoTurnPhase ToProto(ModelTurnPhase phase) => phase switch
        {
            ModelTurnPhase.Start => ProtoTurnPhase.Start,
            ModelTurnPhase.Upkeep => ProtoTurnPhase.Upkeep,
            ModelTurnPhase.Planning => ProtoTurnPhase.Planning,
            ModelTurnPhase.Player => ProtoTurnPhase.Player,
            ModelTurnPhase.Resolution => ProtoTurnPhase.Resolution,
            ModelTurnPhase.End => ProtoTurnPhase.End,
            _ => ProtoTurnPhase.Unspecified
        };
    }
}
