using KingsboardServer.Data;
using KingsboardServer.Handlers;
using KingsboardServer.Repositories;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services;
using KingsboardServer.Services.CardActions;
using KingsboardServer.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace KingsboardServer
{
    /// <summary>
    /// The server's entry point: configures services and gRPC endpoints, then runs the server and game loop
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Starts the Kingsboard server
        /// </summary>
        public static void Main(string[] args)
        {


            #region StartupLogger

            //Setup logger for catching startup errors before the full DI container is configured
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            #endregion
            try
            {
                Log.Information("Starting Up...");

                var builder = WebApplication.CreateBuilder(args);


                builder.Services.AddSerilog((services, lc) => lc
                .ReadFrom.Configuration(builder.Configuration)
                //.ReadFrom.Services(services) // Allows Serilog to use other DI services
                .Enrich.FromLogContext()
                .WriteTo.Console());

                builder.Services.AddGrpc();

                //Declare the Game service as a concrete object so that it can be started after setup


                builder.Services.AddSingleton(TimeProvider.System);

                //Every system reads its settings straight from IConfiguration (appsettings.json), each from its own section

                //Database (appsettings.json -> "ConnectionStrings:Kingsboard"). All database access goes through Entity Framework:
                //repositories create a short-lived KingsboardDbContext per operation from this factory.
                builder.Services.AddDbContextFactory<KingsboardDbContext>((services, options) =>
                {
                    string connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Kingsboard")
                        ?? throw new InvalidOperationException("ConnectionStrings:Kingsboard must be set");
                    SqliteConnectionStringBuilder connection = new SqliteConnectionStringBuilder(connectionString);

                    // Keep the database next to the server rather than wherever it happened to be started from
                    if (!string.IsNullOrEmpty(connection.DataSource) && connection.DataSource != ":memory:" && !Path.IsPathRooted(connection.DataSource))
                    {
                        connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
                    }
                    options.UseSqlite(connection.ToString());
                });

                //Turn timer (appsettings.json -> "TurnSettings")
                builder.Services.AddSingleton<ITurnService, TurnService>();

                //Chat (appsettings.json -> "ChatSettings")
                builder.Services.AddSingleton<IChatRepository, ChatRepository>();
                builder.Services.AddSingleton<IChatRoomService, ChatRoomService>();

                //Board (appsettings.json -> "BoardSettings")
                builder.Services.AddSingleton<IBoardRepository, BoardRepository>();
                builder.Services.AddSingleton<IBoardService, BoardService>();
                builder.Services.AddSingleton<ITileService, TileService>();
                builder.Services.AddSingleton<IBoardViewService, BoardViewService>();

                //Players (appsettings.json -> "PlayerSettings")
                builder.Services.AddSingleton<IPlayerService, PlayerService>();

                //Cards: every kind of card names an action, and each action is carried out by one ICardAction
                builder.Services.AddSingleton<ICardAction, MoveCardAction>();
                builder.Services.AddSingleton<ICardAction, SayRandomNumberCardAction>();
                builder.Services.AddSingleton<ICardAction, SayRandomColourCardAction>();
                builder.Services.AddSingleton<ICardAction, SayHelloCardAction>();
                builder.Services.AddSingleton<ICardRepository, CardRepository>();
                builder.Services.AddSingleton<ICardService, CardService>();

                //Pawns (appsettings.json -> "PawnSettings")
                builder.Services.AddSingleton<IPawnMovementService, PawnMovementService>();
                builder.Services.AddSingleton<IPawnRepository, PawnRepository>();
                // One PawnService instance is both the pawn API and the handler for the turn phases pawns act in
                builder.Services.AddSingleton<PawnService>();
                builder.Services.AddSingleton<IPawnService>(services => services.GetRequiredService<PawnService>());
                builder.Services.AddSingleton<ITurnPhaseHandler>(services => services.GetRequiredService<PawnService>());

                builder.Services.AddSingleton<IGameService, GameService>();



                // Configure the HTTP request pipeline.

                //Build & Run
                var app = builder.Build();

                //Create the database, or bring its tables up to date, before anything reads or writes it
                using (KingsboardDbContext db = app.Services.GetRequiredService<IDbContextFactory<KingsboardDbContext>>().CreateDbContext())
                {
                    db.Database.Migrate();
                }

                //Configure GRPC Services
                app.MapGrpcService<ChatGrpcHandler>();
                app.MapGrpcService<TurnTimerGrpcHandler>();
                app.MapGrpcService<BoardGrpcHandler>();
                app.MapGrpcService<PawnGrpcHandler>();
                app.MapGrpcService<PlayerGrpcHandler>();
                app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

                IGameService game = app.Services.GetRequiredService<IGameService>();

                //Start the game loop once the server is listening, and stop it on shutdown
                app.Lifetime.ApplicationStarted.Register(game.StartGame);
                app.Lifetime.ApplicationStopping.Register(game.StopGame);

                app.Run();

            }
            catch (HostAbortedException)
            {
                //The Entity Framework tools (e.g. "dotnet ef migrations add") stop the host on purpose once they've read its setup
            }
            catch (Exception e)
            {
                Log.Fatal($"Application start-up failed due to: {e.Message}", e);
            }


            // Stage 2: Full DI configuration




            //builder.Host.UseSerilog();


            // Add services to the container.





        }
    }
}
