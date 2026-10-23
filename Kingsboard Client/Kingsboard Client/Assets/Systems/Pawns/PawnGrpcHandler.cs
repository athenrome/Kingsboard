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
using BoardPosition = Kingsboard.Board.BoardPosition;
using Pawn = Kingsboard.Pawns.Pawn;
using PawnAttributes = Kingsboard.Pawns.PawnAttributes;
using PawnMovement = Kingsboard.Pawns.PawnMovement;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Manages the gRPC communication with the server's pawns. Nothing outside this class knows about gRPC: the rest of
    /// the game only sees <see cref="Pawn"/>, <see cref="PawnMovement"/> and the events below.
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
        /// Raised in the Planning phase with every pawn and its planned path for the turn (the turn id comes first)
        /// </summary>
        public event Action<int, IReadOnlyList<Pawn>> PawnsPlanned;

        /// <summary>
        /// Raised in the Resolution phase with how each pawn moved and every pawn afterwards (the turn id comes first)
        /// </summary>
        public event Action<int, IReadOnlyList<PawnMovement>, IReadOnlyList<Pawn>> PawnsMoved;

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

                            case PawnEvent.PayloadOneofCase.Planned:
                                PawnsPlanned?.Invoke(pawnEvent.Planned.TurnId, pawnEvent.Planned.Pawns.Select(ToPawn).ToList());
                                break;

                            case PawnEvent.PayloadOneofCase.Moved:
                                PawnsMoved?.Invoke(pawnEvent.Moved.TurnId, pawnEvent.Moved.Movements.Select(ToPawnMovement).ToList(), pawnEvent.Moved.Pawns.Select(ToPawn).ToList());
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
        public async Task<Pawn> GetPawnByIdAsync(string id)
        {
            GrpcPawn.PawnService.PawnServiceClient client = new GrpcPawn.PawnService.PawnServiceClient(GrpcConnection.Channel);
            try
            {
                GetPawnByIdRequest request = new GetPawnByIdRequest() { Id = id ?? string.Empty };
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
                Id = pawn.Id,
                Name = pawn.Name,
                Colour = ColorUtility.TryParseHtmlString(pawn.Colour, out Color colour) ? colour : Color.white,
                Position = ToBoardPosition(pawn.Position),
                PlannedPath = pawn.PlannedPath.Select(ToBoardPosition).ToList(),
                Attributes = ToPawnAttributes(pawn.Attributes),
                Health = pawn.Health,
                MaxHealth = pawn.MaxHealth
            };
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
            return new PawnMovement()
            {
                PawnId = movement.PawnId,
                Path = movement.Path.Select(ToBoardPosition).ToList(),
                Position = ToBoardPosition(movement.Position),
                Blocked = movement.Blocked
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
    }
}
