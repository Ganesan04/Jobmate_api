using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobmate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One account is both a worker and a work giver, and there is no admin role.
    ///
    /// Data steps are written by hand and ordered around the generated schema steps: every
    /// row holding a value that is leaving an enum is rewritten after the old check is
    /// dropped and before the new one is added, or the new check would reject the table.
    ///
    /// Down restores the schema only. Deleted admin accounts and dispute rows are gone, and
    /// held payments that were released stay released.
    /// </summary>
    public partial class DualRoleWithoutAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disputes",
                schema: "jobmate");

            migrationBuilder.DropIndex(
                name: "ix_worker_profiles_verification",
                schema: "jobmate",
                table: "worker_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "chk_worker_profiles_verification_status",
                schema: "jobmate",
                table: "worker_profiles");

            migrationBuilder.DropIndex(
                name: "ix_verification_requests_status_created",
                schema: "jobmate",
                table: "verification_requests");

            migrationBuilder.DropIndex(
                name: "ix_users_role_active",
                schema: "jobmate",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_role",
                schema: "jobmate",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_payments_status",
                schema: "jobmate",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_notifications_reference_type",
                schema: "jobmate",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_giver_profiles_verification",
                schema: "jobmate",
                table: "giver_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "chk_giver_profiles_verification_status",
                schema: "jobmate",
                table: "giver_profiles");

            // Verification is the person's: users.verification_status is the one badge both
            // modes read, so the per-profile copies go.
            migrationBuilder.DropColumn(
                name: "verification_status",
                schema: "jobmate",
                table: "worker_profiles");

            migrationBuilder.DropColumn(
                name: "verification_status",
                schema: "jobmate",
                table: "giver_profiles");

            migrationBuilder.RenameColumn(
                name: "role",
                schema: "jobmate",
                table: "users",
                newName: "active_role");

            // Admin accounts. Their reviews are kept with the reviewer cleared, because the
            // FK is RESTRICT and the decision itself is still the applicant's history. Any
            // other RESTRICT reference (a job, a payment) makes this fail loudly rather than
            // delete marketplace data.
            migrationBuilder.Sql("""
                UPDATE jobmate.verification_requests SET reviewed_by_user_id = NULL
                WHERE reviewed_by_user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

                UPDATE jobmate.worker_documents SET reviewed_by_user_id = NULL
                WHERE reviewed_by_user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

                DELETE FROM jobmate.refresh_tokens
                WHERE user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

                DELETE FROM jobmate.user_devices
                WHERE user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

                DELETE FROM jobmate.users WHERE active_role = 'Admin';
                """);

            // Money held for a dispute settles normally: it had already been paid.
            migrationBuilder.Sql("""
                UPDATE jobmate.payments SET status = 'Paid', updated_at = now()
                WHERE status = 'Disputed';
                """);

            // Dispute notifications stay in the inbox but no longer link to a screen that
            // does not exist.
            migrationBuilder.Sql("""
                UPDATE jobmate.notifications
                SET reference_type = 'None', reference_id = NULL, route = NULL, updated_at = now()
                WHERE reference_type = 'Dispute';
                """);

            // Every user has both profiles. Inserted after the verification_status columns
            // are dropped, so only the columns registration also fills are needed.
            migrationBuilder.Sql("""
                INSERT INTO jobmate.worker_profiles (worker_profile_id, user_id, location, created_at, updated_at)
                SELECT gen_random_uuid(), u.user_id, NULLIF(u.area, ''), now(), now()
                FROM jobmate.users u
                WHERE NOT EXISTS (SELECT 1 FROM jobmate.worker_profiles p WHERE p.user_id = u.user_id);

                INSERT INTO jobmate.giver_profiles (giver_profile_id, user_id, kind, business_name, contact_person, location, created_at, updated_at)
                SELECT gen_random_uuid(), u.user_id, 'Individual',
                       btrim(u.first_name || ' ' || u.last_name),
                       NULLIF(btrim(u.first_name || ' ' || u.last_name), ''),
                       NULLIF(u.area, ''), now(), now()
                FROM jobmate.users u
                WHERE NOT EXISTS (SELECT 1 FROM jobmate.giver_profiles p WHERE p.user_id = u.user_id);
                """);

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_active_role",
                schema: "jobmate",
                table: "users",
                sql: "\"active_role\" IN ('Worker', 'WorkGiver')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_payments_status",
                schema: "jobmate",
                table: "payments",
                sql: "\"status\" IN ('Pending', 'Processing', 'Paid', 'Failed', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_notifications_reference_type",
                schema: "jobmate",
                table: "notifications",
                sql: "\"reference_type\" IN ('Job', 'Application', 'Assignment', 'Payment', 'Conversation', 'Verification', 'None')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_active_role",
                schema: "jobmate",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_payments_status",
                schema: "jobmate",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_notifications_reference_type",
                schema: "jobmate",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "active_role",
                schema: "jobmate",
                table: "users",
                newName: "role");

            // NotVerified rather than EF's empty-string default, which the check constraint
            // re-added below would reject. Then copied from the user, which is what the
            // profile columns held before this migration.
            migrationBuilder.AddColumn<string>(
                name: "verification_status",
                schema: "jobmate",
                table: "worker_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotVerified");

            migrationBuilder.AddColumn<string>(
                name: "verification_status",
                schema: "jobmate",
                table: "giver_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotVerified");

            migrationBuilder.Sql("""
                UPDATE jobmate.worker_profiles p SET verification_status = u.verification_status
                FROM jobmate.users u WHERE u.user_id = p.user_id AND u.verification_status IS NOT NULL;

                UPDATE jobmate.giver_profiles p SET verification_status = u.verification_status
                FROM jobmate.users u WHERE u.user_id = p.user_id AND u.verification_status IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "jobmate",
                columns: table => new
                {
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    against_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.dispute_id);
                    table.CheckConstraint("chk_disputes_reason", "\"reason\" IN ('UnsafeWorkplace', 'NotPaid', 'AbusiveBehaviour', 'FakeJob', 'NoShow', 'Other')");
                    table.CheckConstraint("chk_disputes_status", "\"status\" IN ('Open', 'UnderReview', 'Resolved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_disputes_against_user_id",
                        column: x => x.against_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_disputes_raised_by_user_id",
                        column: x => x.raised_by_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_worker_profiles_verification",
                schema: "jobmate",
                table: "worker_profiles",
                column: "verification_status");

            migrationBuilder.AddCheckConstraint(
                name: "chk_worker_profiles_verification_status",
                schema: "jobmate",
                table: "worker_profiles",
                sql: "\"verification_status\" IN ('NotVerified', 'Pending', 'Verified', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_status_created",
                schema: "jobmate",
                table: "verification_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_role_active",
                schema: "jobmate",
                table: "users",
                columns: new[] { "role", "is_active" });

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_role",
                schema: "jobmate",
                table: "users",
                sql: "\"role\" IN ('Worker', 'WorkGiver', 'Admin')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_payments_status",
                schema: "jobmate",
                table: "payments",
                sql: "\"status\" IN ('Pending', 'Processing', 'Paid', 'Disputed', 'Failed', 'Refunded', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_notifications_reference_type",
                schema: "jobmate",
                table: "notifications",
                sql: "\"reference_type\" IN ('Job', 'Application', 'Assignment', 'Payment', 'Conversation', 'Dispute', 'Verification', 'None')");

            migrationBuilder.CreateIndex(
                name: "ix_giver_profiles_verification",
                schema: "jobmate",
                table: "giver_profiles",
                column: "verification_status");

            migrationBuilder.AddCheckConstraint(
                name: "chk_giver_profiles_verification_status",
                schema: "jobmate",
                table: "giver_profiles",
                sql: "\"verification_status\" IN ('NotVerified', 'Pending', 'Verified', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_against_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "against_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_job_status",
                schema: "jobmate",
                table: "disputes",
                columns: new[] { "job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_raised_by_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "raised_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_resolved_by_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_created",
                schema: "jobmate",
                table: "disputes",
                columns: new[] { "status", "created_at" });
        }
    }
}
