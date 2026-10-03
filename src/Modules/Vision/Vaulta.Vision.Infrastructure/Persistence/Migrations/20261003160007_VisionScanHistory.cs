using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Vision.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisionScanHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frozen_benchmarks",
                schema: "vision",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frozen_benchmarks", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "scan_attempts",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retention_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    operational_policy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    improvement_policy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scan_captures",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_captures", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_captures_scan_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "vision",
                        principalTable: "scan_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_runs",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    prediction_json = table.Column<string>(type: "jsonb", nullable: true),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: true),
                    embedding = table.Column<float[]>(type: "real[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_runs_scan_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "vision",
                        principalTable: "scan_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    orientation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    presence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_feedback_scan_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "vision",
                        principalTable: "scan_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_feedback_scan_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "vision",
                        principalTable: "scan_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_run_candidates",
                schema: "vision",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retrieval_score = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_run_candidates", x => new { x.run_id, x.rank });
                    table.ForeignKey(
                        name: "FK_scan_run_candidates_scan_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "vision",
                        principalTable: "scan_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reviewed_samples",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    review_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviewed_samples", x => x.id);
                    table.ForeignKey(
                        name: "FK_reviewed_samples_feedback_feedback_id",
                        column: x => x.feedback_id,
                        principalSchema: "vision",
                        principalTable: "feedback",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reviewed_samples_scan_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "vision",
                        principalTable: "scan_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reviewed_samples_scan_captures_capture_id",
                        column: x => x.capture_id,
                        principalSchema: "vision",
                        principalTable: "scan_captures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_attempt_id_idempotency_key",
                schema: "vision",
                table: "feedback",
                columns: new[] { "attempt_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_run_id",
                schema: "vision",
                table: "feedback",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_samples_attempt_id",
                schema: "vision",
                table: "reviewed_samples",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_samples_capture_id",
                schema: "vision",
                table: "reviewed_samples",
                column: "capture_id");

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_samples_feedback_id_capture_id",
                schema: "vision",
                table: "reviewed_samples",
                columns: new[] { "feedback_id", "capture_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_attempts_owner_id_created_at",
                schema: "vision",
                table: "scan_attempts",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_attempts_retention_until",
                schema: "vision",
                table: "scan_attempts",
                column: "retention_until");

            migrationBuilder.CreateIndex(
                name: "IX_scan_captures_asset_id",
                schema: "vision",
                table: "scan_captures",
                column: "asset_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_captures_attempt_id_sequence",
                schema: "vision",
                table: "scan_captures",
                columns: new[] { "attempt_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_runs_attempt_id_execution_key",
                schema: "vision",
                table: "scan_runs",
                columns: new[] { "attempt_id", "execution_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frozen_benchmarks",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "reviewed_samples",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "scan_run_candidates",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "feedback",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "scan_captures",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "scan_runs",
                schema: "vision");

            migrationBuilder.DropTable(
                name: "scan_attempts",
                schema: "vision");
        }
    }
}
