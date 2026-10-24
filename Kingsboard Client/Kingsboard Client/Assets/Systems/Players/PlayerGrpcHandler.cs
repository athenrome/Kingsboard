using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using GrpcPlayer;
using Kingsboard.Networking;
using UnityEngine;

namespace Kingsboard.Players
{
    /// <summary>
    /// Manages the gRPC communication with the server's players. Nothing outside this class knows about gRPC: the rest of
    /// the game only sees <see cref="PlayerInfo"/> and the events below. We stay joined while its stream is open.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class PlayerGrpcHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private bool _connected;

        /// <summary>
        /// Raised when we have joined the game, with our ids and the name we were given. Raised again after every
        /// reconnect, with new ids (the server treats a reconnect as a new player).
        /// </summary>
        public event Action<PlayerInfo> Joined;

        /// <summary>
        /// Raised when the connection is lost, so we're no longer joined. It reconnects by itself.
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Joins the game as <paramref name="name"/> and keeps the stream open, rejoining whenever the connection drops
        /// </summary>
        public async void Connect(string name)
        {
            CancellationToken token = _shutdown.Token;
            GrpcPlayer.PlayerService.PlayerServiceClient client = new GrpcPlayer.PlayerService.PlayerServiceClient(GrpcConnection.Channel);
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<PlayerEvent> call = client.JoinGame(new JoinGameRequest() { Name = name ?? string.Empty }, cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        PlayerEvent playerEvent = call.ResponseStream.Current;
                        if (playerEvent.PayloadCase == PlayerEvent.PayloadOneofCase.Joined)
                        {
                            _connected = true;
                            logFailure = true;
                            Joined?.Invoke(new PlayerInfo()
                            {
                                PlayerId = ToGuid(playerEvent.Joined.PlayerId),
                                SessionId = ToGuid(playerEvent.Joined.SessionId),
                                Name = playerEvent.Joined.Name,
                                Colour = playerEvent.Joined.Colour
                            });
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
                        Debug.LogWarning($"[Players] Not connected to the game, retrying: {ex.Message}");
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
        /// Leaves the game (closing the stream is what tells the server we've gone)
        /// </summary>
        public void Dispose()
        {
            _shutdown.Cancel();
        }

        // The server always sends ids as Guids; anything else becomes Guid.Empty, which matches nothing
        private static Guid ToGuid(string id)
        {
            return Guid.TryParse(id, out Guid guid) ? guid : Guid.Empty;
        }
    }
}
