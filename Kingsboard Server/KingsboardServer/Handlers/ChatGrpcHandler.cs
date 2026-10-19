using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcChat;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Subscriptions.Chat;
using KingsboardServer.Models.Chat;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Handlers
{
    /// <summary>
    /// Handles chat requests from clients (gRPC). All chat logic lives in <see cref="IChatRoomService"/>.
    /// </summary>
    public class ChatGrpcHandler : ChatService.ChatServiceBase
    {
        private readonly IChatRoomService _chatRoom;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Created by gRPC for each call, with the chat room and server lifetime it uses
        /// </summary>
        public ChatGrpcHandler(IChatRoomService chatRoom, IHostApplicationLifetime lifetime, Serilog.ILogger logger)
        {
            _chatRoom = chatRoom;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// Joins the client to the chat and streams its events (joined, history, then live messages) until it disconnects, then leaves the chat
        /// </summary>
        public override async Task JoinChat(JoinChatRequest request, IServerStreamWriter<ChatEvent> responseStream, ServerCallContext context)
        {
            ChatSubscription subscription = _chatRoom.Join(request.Username);

            // End the stream when the client disconnects or the server shuts down
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);
            CancellationToken token = cts.Token;

            try
            {
                await responseStream.WriteAsync(new ChatEvent()
                {
                    Joined = new ChatJoinedEvent()
                    {
                        SessionId = subscription.SessionId.ToString(),
                        Username = subscription.Username
                    }
                });

                // Only this call writes to the stream (gRPC doesn't allow concurrent writes)
                await foreach (ChatEntry entry in subscription.Messages.ReadAllAsync(token))
                {
                    await responseStream.WriteAsync(new ChatEvent() { Message = ToProto(entry) });
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested)
            {
                // Client disconnected or server is stopping
                _logger.Debug("Chat stream for {Username} closed: {Reason}", subscription.Username, ex.Message);
            }
            finally
            {
                _chatRoom.Leave(subscription.SessionId);
            }
        }

        /// <summary>
        /// Sends a player's message to the chat, turning any rejection into the matching gRPC status code
        /// </summary>
        public override Task<Empty> SendChatMessage(SendChatMessageRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.SessionId, out Guid sessionId))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Missing or invalid session id. Join the chat first."));
            }

            ChatSendResult result = _chatRoom.SendMessage(sessionId, request.Text);

            StatusCode? errorCode = result.Status switch
            {
                ChatSendStatus.Sent => null,
                ChatSendStatus.NotJoined => StatusCode.FailedPrecondition,
                ChatSendStatus.EmptyMessage => StatusCode.InvalidArgument,
                ChatSendStatus.MessageTooLong => StatusCode.InvalidArgument,
                ChatSendStatus.RateLimited => StatusCode.ResourceExhausted,
                _ => StatusCode.Internal
            };

            if (errorCode is StatusCode code)
            {
                throw new RpcException(new Status(code, result.Error ?? "Message could not be sent"));
            }

            return Task.FromResult(new Empty());
        }

        private static ChatMessage ToProto(ChatEntry entry)
        {
            return new ChatMessage()
            {
                Id = entry.Id,
                Type = entry.Type == ChatEntryType.System ? ChatMessageType.System : ChatMessageType.Player,
                Username = entry.Username,
                Text = entry.Text,
                SentAt = Timestamp.FromDateTimeOffset(entry.SentAt),
                IsHistory = entry.IsHistory
            };
        }
    }
}
