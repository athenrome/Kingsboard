using KingsboardServer.Data;
using KingsboardServer.Handlers;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Repositories;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services;
using KingsboardServer.Services.CardActions;
using KingsboardServer.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

                //Database settings (appsettings.json -> "DatabaseSettings"). All database access goes through Entity Framework:
                //repositories create a short-lived KingsboardDbContext per operation from this factory.
                builder.Services.AddOptions<DatabaseSettings>()
                    .Bind(builder.Configuration.GetSection(DatabaseSettings.SectionName))
                    .Validate(s => !string.IsNullOrWhiteSpace(s.ConnectionString), "DatabaseSettings:ConnectionString must be set")
                    .ValidateOnStart();

                builder.Services.AddDbContextFactory<KingsboardDbContext>((services, options) =>
                {
                    DatabaseSettings settings = services.GetRequiredService<IOptions<DatabaseSettings>>().Value;
                    SqliteConnectionStringBuilder connection = new SqliteConnectionStringBuilder(settings.ConnectionString);

                    // Keep the database next to the server rather than wherever it happened to be started from
                    if (!string.IsNullOrEmpty(connection.DataSource) && connection.DataSource != ":memory:" && !Path.IsPathRooted(connection.DataSource))
                    {
                        connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
                    }
                    options.UseSqlite(connection.ToString());
                });

                //Global turn timer settings (appsettings.json -> "TurnSettings")
                builder.Services.AddOptions<TurnSettings>()
                    .Bind(builder.Configuration.GetSection(TurnSettings.SectionName))
                    .Validate(s => s.TurnDurationSeconds > 0, "TurnSettings:TurnDurationSeconds must be greater than 0")
                    .ValidateOnStart();

                builder.Services.AddSingleton<ITurnService, TurnService>();

                //Chat settings (appsettings.json -> "ChatSettings")
                builder.Services.AddOptions<ChatSettings>()
                    .Bind(builder.Configuration.GetSection(ChatSettings.SectionName))
                    .Validate(s => s.MaxMessageLength > 0 && s.MaxUsernameLength > 0, "ChatSettings: MaxMessageLength and MaxUsernameLength must be greater than 0")
                    .ValidateOnStart();

                builder.Services.AddSingleton<IChatRepository, ChatRepository>();
                builder.Services.AddSingleton<IChatRoomService, ChatRoomService>();

                //Board settings (appsettings.json -> "BoardSettings")
                builder.Services.AddOptions<BoardSettings>()
                    .Bind(builder.Configuration.GetSection(BoardSettings.SectionName))
                    .Validate(s => s.Width > 0 && s.Depth > 0 && s.Height > 0, "BoardSettings: Width, Depth and Height must be greater than 0")
                    .Validate(s => s.MaxSectionSize > 0 && s.DefaultSectionSize > 0 && s.DefaultSectionSize <= s.MaxSectionSize, "BoardSettings: DefaultSectionSize must be between 1 and MaxSectionSize")
                    .ValidateOnStart();

                builder.Services.AddSingleton<IBoardRepository, BoardRepository>();
                builder.Services.AddSingleton<IBoardService, BoardService>();
                builder.Services.AddSingleton<ITileService, TileService>();
                builder.Services.AddSingleton<IBoardViewService, BoardViewService>();

                //Player settings (appsettings.json -> "PlayerSettings")
                builder.Services.AddOptions<PlayerSettings>()
                    .Bind(builder.Configuration.GetSection(PlayerSettings.SectionName))
                    .Validate(s => s.PawnsPerPlayer >= 0 && s.MaxNameLength > 0, "PlayerSettings: PawnsPerPlayer can't be negative, and MaxNameLength must be greater than 0")
                    .ValidateOnStart();

                builder.Services.AddSingleton<IPlayerService, PlayerService>();

                //Cards: every kind of card names an action, and each action is carried out by one ICardAction
                builder.Services.AddSingleton<ICardAction, MoveCardAction>();
                builder.Services.AddSingleton<ICardAction, SayRandomNumberCardAction>();
                builder.Services.AddSingleton<ICardAction, SayRandomColourCardAction>();
                builder.Services.AddSingleton<ICardAction, SayHelloCardAction>();
                builder.Services.AddSingleton<ICardService, CardService>();

                //Pawn settings (appsettings.json -> "PawnSettings")
                builder.Services.AddOptions<PawnSettings>()
                    .Bind(builder.Configuration.GetSection(PawnSettings.SectionName))
                    .Validate(s => s.Count >= 0 && s.SpawnAreaSize > 0 && s.MaxClimb >= 0 && s.HandSize >= 0, "PawnSettings: Count, MaxClimb and HandSize can't be negative, and SpawnAreaSize must be greater than 0")
                    .ValidateOnStart();

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
