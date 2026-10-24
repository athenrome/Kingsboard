using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcPawn;
using Kingsboard.Networking;
using UnityEngine;
using BoardPosition = Kingsboard.Shared.Models.Board.BoardPosition;
using Card = Kingsboard.Shared.Models.Cards.Card;
using CardSelection = Kingsboard.Shared.Models.Cards.CardSelection;
using CardTargetType = Kingsboard.Shared.Models.Enumerations.CardTargetType;
using Pawn = Kingsboard.Shared.Models.Pawns.Pawn;
using PawnAttributes = Kingsboard.Shared.Models.Pawns.PawnAttributes;
using PawnMovement = Kingsboard.Shared.Models.Pawns.PawnMovement;
using PawnSpeech = Kingsboard.Shared.Models.Pawns.PawnSpeech;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Manages the gRPC communication with the server's pawns. Nothing outside this class knows about gRPC: the rest of
    /// the game only sees <see cref="Pawn"/>, <see cref="Card"/>, <see cref="PawnMovement"/>, <see cref="PawnSpeech"/> and the events below.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class PawnGrpcHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;
        private const double RequestTimeoutSeconds = 10;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _connected;

        /// <summary>
        /// Raised with every pawn when we connect and whenever the server spawns a new set. It replaces all earlier pawns.
        /// </summary>
        public event Action<IReadOnlyList<Pawn>> PawnsReset;

        /// <summary>
        /// Raised with the new state of some pawns when nothing else about them needs showing: hands drawn (Upkeep),
        /// a player changing a pawn's card (only that pawn), or pawns changing owner
        /// </summary>
        public event Action<IReadOnlyList<Pawn>> PawnsUpdated;

        /// <summary>
        /// Raised in the Planning phase with every pawn, the card it chose and its planned path (the turn id comes first)
        /// </summary>
        public event Action<int, IReadOnlyList<Pawn>> PawnsPlanned;

        /// <summary>
        /// Raised in the Resolution phase with how each pawn that moved got there, what pawns said, and every pawn
        /// afterwards (the turn id comes first)
        /// </summary>
        public event Action<int, IReadOnlyList<PawnMovement>, IReadOnlyList<PawnSpeech>, IReadOnlyList<Pawn>> PawnsResolved;

        /// <summary>
        /// Raised in the End phase with every pawn after its hand went back into its deck (the turn id comes first)
        /// </summary>
        public event Action<int, IReadOnlyList<Pawn>> TurnEnded;

        /// <summary>
        /// Raised when the connection to the pawns is lost. It reconnects by itself.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Starts listening for pawns, reconnecting whenever the connection drops. After each reconnect the server sends every pawn again.
        /// </summary>
        public async void Connect()
        {
            CancellationToken token = _shutdown.Token;
            GrpcPawn.PawnService.PawnServiceClient client = new GrpcPawn.PawnService.PawnServiceClient(GrpcConnection.Channel);
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<PawnEvent> call = client.JoinPawnStream(new Empty(), cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        PawnEvent pawnEvent = call.ResponseStream.Current;

                        switch (pawnEvent.PayloadCase)
                        {
                            case PawnEvent.PayloadOneofCase.Snapshot:
                                _connected = true;
                                logFailure = true;
                                PawnsReset?.Invoke(pawnEvent.Snapshot.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.Spawned:
                                PawnsReset?.Invoke(pawnEvent.Spawned.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.CardsDrawn:
                                PawnsUpdated?.Invoke(pawnEvent.CardsDrawn.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.Planned:
                                PawnsPlanned?.Invoke(pawnEvent.Planned.TurnId, pawnEvent.Planned.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.CardSelected:
                                if (pawnEvent.CardSelected.Pawn != null)
                                {
                                    PawnsUpdated?.Invoke(new[] { ToPawn(pawnEvent.CardSelected.Pawn) });
                                }
                                break;

                            case PawnEvent.PayloadOneofCase.Resolved:
                                PawnsResolved?.Invoke(pawnEvent.Resolved.TurnId,
                                    pawnEvent.Resolved.Movements.Select(ToPawnMovement).ToList(),
                                    pawnEvent.Resolved.Speeches.Select(ToPawnSpeech).ToList(),
                                    pawnEvent.Resolved.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.CardsReturned:
                                TurnEnded?.Invoke(pawnEvent.CardsReturned.TurnId, pawnEvent.CardsReturned.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.OwnersChanged:
                                PawnsUpdated?.Invoke(pawnEvent.OwnersChanged.Pawns.Select(ToPawn).ToList());
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
                        Debug.LogWarning($"[Pawns] Not connected to the pawn server, retrying: {ex.Message}");
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
        /// Asks the server for all of one pawn's details. Returns null if no pawn has that id or the request fails
        /// (failures other than "not found" are logged). Continues on the main thread.
        /// </summary>
        public async Task<Pawn> GetPawnByIdAsync(Guid id)
        {
            GrpcPawn.PawnService.PawnServiceClient client = new GrpcPawn.PawnService.PawnServiceClient(GrpcConnection.Channel);
            try
            {
                GetPawnByIdRequest request = new GetPawnByIdRequest() { Id = id.ToString() };
                GrpcPawn.Pawn pawn = await client.GetPawnByIdAsync(request, deadline: DateTime.UtcNow.AddSeconds(RequestTimeoutSeconds), cancellationToken: _shutdown.Token);
                return ToPawn(pawn);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
            catch (RpcException ex)
            {
                if (!_shutdown.IsCancellationRequested)
                {
                    Debug.LogWarning($"[Pawns] Could not get pawn {id}: {ex.Status.Detail}");
                }
                return null;
            }
        }

        /// <summary>
        /// Asks the server to have a pawn we control play <paramref name="cardId"/> this turn, aimed at
        /// <paramref name="target"/> (null for cards that need no target). <paramref name="sessionId"/> is our player
        /// session. Returns null if the server accepted it, or the server's reason if it didn't (e.g. it isn't the Player
        /// phase). The new choice arrives on the pawn stream either way. Continues on the main thread.
        /// </summary>
        public async Task<string> SelectCardAsync(Guid sessionId, Guid pawnId, Guid cardId, BoardPosition target)
        {
            GrpcPawn.PawnService.PawnServiceClient client = new GrpcPawn.PawnService.PawnServiceClient(GrpcConnection.Channel);
            try
            {
                SelectCardRequest request = new SelectCardRequest()
                {
                    SessionId = sessionId.ToString(),
                    PawnId = pawnId.ToString(),
                    CardId = cardId.ToString(),
                    Target = target == null ? null : new GrpcBoard.BoardPosition() { X = target.X, Y = target.Y, Z = target.Z }
                };
                await client.SelectCardAsync(request, deadline: DateTime.UtcNow.AddSeconds(RequestTimeoutSeconds), cancellationToken: _shutdown.Token);
                return null;
            }
            catch (RpcException ex)
            {
                return string.IsNullOrEmpty(ex.Status.Detail) ? ex.StatusCode.ToString() : ex.Status.Detail;
            }
        }

        /// <summary>
        /// Stops listening (closing the stream is what tells the server we've gone)
        /// </summary>
        public void Dispose()
        {
            _shutdown.Cancel();
        }

        private static Pawn ToPawn(GrpcPawn.Pawn pawn)
        {
            return new Pawn()
            {
                Id = ToGuid(pawn.Id),
                Name = pawn.Name,
                Colour = pawn.Colour,
                Position = ToBoardPosition(pawn.Position),
                PlannedPath = pawn.PlannedPath.Select(ToBoardPosition).ToList(),
                Attributes = ToPawnAttributes(pawn.Attributes),
                Health = pawn.Health,
                MaxHealth = pawn.MaxHealth,
                Deck = pawn.Deck.Select(ToCard).ToList(),
                Hand = pawn.Hand.Select(ToCard).ToList(),
                SelectedCard = ToCardSelection(pawn.SelectedCard),
                OwnerId = Guid.TryParse(pawn.OwnerId, out Guid ownerId) ? ownerId : (Guid?)null,
                OwnerName = pawn.OwnerName,
                OwnerColour = pawn.OwnerColour
            };
        }

        private static Card ToCard(GrpcCard.Card card)
        {
            return new Card()
            {
                Id = ToGuid(card.Id),
                DefinitionId = card.DefinitionId,
                Name = card.Name,
                Description = card.Description,
                ImageId = card.ImageId,
                TargetType = card.TargetType switch
                {
                    GrpcCard.CardTargetType.None => CardTargetType.None,
                    GrpcCard.CardTargetType.Tile => CardTargetType.Tile,
                    _ => CardTargetType.Unknown
                }
            };
        }

        // An absent selection arrives as null: the pawn has no card chosen
        private static CardSelection ToCardSelection(GrpcCard.CardSelection selection)
        {
            if (selection == null)
            {
                return null;
            }
            return new CardSelection(ToGuid(selection.CardId), selection.Target == null ? null : ToBoardPosition(selection.Target));
        }

        private static PawnSpeech ToPawnSpeech(GrpcPawn.PawnSpeech speech)
        {
            return new PawnSpeech(ToGuid(speech.PawnId), speech.Text);
        }

        private static PawnAttributes ToPawnAttributes(GrpcPawn.PawnAttributes attributes)
        {
            // A missing message field arrives as null; proto3 treats that as all-zero
            if (attributes == null)
            {
                return new PawnAttributes();
            }

            return new PawnAttributes()
            {
                Strength = attributes.Strength,
                Dexterity = attributes.Dexterity,
                Intelligence = attributes.Intelligence,
                Wisdom = attributes.Wisdom,
                Constitution = attributes.Constitution,
                Physique = attributes.Physique
            };
        }

        private static PawnMovement ToPawnMovement(GrpcPawn.PawnMovement movement)
        {
            return new PawnMovement(ToGuid(movement.PawnId), movement.Path.Select(ToBoardPosition).ToList(), ToBoardPosition(movement.Position), movement.Blocked);
        }

        // The server always sends ids as Guids; anything else becomes Guid.Empty, which matches no pawn
        private static Guid ToGuid(string id)
        {
            return Guid.TryParse(id, out Guid guid) ? guid : Guid.Empty;
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
    }
}
