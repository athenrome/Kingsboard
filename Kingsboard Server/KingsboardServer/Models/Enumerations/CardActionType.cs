namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// What a card does when it is played. Each value is carried out by one <c>ICardAction</c>.
    /// </summary>
    public enum CardActionType
    {
        /// <summary>
        /// Walk towards a target tile, up to as many steps as the pawn's Dexterity
        /// </summary>
        Move,

        /// <summary>
        /// Say a random whole number from 1 to 100
        /// </summary>
        SayRandomNumber,

        /// <summary>
        /// Say the name of a random colour
        /// </summary>
        SayRandomColour,

        /// <summary>
        /// Say "Hello, my name is ..." with the pawn's name
        /// </summary>
        SayHello
    }
}
