using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcBoard;
using GrpcChat;
using GrpcTurnTimer;

namespace TestConsole
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Pass "--turns" to also print the turn timer, "--board" to also print the board and its changes
            bool showTurns = args.Contains("--turns");
            bool showBoard = args.Contains("--board");

            // The port number must match the port of the gRPC server. Override with --server=https://host:port
            string serverAddress = args.FirstOrDefault(a => a.StartsWith("--server="))?.Substring("--server=".Length) ?? "https://localhost:7116";
            using var channel = GrpcChannel.ForAddress(serverAddress);
            using var shutdown = new CancellationTokenSource();

            if (showBoard)
            {
                var boardClient = new BoardService.BoardServiceClient(channel);
                var boardTask = Task.Run(async () =>
                {
                    try
                    {
                        using var boardCall = boardClient.JoinBoardStream(new Empty(), cancellationToken: shutdown.Token);
                        await foreach (BoardEvent boardEvent in boardCall.ResponseStream.ReadAllAsync(shutdown.Token))
                        {
                            if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.Board)
                            {
                                PrintBoard(boardEvent.Board);
                            }
                            else if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.TilesChanged)
                            {
                                var changed = boardEvent.TilesChanged;
                                Console.WriteLine($"[Board v{changed.Version}] {changed.Tiles.Count} tile(s) changed: {string.Join(", ", changed.Tiles.Select(t => $"({t.X}, {t.Y}) {t.Type}"))}");
                            }
                        }
                    }
                    catch (Exception) when (shutdown.IsCancellationRequested) { }
                    catch (RpcException ex)
                    {
                        Console.WriteLine($"Board disconnected: {ex.Status.Detail}");
                    }
                });
            }

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

        static void PrintBoard(BoardState board)
        {
            Console.WriteLine($"[Board v{board.Version}] {board.Width} x {board.Height} (~ water, . plains, T forest, n hills, ^ mountain)");

            // Top row first, so y = 0 is at the bottom like in the game
            for (int y = board.Height - 1; y >= 0; y--)
            {
                var row = new System.Text.StringBuilder();
                for (int x = 0; x < board.Width; x++)
                {
                    row.Append(board.Tiles[y * board.Width + x].Type switch
                    {
                        TileType.Water => '~',
                        TileType.Plains => '.',
                        TileType.Forest => 'T',
                        TileType.Hills => 'n',
                        TileType.Mountain => '^',
                        _ => '?'
                    });
                }
                Console.WriteLine(row);
            }
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
