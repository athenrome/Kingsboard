using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcTurnTimer;
using Kingsboard.Networking;
using UnityEngine;
using TurnPhase = Kingsboard.Shared.Models.Enumerations.TurnPhase;

namespace Kingsboard.TurnTimer
{
    /// <summary>
    /// Manages the gRPC communication with the server's turn timer. Nothing outside this class knows about gRPC: the rest of
    /// the game only sees <see cref="TurnInfo"/> and the events below.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class TurnTimerGrpcHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _connected;

        /// <summary>
        /// Raised with the turn's state when we connect, when a turn starts or ends, and when each phase starts
        /// </summary>
        public event Action<TurnInfo> TurnUpdated;

        /// <summary>
        /// Raised when the connection to the turn timer is lost. It reconnects by itself.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Starts listening to the turn timer, reconnecting whenever the connection drops.
        /// After each (re)connect the server sends the turn in progress first.
        /// </summary>
        public async void Connect()
        {
            CancellationToken token = _shutdown.Token;
            TurnTimerService.TurnTimerServiceClient client = new TurnTimerService.TurnTimerServiceClient(GrpcConnection.Channel);
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<TurnEvent> call = client.JoinTurnStream(new Empty(), cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        TurnEvent turnEvent = call.ResponseStream.Current;
                        if (turnEvent.Turn == null)
                        {
                            continue;
                        }

                        _connected = true;
                        logFailure = true;
                        TurnUpdated?.Invoke(ToTurnInfo(turnEvent.Turn));
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
                        Debug.LogWarning($"[TurnTimer] Not connected to the turn timer, retrying: {ex.Message}");
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

        private static TurnInfo ToTurnInfo(TurnState state)
        {
            return new TurnInfo()
            {
                TurnId = state.TurnId,
                Phase = ToTurnPhase(state.Phase),
                DurationSeconds = state.TurnDurationMs / 1000f,
                RemainingSeconds = state.TimeRemainingMs / 1000f,
                ReceivedAt = Time.realtimeSinceStartup
            };
        }

        private static TurnPhase ToTurnPhase(GrpcTurnTimer.TurnPhase phase)
        {
            switch (phase)
            {
                case GrpcTurnTimer.TurnPhase.Start: return TurnPhase.Start;
                case GrpcTurnTimer.TurnPhase.Upkeep: return TurnPhase.Upkeep;
                case GrpcTurnTimer.TurnPhase.Planning: return TurnPhase.Planning;
                case GrpcTurnTimer.TurnPhase.Player: return TurnPhase.Player;
                case GrpcTurnTimer.TurnPhase.Resolution: return TurnPhase.Resolution;
                case GrpcTurnTimer.TurnPhase.End: return TurnPhase.End;
                default: return TurnPhase.Unknown;
            }
        }
    }
}
