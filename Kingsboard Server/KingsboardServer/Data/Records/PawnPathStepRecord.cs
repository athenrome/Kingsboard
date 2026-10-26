namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of one step of the path a pawn plans to walk.
    /// Storage only: <c>PawnService</c> turns a pawn's steps into its planned path and back.
    /// </summary>
    public class PawnPathStepRecord : BaseModel
    {
        /// <summary>
        /// Unique id of the stored step
        /// </summary>
        public Guid StepId { get; set; }

        /// <summary>
        /// <see cref="BaseModel.Id"/> of the pawn whose path this is. Set when the pawn is saved.
        /// </summary>
        public int PawnRecordId { get; set; }

        /// <summary>
        /// Where the step comes in the path, from 0 (the first step)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Column of the step
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Row of the step
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Level of the step
        /// </summary>
        public int Z { get; set; }
    }
}
