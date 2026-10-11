using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DyeHouseERP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadyGoodsTransferStageExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StageExecutionId",
                table: "ReadyGoodsTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadyGoodsTransfers_StageExecutionId",
                table: "ReadyGoodsTransfers",
                column: "StageExecutionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReadyGoodsTransfers_ProductionOrderStageExecutions_StageExecutionId",
                table: "ReadyGoodsTransfers",
                column: "StageExecutionId",
                principalTable: "ProductionOrderStageExecutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadyGoodsTransfers_ProductionOrderStageExecutions_StageExecutionId",
                table: "ReadyGoodsTransfers");

            migrationBuilder.DropIndex(
                name: "IX_ReadyGoodsTransfers_StageExecutionId",
                table: "ReadyGoodsTransfers");

            migrationBuilder.DropColumn(
                name: "StageExecutionId",
                table: "ReadyGoodsTransfers");
        }
    }
}
