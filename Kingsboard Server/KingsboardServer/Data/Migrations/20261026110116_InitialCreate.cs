using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KingsboardServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Boards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pawns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PawnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Colour = table.Column<string>(type: "TEXT", nullable: false),
                    PositionX = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionY = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionZ = table.Column<int>(type: "INTEGER", nullable: false),
                    Strength = table.Column<int>(type: "INTEGER", nullable: false),
                    Dexterity = table.Column<int>(type: "INTEGER", nullable: false),
                    Intelligence = table.Column<int>(type: "INTEGER", nullable: false),
                    Wisdom = table.Column<int>(type: "INTEGER", nullable: false),
                    Constitution = table.Column<int>(type: "INTEGER", nullable: false),
                    Physique = table.Column<int>(type: "INTEGER", nullable: false),
                    Health = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxHealth = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedCardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectedTargetX = table.Column<int>(type: "INTEGER", nullable: true),
                    SelectedTargetY = table.Column<int>(type: "INTEGER", nullable: true),
                    SelectedTargetZ = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerName = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerColour = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pawns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardTiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Z = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardTiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardTiles_Boards_BoardRecordId",
                        column: x => x.BoardRecordId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PawnRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Pile = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ImageId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_Pawns_PawnRecordId",
                        column: x => x.PawnRecordId,
                        principalTable: "Pawns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PawnPathSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PawnRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Z = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PawnPathSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PawnPathSteps_Pawns_PawnRecordId",
                        column: x => x.PawnRecordId,
                        principalTable: "Pawns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_BoardId",
                table: "Boards",
                column: "BoardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTiles_BoardRecordId_X_Y_Z",
                table: "BoardTiles",
                columns: new[] { "BoardRecordId", "X", "Y", "Z" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTiles_TileId",
                table: "BoardTiles",
                column: "TileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CardId",
                table: "Cards",
                column: "CardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cards_PawnRecordId_Pile_Order",
                table: "Cards",
                columns: new[] { "PawnRecordId", "Pile", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_MessageId",
                table: "ChatMessages",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_Sequence",
                table: "ChatMessages",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PawnPathSteps_PawnRecordId_Order",
                table: "PawnPathSteps",
                columns: new[] { "PawnRecordId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_PawnPathSteps_StepId",
                table: "PawnPathSteps",
                column: "StepId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pawns_PawnId",
                table: "Pawns",
                column: "PawnId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoardTiles");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "PawnPathSteps");

            migrationBuilder.DropTable(
                name: "Boards");

            migrationBuilder.DropTable(
                name: "Pawns");
        }
    }
}
