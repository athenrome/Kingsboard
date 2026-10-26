using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcPawn;
using KingsboardServer.Events.Pawns;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Models.Players;
using KingsboardServer.Services.Interfaces;
using BoardPosition = Kingsboard.Shared.Models.Board.BoardPosition;
using Card = Kingsboard.Shared.Models.Cards.Card;
using CardSelection = Kingsboard.Shared.Models.Cards.CardSelection;
using CardTargetType = Kingsboard.Shared.Models.Enumerations.CardTargetType;
using Pawn = Kingsboard.Shared.Models.Pawns.Pawn;
using PawnMovement = Kingsboard.Shared.Models.Pawns.PawnMovement;
using PawnSpeech = Kingsboard.Shared.Models.Pawns.PawnSpeech;
using ProtoBoardPosition = GrpcBoard.BoardPosition;
using PawnAttributes = Kingsboard.Shared.Models.Pawns.PawnAttributes;
using ProtoCard = GrpcCard.Card;
using ProtoCardSelection = GrpcCard.CardSelection;
using ProtoCardTargetType = GrpcCard.CardTargetType;
using ProtoPawn = GrpcPawn.Pawn;
using ProtoPawnAttributes = GrpcPawn.PawnAttributes;
using ProtoPawnMovement = GrpcPawn.PawnMovement;
using ProtoPawnSpeech = GrpcPawn.PawnSpeech;

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
        private readonly IPlayerService _playerService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the pawn service it streams from, the player service that identifies
        /// callers, and the server lifetime
        /// </summary>
        public PawnGrpcHandler(IPawnService pawnService, IPlayerService playerService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _pawnService = pawnService;
            _playerService = playerService;
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
        /// Returns all of one pawn's details. Throws InvalidArgument if the id isn't a valid pawn id, and NotFound if no pawn has it.
        /// </summary>
        public override Task<ProtoPawn> GetPawnById(GetPawnByIdRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.Id, out Guid id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid pawn id is required"));
            }

            Pawn pawn = _pawnService.GetPawnById(id) ?? throw new RpcException(new Status(StatusCode.NotFound, $"No pawn has id {id}"));
            return Task.FromResult(ToProto(pawn));
        }

        /// <summary>
        /// Chooses the card one of the caller's pawns will play this turn. Throws Unauthenticated for an unknown session,
        /// InvalidArgument for malformed ids, and FailedPrecondition (with the reason as its detail) when the pawn service
        /// turns the choice down.
        /// </summary>
        public override Task<Empty> SelectCard(SelectCardRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.SessionId, out Guid sessionId) || _playerService.GetPlayerBySession(sessionId) is not Player player)
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or unknown session id. Join the game first."));
            }
            if (!Guid.TryParse(request.PawnId, out Guid pawnId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid pawn id is required"));
            }
            if (!Guid.TryParse(request.CardId, out Guid cardId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid card id is required"));
            }

            BoardPosition? target = request.Target is null ? null : ToBoardPosition(request.Target);
            CardSelectResult result = _pawnService.SelectCard(player.PublicId, pawnId, cardId, target);

            if (!result.Selected)
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, result.Error ?? "The card could not be chosen"));
            }

            return Task.FromResult(new Empty());
        }

        /// <summary>
        /// Streams the pawns to one client: a snapshot first, then every change (spawns, draws, card choices, resolutions,
        /// returned hands and owner changes), until the client disconnects
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

        private PawnEvent ToSnapshot(IReadOnlyList<Pawn> pawns)
        {
            PawnsSnapshotEvent snapshot = new PawnsSnapshotEvent();
            snapshot.Pawns.AddRange(pawns.Select(ToProto));
            return new PawnEvent() { Snapshot = snapshot };
        }

        private PawnEvent ToEvent(PawnsChangeEvent change)
        {
            switch (change.Reason)
            {
                case PawnChangeReason.Spawned:
                    PawnsSpawnedEvent spawned = new PawnsSpawnedEvent();
                    spawned.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Spawned = spawned };

                case PawnChangeReason.CardsDrawn:
                    PawnsCardsDrawnEvent drawn = new PawnsCardsDrawnEvent() { TurnId = change.TurnId };
                    drawn.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { CardsDrawn = drawn };

                case PawnChangeReason.Planned:
                    PawnsPlannedEvent planned = new PawnsPlannedEvent() { TurnId = change.TurnId };
                    planned.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Planned = planned };

                case PawnChangeReason.CardSelected:
                    return new PawnEvent()
                    {
                        CardSelected = new PawnCardSelectedEvent() { TurnId = change.TurnId, Pawn = ToProto(change.Pawns[0]) }
                    };

                case PawnChangeReason.Resolved:
                    PawnsResolvedEvent resolved = new PawnsResolvedEvent() { TurnId = change.TurnId };
                    resolved.Movements.AddRange(change.Movements.Select(ToProto));
                    resolved.Speeches.AddRange(change.Speeches.Select(ToProto));
                    resolved.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { Resolved = resolved };

                case PawnChangeReason.CardsReturned:
                    PawnsCardsReturnedEvent returned = new PawnsCardsReturnedEvent() { TurnId = change.TurnId };
                    returned.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { CardsReturned = returned };

                default:
                    PawnsOwnersChangedEvent owners = new PawnsOwnersChangedEvent();
                    owners.Pawns.AddRange(change.Pawns.Select(ToProto));
                    return new PawnEvent() { OwnersChanged = owners };
            }
        }

        private ProtoPawn ToProto(Pawn pawn)
        {
            ProtoPawn message = new ProtoPawn()
            {
                Id = pawn.PublicId.ToString(),
                Name = pawn.Name,
                Colour = pawn.Colour,
                Position = ToProto(pawn.Position),
                Attributes = ToProto(pawn.Attributes),
                Health = pawn.Health,
                MaxHealth = pawn.MaxHealth,
                SelectedCard = pawn.SelectedCard is null ? null : ToProto(pawn.SelectedCard),
                OwnerId = pawn.OwnerId?.ToString() ?? string.Empty,
                OwnerName = pawn.OwnerName,
                OwnerColour = pawn.OwnerColour
            };
            message.PlannedPath.AddRange(pawn.PlannedPath.Select(ToProto));
            message.Deck.AddRange(pawn.Deck.Select(ToProto));
            message.Hand.AddRange(pawn.Hand.Select(ToProto));
            return message;
        }

        private static ProtoCard ToProto(Card card)
        {
            return new ProtoCard()
            {
                Id = card.PublicId.ToString(),
                DefinitionId = card.DefinitionId,
                Name = card.Name,
                Description = card.Description,
                ImageId = card.ImageId,
                TargetType = card.TargetType switch
                {
                    CardTargetType.None => ProtoCardTargetType.None,
                    CardTargetType.Tile => ProtoCardTargetType.Tile,
                    _ => ProtoCardTargetType.Unspecified
                }
            };
        }

        private static ProtoCardSelection ToProto(CardSelection selection)
        {
            return new ProtoCardSelection()
            {
                CardId = selection.CardId.ToString(),
                Target = selection.Target is null ? null : ToProto(selection.Target)
            };
        }

        private static ProtoPawnSpeech ToProto(PawnSpeech speech)
        {
            return new ProtoPawnSpeech()
            {
                PawnId = speech.PawnId.ToString(),
                Text = speech.Text
            };
        }

        private static BoardPosition ToBoardPosition(ProtoBoardPosition position)
        {
            return new BoardPosition(position.X, position.Y, position.Z);
        }

        private static ProtoPawnAttributes ToProto(PawnAttributes attributes)
        {
            return new ProtoPawnAttributes()
            {
                Strength = attributes.Strength,
                Dexterity = attributes.Dexterity,
                Intelligence = attributes.Intelligence,
                Wisdom = attributes.Wisdom,
                Constitution = attributes.Constitution,
                Physique = attributes.Physique
            };
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
