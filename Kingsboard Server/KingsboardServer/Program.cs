using KingsboardServer.Handlers;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Repositories;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services;
using KingsboardServer.Services.Interfaces;
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
                    .ValidateOnStart();

                builder.Services.AddSingleton<IBoardRepository, BoardRepository>();
                builder.Services.AddSingleton<IBoardService, BoardService>();
                builder.Services.AddSingleton<ITileService, TileService>();

                builder.Services.AddSingleton<IGameService, GameService>();



                // Configure the HTTP request pipeline.

                //Build & Run
                var app = builder.Build();

                //Configure GRPC Services
                app.MapGrpcService<ChatGrpcHandler>();
                app.MapGrpcService<TurnTimerGrpcHandler>();
                app.MapGrpcService<BoardGrpcHandler>();
                app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

                IGameService game = app.Services.GetRequiredService<IGameService>();

                //Start the game loop once the server is listening, and stop it on shutdown
                app.Lifetime.ApplicationStarted.Register(game.StartGame);
                app.Lifetime.ApplicationStopping.Register(game.StopGame);

                app.Run();

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
