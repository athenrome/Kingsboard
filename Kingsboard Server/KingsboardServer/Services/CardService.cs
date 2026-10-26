using Kingsboard.Shared.Models.Cards;
using Kingsboard.Shared.Models.Enumerations;
using Kingsboard.Shared.Models.Pawns;
using KingsboardServer.Data.Records;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Owns the action cards. The kinds of card are listed here in code; each one names the <see cref="CardActionType"/>
    /// that an <see cref="ICardAction"/> carries out.
    /// </summary>
    public class CardService : ICardService
    {
        /// <summary>
        /// Id of the Move card
        /// </summary>
        public const string MoveCardId = "move";

        /// <summary>
        /// Id of the test card that says a random number
        /// </summary>
        public const string SayNumberCardId = "say-number";

        /// <summary>
        /// Id of the test card that says a random colour
        /// </summary>
        public const string SayColourCardId = "say-colour";

        /// <summary>
        /// Id of the test card that says hello
        /// </summary>
        public const string SayHelloCardId = "say-hello";

        private static readonly CardDefinition[] Definitions =
        {
            new CardDefinition()
            {
                Id = MoveCardId,
                Name = "Move",
                Description = "Walk towards a tile, up to as many steps as your Dexterity.",
                TargetType = CardTargetType.Tile,
                Action = CardActionType.Move
            },
            new CardDefinition()
            {
                Id = SayNumberCardId,
                Name = "Pick a Number",
                Description = "Say a random number from 1 to 100.",
                TargetType = CardTargetType.None,
                Action = CardActionType.SayRandomNumber
            },
            new CardDefinition()
            {
                Id = SayColourCardId,
                Name = "Pick a Colour",
                Description = "Say a random colour.",
                TargetType = CardTargetType.None,
                Action = CardActionType.SayRandomColour
            },
            new CardDefinition()
            {
                Id = SayHelloCardId,
                Name = "Introduce Yourself",
                Description = "Say hello and tell everyone your name.",
                TargetType = CardTargetType.None,
                Action = CardActionType.SayHello
            }
        };

        private readonly Serilog.ILogger _logger;
        private readonly IReadOnlyDictionary<CardActionType, ICardAction> _actions;
        private readonly ICardRepository _repository;

        // The definitions as stored in the database, read (after storing the ones in code) the first time a card is
        // stored or read back. Guarded by _storedLock.
        private readonly object _storedLock = new();
        private StoredDefinitions? _stored;

        /// <summary>
        /// Creates the service with every registered card action. Throws if two actions claim the same <see cref="CardActionType"/>.
        /// The definitions are stored in <paramref name="repository"/> the first time they're needed.
        /// </summary>
        public CardService(Serilog.ILogger logger, IEnumerable<ICardAction> actions, ICardRepository repository)
        {
            _logger = logger;
            _repository = repository;
            _actions = actions.ToDictionary(a => a.Action);

            foreach (CardDefinition definition in Definitions.Where(d => !_actions.ContainsKey(d.Action)))
            {
                _logger.Warning("Card {CardId} has no action registered for {Action}; it will do nothing", definition.Id, definition.Action);
            }
        }

        /// <summary>
        /// Every kind of card in the game
        /// </summary>
        public IReadOnlyList<CardDefinition> GetDefinitions()
        {
            return Definitions;
        }

        /// <summary>
        /// The kind of card with this id (case-insensitive), or null if there isn't one
        /// </summary>
        public CardDefinition? GetDefinition(string definitionId)
        {
            return Definitions.FirstOrDefault(d => string.Equals(d.Id, definitionId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Makes a fresh copy, with a new id, of each listed card. Unknown ids are logged and skipped.
        /// </summary>
        public IReadOnlyList<Card> CreateDeck(IEnumerable<string> definitionIds)
        {
            List<Card> deck = new List<Card>();
            foreach (string id in definitionIds)
            {
                if (GetDefinition(id) is not CardDefinition definition)
                {
                    _logger.Warning("Unknown card {CardId} in a starting deck; skipping it", id);
                    continue;
                }

                deck.Add(new Card()
                {
                    PublicId = Guid.NewGuid(),
                    DefinitionId = definition.Id,
                    Name = definition.Name,
                    Description = definition.Description,
                    ImageId = definition.ImageId,
                    TargetType = definition.TargetType
                });
            }
            return deck;
        }

        /// <summary>
        /// The action registered for the card's definition, or null if there is none
        /// </summary>
        public ICardAction? GetAction(Card card)
        {
            if (GetDefinition(card.DefinitionId) is not CardDefinition definition)
            {
                return null;
            }
            return _actions.TryGetValue(definition.Action, out ICardAction? action) ? action : null;
        }

        /// <summary>
        /// Moves cards from the front of the deck to the end of the hand, so a drawn card is never in both
        /// </summary>
        public void DrawCards(Pawn pawn, int count)
        {
            int drawn = Math.Clamp(count, 0, pawn.Deck.Count);
            pawn.Hand = pawn.Hand.Concat(pawn.Deck.Take(drawn)).ToArray();
            pawn.Deck = pawn.Deck.Skip(drawn).ToArray();
        }

        /// <summary>
        /// Puts the hand back into the deck, shuffles the deck, and clears the chosen card
        /// </summary>
        public void ReturnHand(Pawn pawn, Random random)
        {
            pawn.Deck = Shuffle(pawn.Deck.Concat(pawn.Hand).ToArray(), random);
            pawn.Hand = Array.Empty<Card>();
            pawn.SelectedCard = null;
        }

        /// <summary>
        /// A Fisher-Yates shuffle into a new list
        /// </summary>
        public IReadOnlyList<Card> Shuffle(IReadOnlyList<Card> cards, Random random)
        {
            Card[] shuffled = cards.ToArray();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return shuffled;
        }

        /// <summary>
        /// A copy of the card, so callers can't change the original
        /// </summary>
        public Card Clone(Card card)
        {
            return new Card()
            {
                Id = card.Id,
                PublicId = card.PublicId,
                DefinitionId = card.DefinitionId,
                Name = card.Name,
                Description = card.Description,
                ImageId = card.ImageId,
                TargetType = card.TargetType
            };
        }

        /// <summary>
        /// Turns a card into the record of who holds it and where: in <paramref name="location"/> at position
        /// <paramref name="order"/> (0 = first). The record keeps the card's PublicId and points at its stored definition;
        /// it's linked to its pawn when the pawn is saved. Throws if the card's definition isn't in code.
        /// </summary>
        public PawnCardRecord ToRecord(Card card, CardLocation location, int order)
        {
            StoredDefinitions stored = GetStoredDefinitions();
            if (!stored.RecordIds.TryGetValue(card.DefinitionId, out int cardRecordId))
            {
                throw new InvalidOperationException($"Card {card.PublicId} is a copy of {card.DefinitionId}, which isn't a known card");
            }

            return new PawnCardRecord()
            {
                Id = card.Id,
                PublicId = card.PublicId,
                CardRecordId = cardRecordId,
                Location = location,
                Order = order
            };
        }

        /// <summary>
        /// Combines a stored card with its stored definition into a card. Throws if the definition isn't stored.
        /// </summary>
        public Card FromRecord(PawnCardRecord record)
        {
            StoredDefinitions stored = GetStoredDefinitions();
            if (!stored.Records.TryGetValue(record.CardRecordId, out CardRecord? definition))
            {
                throw new InvalidOperationException($"Card {record.PublicId} is a copy of card definition {record.CardRecordId}, which isn't stored");
            }

            return new Card()
            {
                Id = record.Id,
                PublicId = record.PublicId,
                DefinitionId = definition.DefinitionId,
                Name = definition.Name,
                Description = definition.Description,
                ImageId = definition.ImageId,
                TargetType = definition.TargetType
            };
        }

        /// <summary>
        /// The stored card definitions, storing the ones in code first if this is the first time they're needed
        /// </summary>
        private StoredDefinitions GetStoredDefinitions()
        {
            lock (_storedLock)
            {
                if (_stored is null)
                {
                    IReadOnlyList<CardRecord> records = _repository.SaveDefinitions(Definitions.Select(ToRecord).ToArray());
                    _stored = new StoredDefinitions(
                        records.ToDictionary(r => r.DefinitionId, r => r.Id, StringComparer.OrdinalIgnoreCase),
                        records.ToDictionary(r => r.Id));
                }
                return _stored;
            }
        }

        /// <summary>
        /// Turns a definition in code into the record it's stored as
        /// </summary>
        private static CardRecord ToRecord(CardDefinition definition)
        {
            return new CardRecord()
            {
                DefinitionId = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                ImageId = definition.ImageId,
                TargetType = definition.TargetType,
                Action = definition.Action
            };
        }

        /// <summary>
        /// The stored card definitions, looked up both ways
        /// </summary>
        /// <param name="RecordIds">Each stored definition's id, by its DefinitionId</param>
        /// <param name="Records">Each stored definition, by its id</param>
        private record StoredDefinitions(IReadOnlyDictionary<string, int> RecordIds, IReadOnlyDictionary<int, CardRecord> Records);
    }
}
