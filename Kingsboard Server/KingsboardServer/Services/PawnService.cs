using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Cards;
using Kingsboard.Shared.Models.Enumerations;
using Kingsboard.Shared.Models.Pawns;
using KingsboardServer.Data.Records;
using KingsboardServer.Events.Players;
using KingsboardServer.Events.Pawns;
using KingsboardServer.Models;
using KingsboardServer.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Models.Players;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Owns the pawns and decides everything they do. Pawns act by playing action cards: each turn every pawn draws a
    /// hand (Upkeep), picks a card and target from it (Planning), may have that choice changed by the player who controls
    /// it (Player), plays it (Resolution, highest Dexterity first) and shuffles its hand back into its deck (End).
    /// It also hands pawns out to players as they join and takes them back when they leave.
    /// What each card does is up to its <see cref="ICardAction"/>; where pawns can walk is up to <see cref="IPawnMovementService"/>.
    /// </summary>
    public class PawnService : IPawnService, ITurnPhaseHandler
    {
        private static readonly string[] Names =
        {
            "Alda", "Bram", "Cora", "Dain", "Elsa", "Finn", "Greta", "Hale",
            "Ilse", "Jory", "Kara", "Leif", "Mira", "Nils", "Oona", "Pell"
        };

        private static readonly string[] Colours =
        {
            "#E63946", "#F4A261", "#E9C46A", "#2A9D8F", "#457B9D", "#9B5DE5", "#F15BB5", "#00BBF9",
            "#FB8500", "#8AC926", "#FFFFFF", "#6D597A"
        };

        // Used when PawnSettings:StartingDeck is empty: three Move cards and one of each test card
        private static readonly string[] DefaultStartingDeck =
        {
            CardService.MoveCardId, CardService.MoveCardId, CardService.MoveCardId,
            CardService.SayNumberCardId, CardService.SayColourCardId, CardService.SayHelloCardId
        };

        private static readonly TurnPhase[] HandledPhases = { TurnPhase.Upkeep, TurnPhase.Planning, TurnPhase.Player, TurnPhase.Resolution, TurnPhase.End };

        // Every attribute is rolled as a whole number in this range, inclusive
        private const int MinAttribute = 1;
        private const int MaxAttribute = 10;

        private readonly Serilog.ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IPawnRepository _repository;
        private readonly IBoardService _boardService;
        private readonly IPawnMovementService _movement;
        private readonly ICardService _cardService;
        private readonly IPlayerService _playerService;

        // Guards every read and change of the pawns, and keeps changes and their events in order.
        // Lock order is player, then pawn, then board: player changes arrive under the player lock, board queries are made
        // while holding this one, and neither the board nor the players ever call back in.
        private readonly object _lock = new();
        private Random _random = new Random();

        // The working copy of the stored pawns, read from the database the first time it's needed and replaced by every
        // save since, so reads don't go back to the database. Only touched under _lock, and never changed in place.
        private IReadOnlyList<Pawn> _pawns = Array.Empty<Pawn>();
        private bool _pawnsLoaded;

        // The turn in progress, and whether players may change their pawns' cards (only during its Player phase)
        private int _turnId;
        private bool _selectionOpen;

        /// <summary>
        /// Raised when pawns change. Raised under the pawn lock, in order. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<PawnsChangeEvent>? PawnsChanged;

        /// <summary>
        /// Creates the service and starts following players joining and leaving. There are no pawns until
        /// <see cref="SpawnPawns"/> is called.
        /// </summary>
        public PawnService(Serilog.ILogger logger, IConfiguration configuration,
            IPawnRepository repository, IBoardService boardService, IPawnMovementService movement, ICardService cardService, IPlayerService playerService)
        {
            _logger = logger;
            _configuration = configuration;
            _repository = repository;
            _boardService = boardService;
            _movement = movement;
            _cardService = cardService;
            _playerService = playerService;

            _playerService.PlayersChanged += OnPlayersChanged;
        }

        /// <summary>
        /// The phases pawns do work in: Upkeep (draw), Planning (choose cards), Player (players may change choices),
        /// Resolution (play cards) and End (return hands)
        /// </summary>
        public IReadOnlyCollection<TurnPhase> Phases => HandledPhases;

        /// <summary>
        /// Returns snapshots of every pawn (empty before any are spawned)
        /// </summary>
        public IReadOnlyList<Pawn> GetPawns()
        {
            lock (_lock)
            {
                return CloneAll(GetLoadedPawns());
            }
        }

        /// <summary>
        /// Returns a snapshot of one pawn with all its details, or null if no pawn has that id
        /// </summary>
        public Pawn? GetPawnById(Guid id)
        {
            lock (_lock)
            {
                Pawn? pawn = GetLoadedPawns().FirstOrDefault(p => p.PublicId == id);
                return pawn is null ? null : Clone(pawn);
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every pawn while no pawn can change, so a caller that starts
        /// listening inside it gets every change after the snapshot and none before it
        /// </summary>
        public void ReadPawns(Action<IReadOnlyList<Pawn>> read)
        {
            lock (_lock)
            {
                read(CloneAll(GetLoadedPawns()));
            }
        }

        /// <summary>
        /// Places PawnSettings:Count pawns on random passable columns near the board's front-left corner, replacing any
        /// earlier pawns, gives them to the joined players, and raises <see cref="PawnsChanged"/>.
        /// Throws if no board has been created yet.
        /// </summary>
        public IReadOnlyList<Pawn> SpawnPawns()
        {
            int wanted = _configuration.GetValue("PawnSettings:Count", 8);
            int spawnAreaSize = _configuration.GetValue("PawnSettings:SpawnAreaSize", 24);
            int? spawnSeed = _configuration.GetValue<int?>("PawnSettings:Seed");
            string[]? configuredDeck = _configuration.GetSection("PawnSettings:StartingDeck").Get<string[]>();
            GameBoard board = _boardService.GetBoard() ?? throw new InvalidOperationException("No board has been created yet");

            IReadOnlyList<Pawn> spawned = Array.Empty<Pawn>();

            // Hold the players steady (player lock, then pawn lock) so no one joins between spawning and handing pawns out
            _playerService.ReadPlayers(players =>
            {
                lock (_lock)
                {
                    _random = spawnSeed is int seed ? new Random(seed) : new Random();

                    int areaWidth = Math.Clamp(spawnAreaSize, 1, board.Width);
                    int areaDepth = Math.Clamp(spawnAreaSize, 1, board.Depth);

                    List<BoardPosition> candidates = new List<BoardPosition>();
                    for (int y = 0; y < areaDepth; y++)
                    {
                        for (int x = 0; x < areaWidth; x++)
                        {
                            if (_movement.GetStandPosition(new BoardPosition(x, y, 0)) is BoardPosition stand)
                            {
                                candidates.Add(stand);
                            }
                        }
                    }

                    Shuffle(candidates);
                    int count = Math.Min(Math.Max(0, wanted), candidates.Count);
                    IReadOnlyList<string> startingDeck = configuredDeck is { Length: > 0 } deck ? deck : DefaultStartingDeck;

                    List<Pawn> pawns = new List<Pawn>(count);
                    for (int i = 0; i < count; i++)
                    {
                        pawns.Add(CreatePawn(i, candidates[i], startingDeck));
                    }

                    if (count < wanted)
                    {
                        _logger.Warning("Only room for {Count} of {Wanted} pawns in the spawn area", count, wanted);
                    }

                    AssignOwners(pawns, players);
                    SavePawns(pawns);
                    RaiseChanged(PawnChangeReason.Spawned, 0, pawns);

                    _logger.Information("Spawned {Count} pawns", count);
                    spawned = CloneAll(pawns);
                }
            });

            return spawned;
        }

        /// <summary>
        /// The most health the pawn can have, which is its Physique
        /// </summary>
        public int GetMaxHealth(Pawn pawn)
        {
            return pawn.Attributes.Physique;
        }

        /// <summary>
        /// Chooses a card for a pawn on behalf of a player, if it's the Player phase, the player controls the pawn, the
        /// card is in its hand and any target it needs is on the board. Works out the pawn's new planned path and raises
        /// <see cref="PawnsChanged"/> with just that pawn.
        /// </summary>
        public CardSelectResult SelectCard(Guid playerId, Guid pawnId, Guid cardId, BoardPosition? target)
        {
            lock (_lock)
            {
                if (!_selectionOpen)
                {
                    return new CardSelectResult(false, "Cards can only be chosen in the Player phase");
                }

                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                Pawn? pawn = pawns.FirstOrDefault(p => p.PublicId == pawnId);
                if (pawn is null)
                {
                    return new CardSelectResult(false, $"No pawn has id {pawnId}");
                }
                if (pawn.OwnerId != playerId)
                {
                    return new CardSelectResult(false, $"You don't control {pawn.Name}");
                }

                Card? card = pawn.Hand.FirstOrDefault(c => c.PublicId == cardId);
                if (card is null)
                {
                    return new CardSelectResult(false, $"That card isn't in {pawn.Name}'s hand");
                }

                BoardPosition? aim = null;
                if (card.TargetType == CardTargetType.Tile)
                {
                    if (target is null)
                    {
                        return new CardSelectResult(false, $"{card.Name} needs a tile to aim at");
                    }

                    BoardPosition column = _movement.GetColumn(target);
                    if (!_boardService.IsOnBoard(column))
                    {
                        return new CardSelectResult(false, "That tile isn't on the board");
                    }
                    aim = _movement.GetStandPosition(column) ?? column;
                }

                pawn.SelectedCard = new CardSelection(card.PublicId, aim);
                pawn.PlannedPath = PlanPath(pawn, card, aim, OccupiedBy(pawns, pawn));

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.CardSelected, _turnId, new[] { pawn });

                _logger.Debug("Turn {TurnId}: player {PlayerId} chose {Card} for {Pawn}", _turnId, playerId, card.Name, pawn.Name);
                return new CardSelectResult(true);
            }
        }

        /// <summary>
        /// Does the pawns' work for the turn's phase. Finishes all of it before returning, so the next phase only begins
        /// once every pawn is done.
        /// </summary>
        public Task RunPhaseAsync(Turn turn, CancellationToken cancellationToken)
        {
            switch (turn.CurrentPhase)
            {
                case TurnPhase.Upkeep:
                    DrawHands(turn.Id);
                    break;
                case TurnPhase.Planning:
                    PlanCards(turn.Id);
                    break;
                case TurnPhase.Player:
                    OpenSelection(turn.Id);
                    break;
                case TurnPhase.Resolution:
                    ResolveCards(turn.Id);
                    break;
                case TurnPhase.End:
                    ReturnHands(turn.Id);
                    break;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Upkeep: each pawn draws up to PawnSettings:HandSize cards from its deck
        /// </summary>
        private void DrawHands(int turnId)
        {
            int handSize = Math.Max(0, _configuration.GetValue("PawnSettings:HandSize", 5));

            lock (_lock)
            {
                _turnId = turnId;
                _selectionOpen = false;

                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                foreach (Pawn pawn in pawns)
                {
                    _cardService.DrawCards(pawn, handSize - pawn.Hand.Count);
                }

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.CardsDrawn, turnId, pawns);
            }
        }

        /// <summary>
        /// Planning: each pawn tries the cards in its hand in a random order and chooses the first it can use, with a target
        /// if it needs one. Pawns that move pick a destination no other pawn stands on or has already picked.
        /// A pawn with no usable card chooses nothing. Pawns players control choose too; their players can change it later.
        /// </summary>
        private void PlanCards(int turnId)
        {
            lock (_lock)
            {
                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                HashSet<BoardPosition> claimed = new HashSet<BoardPosition>();
                foreach (Pawn pawn in pawns)
                {
                    pawn.SelectedCard = null;
                    pawn.PlannedPath = Array.Empty<BoardPosition>();

                    CardActionContext context = new CardActionContext()
                    {
                        Pawn = pawn,
                        Occupied = OccupiedBy(pawns, pawn),
                        Claimed = claimed,
                        Random = _random
                    };

                    foreach (Card card in _cardService.Shuffle(pawn.Hand, _random))
                    {
                        if (_cardService.GetAction(card) is not ICardAction action || !action.TryChooseTarget(context, out BoardPosition? target))
                        {
                            continue;
                        }

                        pawn.SelectedCard = new CardSelection(card.PublicId, target);
                        context.Target = target;
                        pawn.PlannedPath = action.PlanPath(context);
                        if (pawn.PlannedPath.Count > 0)
                        {
                            claimed.Add(_movement.GetColumn(pawn.PlannedPath[^1]));
                        }
                        break;
                    }
                }

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.Planned, turnId, pawns);

                _logger.Debug("Turn {TurnId}: {Chosen} of {Count} pawns chose a card", turnId, pawns.Count(p => p.SelectedCard is not null), pawns.Count);
            }
        }

        /// <summary>
        /// Player: players may now change the cards their pawns will play, until Resolution begins
        /// </summary>
        private void OpenSelection(int turnId)
        {
            lock (_lock)
            {
                _turnId = turnId;
                _selectionOpen = true;
            }
        }

        /// <summary>
        /// Resolution: each pawn plays its chosen card, highest Dexterity first (ties in random order), against the board
        /// and the other pawns as they are at that moment, so earlier pawns' moves can block later ones. Cards stay in the
        /// hand; choices and plans are cleared afterwards.
        /// </summary>
        private void ResolveCards(int turnId)
        {
            lock (_lock)
            {
                _selectionOpen = false;

                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                List<Pawn> order = pawns
                    .Select(p => (Pawn: p, TieBreak: _random.Next()))
                    .OrderByDescending(entry => entry.Pawn.Attributes.Dexterity)
                    .ThenBy(entry => entry.TieBreak)
                    .Select(entry => entry.Pawn)
                    .ToList();

                List<PawnMovement> movements = new List<PawnMovement>();
                List<PawnSpeech> speeches = new List<PawnSpeech>();

                foreach (Pawn pawn in order)
                {
                    CardSelection? selection = pawn.SelectedCard;
                    Card? card = selection is null ? null : pawn.Hand.FirstOrDefault(c => c.PublicId == selection.CardId);

                    pawn.SelectedCard = null;
                    pawn.PlannedPath = Array.Empty<BoardPosition>();

                    if (card is null || _cardService.GetAction(card) is not ICardAction action)
                    {
                        continue;
                    }

                    CardActionResult result = action.Resolve(new CardActionContext()
                    {
                        Pawn = pawn,
                        Target = selection!.Target,
                        Occupied = OccupiedBy(pawns, pawn),
                        Random = _random
                    });

                    if (result.Path.Count > 0 || result.Blocked || result.Position != pawn.Position)
                    {
                        movements.Add(new PawnMovement(pawn.PublicId, result.Path, result.Position, result.Blocked));
                    }
                    pawn.Position = result.Position;

                    if (result.Speech is string speech)
                    {
                        speeches.Add(new PawnSpeech(pawn.PublicId, speech));
                    }
                }

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.Resolved, turnId, pawns, movements, speeches);

                _logger.Debug("Turn {TurnId}: {Moved} pawns moved ({Blocked} blocked), {Spoke} spoke",
                    turnId, movements.Count(m => m.Path.Count > 0), movements.Count(m => m.Blocked), speeches.Count);
            }
        }

        /// <summary>
        /// End: each pawn shuffles its hand back into its deck
        /// </summary>
        private void ReturnHands(int turnId)
        {
            lock (_lock)
            {
                _selectionOpen = false;

                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                foreach (Pawn pawn in pawns)
                {
                    _cardService.ReturnHand(pawn, _random);
                    pawn.PlannedPath = Array.Empty<BoardPosition>();
                }

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.CardsReturned, turnId, pawns);
            }
        }

        /// <summary>
        /// A player joined or left: hand pawns out again. Raised under the player lock, so this runs in order.
        /// </summary>
        private void OnPlayersChanged(object? sender, PlayersChangeEvent change)
        {
            lock (_lock)
            {
                List<Pawn> pawns = CloneAll(GetLoadedPawns()).ToList();
                if (!AssignOwners(pawns, change.Players))
                {
                    return;
                }

                SavePawns(pawns);
                RaiseChanged(PawnChangeReason.OwnersChanged, 0, pawns);

                _logger.Information("Pawn owners changed: {Owned} of {Count} pawns are controlled by players", pawns.Count(p => p.OwnerId is not null), pawns.Count);
            }
        }

        /// <summary>
        /// Gives each player (in the order they joined) pawns no one controls until they have PlayerSettings:PawnsPerPlayer,
        /// marking each with the player's name and colour.
        /// Pawns of players who have left go back to no one, as do any a player has beyond the limit (if it was lowered).
        /// Returns true if any pawn's owner changed. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private bool AssignOwners(List<Pawn> pawns, IReadOnlyList<Player> players)
        {
            int perPlayer = Math.Max(0, _configuration.GetValue("PlayerSettings:PawnsPerPlayer", 2));
            Dictionary<Guid, Player> joined = players.ToDictionary(p => p.PublicId);
            Dictionary<Guid, int> owned = new Dictionary<Guid, int>();
            bool changed = false;

            foreach (Pawn pawn in pawns.Where(p => p.OwnerId is not null))
            {
                Guid ownerId = pawn.OwnerId!.Value;
                int count = owned.GetValueOrDefault(ownerId);
                if (!joined.ContainsKey(ownerId) || count >= perPlayer)
                {
                    pawn.OwnerId = null;
                    pawn.OwnerName = string.Empty;
                    pawn.OwnerColour = string.Empty;
                    changed = true;
                    continue;
                }
                owned[ownerId] = count + 1;
            }

            foreach (Player player in players)
            {
                int count = owned.GetValueOrDefault(player.PublicId);
                foreach (Pawn pawn in pawns.Where(p => p.OwnerId is null).Take(perPlayer - count))
                {
                    pawn.OwnerId = player.PublicId;
                    pawn.OwnerName = player.Name;
                    pawn.OwnerColour = player.Colour;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// The path <paramref name="pawn"/> expects to walk if it plays <paramref name="card"/> at <paramref name="target"/>
        /// </summary>
        private IReadOnlyList<BoardPosition> PlanPath(Pawn pawn, Card card, BoardPosition? target, IReadOnlySet<BoardPosition> occupied)
        {
            if (_cardService.GetAction(card) is not ICardAction action)
            {
                return Array.Empty<BoardPosition>();
            }

            return action.PlanPath(new CardActionContext()
            {
                Pawn = pawn,
                Target = target,
                Occupied = occupied,
                Random = _random
            });
        }

        /// <summary>
        /// The columns every pawn except <paramref name="pawn"/> stands on
        /// </summary>
        private IReadOnlySet<BoardPosition> OccupiedBy(IEnumerable<Pawn> pawns, Pawn pawn)
        {
            return pawns.Where(p => p.PublicId != pawn.PublicId).Select(p => _movement.GetColumn(p.Position)).ToHashSet();
        }

        /// <summary>
        /// Creates the <paramref name="index"/>th pawn of a new set, standing at <paramref name="position"/>: gives it a name
        /// (numbered once the name list runs out) and colour, rolls its attributes, starts it at full health and deals it
        /// a shuffled deck of <paramref name="startingDeck"/>. Caller must hold <see cref="_lock"/>, since it uses the shared random.
        /// </summary>
        private Pawn CreatePawn(int index, BoardPosition position, IReadOnlyList<string> startingDeck)
        {
            Pawn pawn = new Pawn()
            {
                PublicId = Guid.NewGuid(),
                Name = index < Names.Length ? Names[index] : $"{Names[index % Names.Length]} {index / Names.Length + 1}",
                Colour = Colours[index % Colours.Length],
                Position = position,
                Attributes = RollAttributes(),
                Deck = _cardService.Shuffle(_cardService.CreateDeck(startingDeck), _random)
            };
            pawn.MaxHealth = GetMaxHealth(pawn);
            pawn.Health = pawn.MaxHealth;
            return pawn;
        }

        /// <summary>
        /// Rolls every attribute as a random whole number from <see cref="MinAttribute"/> to <see cref="MaxAttribute"/>,
        /// then sets Physique from them. Caller must hold <see cref="_lock"/>, since it uses the shared random.
        /// </summary>
        private PawnAttributes RollAttributes()
        {
            PawnAttributes attributes = new PawnAttributes()
            {
                Strength = _random.Next(MinAttribute, MaxAttribute + 1),
                Dexterity = _random.Next(MinAttribute, MaxAttribute + 1),
                Intelligence = _random.Next(MinAttribute, MaxAttribute + 1),
                Wisdom = _random.Next(MinAttribute, MaxAttribute + 1),
                Constitution = _random.Next(MinAttribute, MaxAttribute + 1)
            };
            attributes.Physique = CalculatePhysique(attributes);
            return attributes;
        }

        /// <summary>
        /// Physique: Dexterity + (Strength × 2)
        /// </summary>
        private static int CalculatePhysique(PawnAttributes attributes)
        {
            return attributes.Dexterity + attributes.Strength * 2;
        }

        private void Shuffle<T>(List<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Raises <see cref="PawnsChanged"/> with snapshots, so listeners can't change the stored pawns. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void RaiseChanged(PawnChangeReason reason, int turnId, IReadOnlyList<Pawn> pawns,
            IReadOnlyList<PawnMovement>? movements = null, IReadOnlyList<PawnSpeech>? speeches = null)
        {
            PawnsChanged?.Invoke(this, new PawnsChangeEvent(reason, turnId, CloneAll(pawns),
                movements ?? Array.Empty<PawnMovement>(), speeches ?? Array.Empty<PawnSpeech>()));
        }

        /// <summary>
        /// The working copy of the stored pawns, reading them from the database the first time.
        /// Caller must hold <see cref="_lock"/>, and must not change them.
        /// </summary>
        private IReadOnlyList<Pawn> GetLoadedPawns()
        {
            if (!_pawnsLoaded)
            {
                ILookup<int, PawnCardRecord> cardsByPawn = _repository.GetPawnCards().ToLookup(c => c.PawnRecordId);
                _pawns = _repository.GetPawns().Select(record => FromRecord(record, cardsByPawn[record.Id])).ToArray();
                _pawnsLoaded = true;
            }
            return _pawns;
        }

        /// <summary>
        /// Stores the pawns and their cards in place of all the current ones and makes them the working copy, with their
        /// stored ids. The caller must not change them afterwards. Saves first, so a failed write leaves the working copy
        /// matching the database. Planned paths aren't stored. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void SavePawns(IReadOnlyList<Pawn> pawns)
        {
            PawnRecord[] pawnRecords = pawns.Select((pawn, i) => ToRecord(pawn, i)).ToArray();
            Dictionary<Guid, PawnCardRecord[]> cardRecords = pawns.ToDictionary(
                pawn => pawn.PublicId,
                pawn => pawn.Deck.Select((card, i) => _cardService.ToRecord(card, CardLocation.Deck, i))
                    .Concat(pawn.Hand.Select((card, i) => _cardService.ToRecord(card, CardLocation.Hand, i)))
                    .ToArray());

            _repository.SavePawns(pawnRecords, cardRecords.ToDictionary(c => c.Key, c => (IReadOnlyList<PawnCardRecord>)c.Value));

            // Keep the ids the database gave them
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                pawn.Id = pawnRecords[i].Id;
                PawnCardRecord[] cards = cardRecords[pawn.PublicId];
                int c = 0;
                foreach (Card card in pawn.Deck.Concat(pawn.Hand))
                {
                    card.Id = cards[c++].Id;
                }
            }

            _pawns = pawns;
            _pawnsLoaded = true;
        }

        /// <summary>
        /// Turns a pawn into the record it's stored as, without its cards or planned path.
        /// <paramref name="order"/> is the pawn's place in the list of pawns.
        /// </summary>
        private PawnRecord ToRecord(Pawn pawn, int order)
        {
            return new PawnRecord()
            {
                Id = pawn.Id,
                PublicId = pawn.PublicId,
                Order = order,
                Name = pawn.Name,
                Colour = pawn.Colour,
                PositionX = pawn.Position.X,
                PositionY = pawn.Position.Y,
                PositionZ = pawn.Position.Z,
                Strength = pawn.Attributes.Strength,
                Dexterity = pawn.Attributes.Dexterity,
                Intelligence = pawn.Attributes.Intelligence,
                Wisdom = pawn.Attributes.Wisdom,
                Constitution = pawn.Attributes.Constitution,
                Physique = pawn.Attributes.Physique,
                Health = pawn.Health,
                MaxHealth = pawn.MaxHealth,
                SelectedCardId = pawn.SelectedCard?.CardId,
                SelectedTargetX = pawn.SelectedCard?.Target?.X,
                SelectedTargetY = pawn.SelectedCard?.Target?.Y,
                SelectedTargetZ = pawn.SelectedCard?.Target?.Z,
                OwnerId = pawn.OwnerId,
                OwnerName = pawn.OwnerName,
                OwnerColour = pawn.OwnerColour
            };
        }

        /// <summary>
        /// Combines a stored pawn with the cards it holds into a pawn. Its planned path isn't stored, so it comes back empty.
        /// </summary>
        private Pawn FromRecord(PawnRecord record, IEnumerable<PawnCardRecord> cards)
        {
            BoardPosition? target = record is { SelectedTargetX: int x, SelectedTargetY: int y, SelectedTargetZ: int z }
                ? new BoardPosition(x, y, z)
                : null;

            return new Pawn()
            {
                Id = record.Id,
                PublicId = record.PublicId,
                Name = record.Name,
                Colour = record.Colour,
                Position = new BoardPosition(record.PositionX, record.PositionY, record.PositionZ),
                Attributes = new PawnAttributes()
                {
                    Strength = record.Strength,
                    Dexterity = record.Dexterity,
                    Intelligence = record.Intelligence,
                    Wisdom = record.Wisdom,
                    Constitution = record.Constitution,
                    Physique = record.Physique
                },
                Health = record.Health,
                MaxHealth = record.MaxHealth,
                Deck = CardsIn(cards, CardLocation.Deck),
                Hand = CardsIn(cards, CardLocation.Hand),
                SelectedCard = record.SelectedCardId is Guid cardId ? new CardSelection(cardId, target) : null,
                OwnerId = record.OwnerId,
                OwnerName = record.OwnerName,
                OwnerColour = record.OwnerColour
            };
        }

        /// <summary>
        /// The stored cards in one location, in order
        /// </summary>
        private IReadOnlyList<Card> CardsIn(IEnumerable<PawnCardRecord> cards, CardLocation location)
        {
            return cards.Where(c => c.Location == location).OrderBy(c => c.Order).Select(_cardService.FromRecord).ToArray();
        }

        /// <summary>
        /// Creates copies so callers can't change the stored pawns
        /// </summary>
        private IReadOnlyList<Pawn> CloneAll(IReadOnlyList<Pawn> pawns)
        {
            return pawns.Select(Clone).ToArray();
        }

        /// <summary>
        /// Creates a copy of one pawn, including its cards, so callers can't change the stored one
        /// </summary>
        private Pawn Clone(Pawn pawn)
        {
            return new Pawn()
            {
                Id = pawn.Id,
                PublicId = pawn.PublicId,
                Name = pawn.Name,
                Colour = pawn.Colour,
                Position = pawn.Position,
                Attributes = new PawnAttributes()
                {
                    Strength = pawn.Attributes.Strength,
                    Dexterity = pawn.Attributes.Dexterity,
                    Intelligence = pawn.Attributes.Intelligence,
                    Wisdom = pawn.Attributes.Wisdom,
                    Constitution = pawn.Attributes.Constitution,
                    Physique = pawn.Attributes.Physique
                },
                Health = pawn.Health,
                MaxHealth = pawn.MaxHealth,
                PlannedPath = pawn.PlannedPath.ToArray(),
                Deck = pawn.Deck.Select(_cardService.Clone).ToArray(),
                Hand = pawn.Hand.Select(_cardService.Clone).ToArray(),
                SelectedCard = pawn.SelectedCard,
                OwnerId = pawn.OwnerId,
                OwnerName = pawn.OwnerName,
                OwnerColour = pawn.OwnerColour
            };
        }
    }
}
