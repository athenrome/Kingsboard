using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcBoard;
using GrpcChat;
using GrpcPawn;
using GrpcTurnTimer;

namespace TestConsole
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Pass "--turns" to also print the turn timer, "--board" to also print your section of the board and its changes,
            // "--pawns" to also print the pawns' plans and moves. With --board, type "/view x y [width depth]" to look at a different section.
            bool showTurns = args.Contains("--turns");
            bool showBoard = args.Contains("--board");
            bool showPawns = args.Contains("--pawns");

            // The port number must match the port of the gRPC server. Override with --server=https://host:port
            string serverAddress = args.FirstOrDefault(a => a.StartsWith("--server="))?.Substring("--server=".Length) ?? "https://localhost:7116";
            using var channel = GrpcChannel.ForAddress(serverAddress);
            using var shutdown = new CancellationTokenSource();

            var boardClient = new BoardService.BoardServiceClient(channel);
            string? boardSessionId = null;
            BoardRegion? boardRegion = null;

            if (showBoard)
            {
                var boardTask = Task.Run(async () =>
                {
                    try
                    {
                        using var boardCall = boardClient.JoinBoardStream(new JoinBoardStreamRequest(), cancellationToken: shutdown.Token);
                        await foreach (BoardEvent boardEvent in boardCall.ResponseStream.ReadAllAsync(shutdown.Token))
                        {
                            if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.Joined)
                            {
                                boardSessionId = boardEvent.Joined.SessionId;
                                Console.WriteLine($"[Board] Joined (sections up to {boardEvent.Joined.MaxSectionSize} x {boardEvent.Joined.MaxSectionSize}). Type \"/view x y [width depth]\" to move.");
                            }
                            else if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.Section)
                            {
                                boardRegion = boardEvent.Section.Region;
                                PrintSection(boardEvent.Section);
                            }
                            else if (boardEvent.PayloadCase == BoardEvent.PayloadOneofCase.TilesChanged)
                            {
                                var changed = boardEvent.TilesChanged;
                                string placed = string.Join(", ", changed.Tiles.Select(t => $"({t.Position.X}, {t.Position.Y}, {t.Position.Z}) {t.Type}"));
                                string removed = string.Join(", ", changed.Removed.Select(p => $"({p.X}, {p.Y}, {p.Z})"));
                                Console.WriteLine($"[Board v{changed.Version}] placed: [{placed}] removed: [{removed}]");
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

            if (showPawns)
            {
                var pawnClient = new PawnService.PawnServiceClient(channel);
                var pawnTask = Task.Run(async () =>
                {
                    try
                    {
                        using var pawnCall = pawnClient.JoinPawnStream(new Empty(), cancellationToken: shutdown.Token);
                        await foreach (PawnEvent pawnEvent in pawnCall.ResponseStream.ReadAllAsync(shutdown.Token))
                        {
                            PrintPawnEvent(pawnEvent);
                        }
                    }
                    catch (Exception) when (shutdown.IsCancellationRequested) { }
                    catch (RpcException ex)
                    {
                        Console.WriteLine($"Pawns disconnected: {ex.Status.Detail}");
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
                            Console.WriteLine($"[Turn {turnEvent.Turn.TurnId}] {turnEvent.Type} {turnEvent.Turn.Phase} ({turnEvent.Turn.TimeRemainingMs}ms remaining)");
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

                if (text.StartsWith("/view", StringComparison.OrdinalIgnoreCase))
                {
                    await ViewSection(boardClient, boardSessionId, boardRegion, text);
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

        /// <summary>
        /// Handles "/view x y [width depth]": asks the server for a different section (sent on the board stream)
        /// </summary>
        static async Task ViewSection(BoardService.BoardServiceClient boardClient, string? sessionId, BoardRegion? current, string command)
        {
            if (sessionId == null)
            {
                Console.WriteLine("(not viewing the board: start with --board)");
                return;
            }

            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
            {
                Console.WriteLine("(usage: /view x y [width depth])");
                return;
            }

            int width = parts.Length > 3 && int.TryParse(parts[3], out int w) ? w : current?.Width ?? 24;
            int depth = parts.Length > 4 && int.TryParse(parts[4], out int d) ? d : current?.Depth ?? 24;

            try
            {
                await boardClient.SetBoardSectionAsync(new SetBoardSectionRequest()
                {
                    SessionId = sessionId,
                    Region = new BoardRegion() { Origin = new BoardPosition() { X = x, Y = y }, Width = width, Depth = depth }
                });
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"(section not changed: {ex.Status.Detail})");
            }
        }

        static void PrintSection(BoardSection section)
        {
            BoardRegion region = section.Region;
            Console.WriteLine($"[Board v{section.Version}] Section {region.Width} x {region.Depth} at ({region.Origin.X}, {region.Origin.Y}) of a {section.BoardWidth} x {section.BoardDepth} x {section.BoardHeight} board, {section.Tiles.Count} tiles");
            Console.WriteLine("Top tile of each stack (~ water, . plains, T forest, n hills, ^ mountain, # stone), then stack heights:");

            // The top tile of each column is the one with the highest z
            var tops = section.Tiles
                .GroupBy(t => (t.Position.X, t.Position.Y))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Position.Z).First());

            // Back row first, so the front of the section is at the bottom like looking down on the board
            for (int y = region.Origin.Y + region.Depth - 1; y >= region.Origin.Y; y--)
            {
                var types = new System.Text.StringBuilder();
                var heights = new System.Text.StringBuilder();
                for (int x = region.Origin.X; x < region.Origin.X + region.Width; x++)
                {
                    if (!tops.TryGetValue((x, y), out Tile? top))
                    {
                        types.Append(' ');
                        heights.Append('0');
                        continue;
                    }

                    types.Append(top.Type switch
                    {
                        TileType.Water => '~',
                        TileType.Plains => '.',
                        TileType.Forest => 'T',
                        TileType.Hills => 'n',
                        TileType.Mountain => '^',
                        TileType.Stone => '#',
                        _ => '?'
                    });
                    heights.Append(top.Position.Z + 1);
                }
                Console.WriteLine($"{types}   {heights}");
            }
        }

        static string Describe(BoardPosition position) => $"({position.X}, {position.Y}, {position.Z})";

        static void PrintPawnEvent(PawnEvent pawnEvent)
        {
            switch (pawnEvent.PayloadCase)
            {
                case PawnEvent.PayloadOneofCase.Snapshot:
                    Console.WriteLine($"[Pawns] {pawnEvent.Snapshot.Pawns.Count} pawns:");
                    foreach (Pawn pawn in pawnEvent.Snapshot.Pawns)
                    {
                        Console.WriteLine($"  {pawn.Name} {pawn.Colour} at {Describe(pawn.Position)}");
                    }
                    break;

                case PawnEvent.PayloadOneofCase.Spawned:
                    Console.WriteLine($"[Pawns] Spawned {pawnEvent.Spawned.Pawns.Count}: {string.Join(", ", pawnEvent.Spawned.Pawns.Select(p => $"{p.Name} at {Describe(p.Position)}"))}");
                    break;

                case PawnEvent.PayloadOneofCase.Planned:
                    Console.WriteLine($"[Turn {pawnEvent.Planned.TurnId}] Pawns planned:");
                    foreach (Pawn pawn in pawnEvent.Planned.Pawns)
                    {
                        string plan = pawn.PlannedPath.Count == 0 ? "stay put" : $"walk {pawn.PlannedPath.Count} to {Describe(pawn.PlannedPath[^1])} via {string.Join(" ", pawn.PlannedPath.Select(Describe))}";
                        Console.WriteLine($"  {pawn.Name} at {Describe(pawn.Position)}: {plan}");
                    }
                    break;

                case PawnEvent.PayloadOneofCase.Moved:
                    var names = pawnEvent.Moved.Pawns.ToDictionary(p => p.Id, p => p.Name);
                    Console.WriteLine($"[Turn {pawnEvent.Moved.TurnId}] Pawns moved:");
                    foreach (var movement in pawnEvent.Moved.Movements)
                    {
                        string blocked = movement.Blocked ? " (blocked)" : "";
                        Console.WriteLine($"  {names.GetValueOrDefault(movement.PawnId, movement.PawnId)}: {movement.Path.Count} steps, now at {Describe(movement.Position)}{blocked}");
                    }
                    break;
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
