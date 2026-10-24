using Grpc.Core;
using GrpcPlayer;
using KingsboardServer.Models.Players;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles player requests from clients (gRPC). All player logic lives in <see cref="IPlayerService"/>.
    /// </summary>
    public class PlayerGrpcHandler : PlayerService.PlayerServiceBase
    {
        private readonly IPlayerService _playerService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the player service and server lifetime it uses
        /// </summary>
        public PlayerGrpcHandler(IPlayerService playerService, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _playerService = playerService;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Joins the client to the game, sends it its ids and name, then holds the stream open until the client
        /// disconnects or the server stops, and removes the player when it closes
        /// </summary>
        public override async Task JoinGame(JoinGameRequest request, IServerStreamWriter<PlayerEvent> responseStream, ServerCallContext context)
        {
            Player player = _playerService.Join(request.Name);

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            try
            {
                await responseStream.WriteAsync(new PlayerEvent()
                {
                    Joined = new PlayerJoinedEvent()
                    {
                        PlayerId = player.Id.ToString(),
                        SessionId = player.SessionId.ToString(),
                        Name = player.Name,
                        Colour = player.Colour
                    }
                });

                // Nothing more to send yet; the open stream is what keeps the player joined
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
                _logger.Debug("Player stream for {Name} closed: {Reason}", player.Name, ex.Message);
            }
            finally
            {
                _playerService.Leave(player.SessionId);
            }
        }
    }
}
