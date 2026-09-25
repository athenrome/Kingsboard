using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcChat;
using GrpcTurnTimer;

namespace TestConsole
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Pass "--turns" to also print the turn timer
            bool showTurns = args.Contains("--turns");

            // The port number must match the port of the gRPC server.
            using var channel = GrpcChannel.ForAddress("https://localhost:7116");
            using var shutdown = new CancellationTokenSource();

            var chatClient = new ChatService.ChatServiceClient(channel);

            Console.Write("Username: ");
            string? requestedName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                requestedName = "User" + new Random().Next(1000);
            }

            using var chatCall = chatClient.JoinChat(new JoinChatRequest() { Username = requestedName }, cancellationToken: shutdown.Token);

            // The first event is always "joined", which gives us our session id and final username
            if (!await chatCall.ResponseStream.MoveNext(shutdown.Token) || chatCall.ResponseStream.Current.PayloadCase != ChatEvent.PayloadOneofCase.Joined)
            {
                Console.WriteLine("Failed to join the chat.");
                return;
            }

            ChatJoined joined = chatCall.ResponseStream.Current.Joined;
            Console.WriteLine($"Joined chat as {joined.Username}. Type a message and press Enter (\"/exit\" to quit).");

            // Print incoming chat messages in the background
            var readTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (ChatEvent chatEvent in chatCall.ResponseStream.ReadAllAsync(shutdown.Token))
                    {
                        if (chatEvent.PayloadCase == ChatEvent.PayloadOneofCase.Message)
                        {
                            PrintMessage(chatEvent.Message);
                        }
                    }
                    Console.WriteLine("Chat stream closed by the server.");
                }
                catch (Exception) when (shutdown.IsCancellationRequested) { }
                catch (RpcException ex)
                {
                    Console.WriteLine($"Chat disconnected: {ex.Status.Detail}");
                }
            });

            if (showTurns)
            {
                var turnClient = new TurnTimerService.TurnTimerServiceClient(channel);
                var turnTask = Task.Run(async () =>
                {
                    try
                    {
                        using var turnCall = turnClient.JoinTurnStream(new Empty(), cancellationToken: shutdown.Token);
                        await foreach (var turnEvent in turnCall.ResponseStream.ReadAllAsync(shutdown.Token))
                        {
                            Console.WriteLine($"[Turn {turnEvent.Turn.TurnId}] {turnEvent.Type} ({turnEvent.Turn.TimeRemainingMs}ms remaining)");
                        }
                    }
                    catch (Exception) when (shutdown.IsCancellationRequested) { }
                });
            }

            // Send messages from the main thread
            while (true)
            {
                string? text = Console.ReadLine();
                if (text == null || text.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                try
                {
                    await chatClient.SendChatMessageAsync(new SendChatMessageRequest() { SessionId = joined.SessionId, Text = text });
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"(not sent: {ex.Status.Detail})");
                }
            }

            // Closing the stream leaves the chat
            shutdown.Cancel();
            try { await readTask; } catch { }
        }

        static void PrintMessage(ChatMessage message)
        {
            string time = message.SentAt.ToDateTime().ToLocalTime().ToString("HH:mm");
            string history = message.IsHistory ? " (earlier)" : "";

            if (message.Type == ChatMessageType.System)
            {
                Console.WriteLine($"[{time}]{history} * {message.Text}");
            }
            else
            {
                Console.WriteLine($"[{time}]{history} {message.Username}: {message.Text}");
            }
        }
    }
}
