using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using GrpcChat;
using Kingsboard.Networking;
using UnityEngine;

namespace Kingsboard.Chat
{
    /// <summary>
    /// All communication with the server's chat goes through here. Nothing outside this class
    /// knows about gRPC: the rest of the game only sees <see cref="ChatLine"/> and the events below.
    /// Create and use it from the main thread; its events are raised on the main thread.
    /// </summary>
    public class ChatHandler : IDisposable
    {
        private const double ReconnectDelaySeconds = 2;
        private const double SendTimeoutSeconds = 10;

        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private GrpcChat.ChatService.ChatServiceClient _client;
        private string _sessionId;

        /// <summary>Raised when we've joined the chat, with the username the server gave us</summary>
        public event Action<string> Joined;

        /// <summary>Raised for every message from the server (recent history first, then live messages)</summary>
        public event Action<ChatLine> MessageReceived;

        /// <summary>Raised when the connection to the chat is lost. It reconnects by itself.</summary>
        public event Action Disconnected;

        /// <summary>
        /// Joins the chat and keeps listening for messages, rejoining whenever the connection drops
        /// </summary>
        public async void Connect(string username)
        {
            CancellationToken token = _shutdown.Token;
            _client = new GrpcChat.ChatService.ChatServiceClient(GrpcConnection.Channel);
            bool logFailure = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<ChatEvent> call = _client.JoinChat(new JoinChatRequest() { Username = username }, cancellationToken: token);

                    while (await call.ResponseStream.MoveNext(token))
                    {
                        ChatEvent chatEvent = call.ResponseStream.Current;

                        if (chatEvent.PayloadCase == ChatEvent.PayloadOneofCase.Joined)
                        {
                            _sessionId = chatEvent.Joined.SessionId;
                            logFailure = true;
                            Joined?.Invoke(chatEvent.Joined.Username);
                        }
                        else if (chatEvent.PayloadCase == ChatEvent.PayloadOneofCase.Message)
                        {
                            MessageReceived?.Invoke(ToChatLine(chatEvent.Message));
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
                        Debug.LogWarning($"[Chat] Not connected to the chat server, retrying: {ex.Message}");
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
        /// Sends a message to everyone in the chat.
        /// Returns null when it was sent, otherwise the reason it wasn't.
        /// </summary>
        public async Task<string> SendAsync(string text)
        {
            if (_sessionId == null)
            {
                return "Not connected to the chat";
            }

            try
            {
                SendChatMessageRequest request = new SendChatMessageRequest() { SessionId = _sessionId, Text = text };
                await _client.SendChatMessageAsync(request, deadline: DateTime.UtcNow.AddSeconds(SendTimeoutSeconds), cancellationToken: _shutdown.Token);
                return null;
            }
            catch (RpcException ex)
            {
                return string.IsNullOrEmpty(ex.Status.Detail) ? "Message could not be sent" : ex.Status.Detail;
            }
        }

        /// <summary>Leaves the chat (closing the stream is what tells the server we've left)</summary>
        public void Dispose()
        {
            _shutdown.Cancel();
            _sessionId = null;
        }

        private static ChatLine ToChatLine(ChatMessage message)
        {
            return new ChatLine()
            {
                IsSystem = message.Type == ChatMessageType.System,
                IsHistory = message.IsHistory,
                Username = message.Username,
                Text = message.Text,
                SentAt = message.SentAt != null ? message.SentAt.ToDateTime().ToLocalTime() : DateTime.Now
            };
        }
    }
}
