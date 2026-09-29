using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "jobmate");

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    last_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    profile_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_login_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    latitude = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    longitude = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "giver_profiles",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    business_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    contact_person = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gst_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    average_rating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false, defaultValue: 0m),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_jobs_posted = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_hired = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_giver_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_giver_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    giver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    workers_needed = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    workers_filled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    experience = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    job_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    report_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    job_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    weekdays = table.Column<int[]>(type: "integer[]", nullable: false),
                    pay = table.Column<int>(type: "integer", nullable: false),
                    pay_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    overtime_rate = table.Column<int>(type: "integer", nullable: true),
                    travel_allowance = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    food_allowance = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    workplace_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    contact_person = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    safety_information = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    physical_requirements = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dress_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vehicle_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    emergency_contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_urgent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    application_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                    table.CheckConstraint("ck_jobs_workers_filled", "\"workers_filled\" >= 0");
                    table.CheckConstraint("ck_jobs_workers_needed", "\"workers_needed\" > 0");
                    table.ForeignKey(
                        name: "FK_jobs_users_giver_id",
                        column: x => x.giver_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    push_dispatched_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_reports",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    related_job_id = table.Column<string>(type: "text", nullable: true),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_reports_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_devices",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    app_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_devices_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verification_requests",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_verification_requests_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_verification_requests_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "worker_profiles",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    experience = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    availability = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expected_pay = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    expected_pay_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    average_rating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false, defaultValue: 0m),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_completed_jobs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_applications = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_online = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_active_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_worker_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_message_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    last_message_preview = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_message_sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversations_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    against_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_disputes_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_disputes_users_against_user_id",
                        column: x => x.against_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_disputes_users_raised_by_user_id",
                        column: x => x.raised_by_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_disputes_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_applications",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expected_pay = table.Column<int>(type: "integer", nullable: true),
                    available_from = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    shortlisted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    withdrawn_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    accepted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    proposed_pay = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_applications_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_applications_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_skills",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_skills_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_weekdays",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_weekdays", x => x.Id);
                    table.CheckConstraint("ck_job_weekdays_day_of_week", "\"day_of_week\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_job_weekdays_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    giver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    overtime_amount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    platform_fee = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_amount = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "INR"),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    refunded_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    agreed_pay_snapshot = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payments_total_amount", "\"total_amount\" = \"amount\" + \"overtime_amount\" + \"platform_fee\"");
                    table.ForeignKey(
                        name: "FK_payments_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_users_giver_id",
                        column: x => x.giver_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    worker_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating_direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ratings", x => x.Id);
                    table.CheckConstraint("ck_ratings_stars", "\"stars\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ratings_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ratings_users_from_user_id",
                        column: x => x.from_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ratings_users_to_user_id",
                        column: x => x.to_user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_jobs",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_saved_jobs_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_saved_jobs_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "worker_availability_slots",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeSpan>(type: "time", nullable: false),
                    end_time = table.Column<TimeSpan>(type: "time", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_availability_slots", x => x.Id);
                    table.CheckConstraint("ck_worker_availability_slots_day_of_week", "\"day_of_week\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_worker_availability_slots_worker_profiles_worker_profile_id",
                        column: x => x.worker_profile_id,
                        principalSchema: "jobmate",
                        principalTable: "worker_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "worker_documents",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_worker_documents_worker_profiles_worker_profile_id",
                        column: x => x.worker_profile_id,
                        principalSchema: "jobmate",
                        principalTable: "worker_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "worker_skills",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_worker_skills_worker_profiles_worker_profile_id",
                        column: x => x.worker_profile_id,
                        principalSchema: "jobmate",
                        principalTable: "worker_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unread_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_read_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversation_participants_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "jobmate",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_participants_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    message_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sent_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "jobmate",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "worker_assignments",
                schema: "jobmate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    giver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreed_pay = table.Column<int>(type: "integer", nullable: false),
                    overtime_amount = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    check_in_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    checked_in_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_worker_assignments_job_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "jobmate",
                        principalTable: "job_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_worker_assignments_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "jobmate",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_worker_assignments_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "jobmate",
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_worker_assignments_users_giver_id",
                        column: x => x.giver_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_worker_assignments_users_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "jobmate",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_conversation_user",
                schema: "jobmate",
                table: "conversation_participants",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_user_unread",
                schema: "jobmate",
                table: "conversation_participants",
                columns: new[] { "user_id", "is_archived", "unread_count" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_job_id",
                schema: "jobmate",
                table: "conversations",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_disputes_against_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "against_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_job_status",
                schema: "jobmate",
                table: "disputes",
                columns: new[] { "job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_disputes_raised_by_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "raised_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_disputes_resolved_by_user_id",
                schema: "jobmate",
                table: "disputes",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_created",
                schema: "jobmate",
                table: "disputes",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_giver_profiles_user_id",
                schema: "jobmate",
                table: "giver_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_giver_profiles_verification",
                schema: "jobmate",
                table: "giver_profiles",
                column: "verification_status");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_job_status",
                schema: "jobmate",
                table: "job_applications",
                columns: new[] { "job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_job_worker",
                schema: "jobmate",
                table: "job_applications",
                columns: new[] { "job_id", "worker_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_worker_status",
                schema: "jobmate",
                table: "job_applications",
                columns: new[] { "worker_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_job_skills_job_name",
                schema: "jobmate",
                table: "job_skills",
                columns: new[] { "job_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_skills_name",
                schema: "jobmate",
                table: "job_skills",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_job_weekdays_job_day",
                schema: "jobmate",
                table: "job_weekdays",
                columns: new[] { "job_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_area",
                schema: "jobmate",
                table: "jobs",
                column: "area");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_category_status_date",
                schema: "jobmate",
                table: "jobs",
                columns: new[] { "category", "status", "job_date" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_giver_status",
                schema: "jobmate",
                table: "jobs",
                columns: new[] { "giver_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_date",
                schema: "jobmate",
                table: "jobs",
                columns: new[] { "status", "job_date" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_sent",
                schema: "jobmate",
                table: "messages",
                columns: new[] { "conversation_id", "sent_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_messages_sender_id",
                schema: "jobmate",
                table: "messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_read_created",
                schema: "jobmate",
                table: "notifications",
                columns: new[] { "user_id", "is_read", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_expires",
                schema: "jobmate",
                table: "otp_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_phone_purpose_created",
                schema: "jobmate",
                table: "otp_challenges",
                columns: new[] { "phone_number", "purpose", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                schema: "jobmate",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_user_id",
                schema: "jobmate",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_assignment",
                schema: "jobmate",
                table: "payments",
                column: "assignment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_giver_idempotency",
                schema: "jobmate",
                table: "payments",
                columns: new[] { "giver_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_giver_status_created",
                schema: "jobmate",
                table: "payments",
                columns: new[] { "giver_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_job_id",
                schema: "jobmate",
                table: "payments",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_worker_status_created",
                schema: "jobmate",
                table: "payments",
                columns: new[] { "worker_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_from_user_id",
                schema: "jobmate",
                table: "ratings",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_job_from_to",
                schema: "jobmate",
                table: "ratings",
                columns: new[] { "job_id", "from_user_id", "to_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ratings_to_user_created",
                schema: "jobmate",
                table: "ratings",
                columns: new[] { "to_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "jobmate",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_revoked",
                schema: "jobmate",
                table: "refresh_tokens",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_jobs_job_id",
                schema: "jobmate",
                table: "saved_jobs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_jobs_user_job",
                schema: "jobmate",
                table: "saved_jobs",
                columns: new[] { "user_id", "job_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_reports_resolved_created",
                schema: "jobmate",
                table: "support_reports",
                columns: new[] { "is_resolved", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_support_reports_user_id",
                schema: "jobmate",
                table: "support_reports",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_devices_token",
                schema: "jobmate",
                table: "user_devices",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_devices_user_active",
                schema: "jobmate",
                table: "user_devices",
                columns: new[] { "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_users_area",
                schema: "jobmate",
                table: "users",
                column: "area");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "jobmate",
                table: "users",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                schema: "jobmate",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role_active",
                schema: "jobmate",
                table: "users",
                columns: new[] { "role", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_verification_requests_reviewed_by_user_id",
                schema: "jobmate",
                table: "verification_requests",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_status_created",
                schema: "jobmate",
                table: "verification_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_user_type_status",
                schema: "jobmate",
                table: "verification_requests",
                columns: new[] { "user_id", "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_assignments_application",
                schema: "jobmate",
                table: "worker_assignments",
                column: "application_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_worker_assignments_giver_id",
                schema: "jobmate",
                table: "worker_assignments",
                column: "giver_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_assignments_job_status",
                schema: "jobmate",
                table: "worker_assignments",
                columns: new[] { "job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_worker_assignments_payment_id",
                schema: "jobmate",
                table: "worker_assignments",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_worker_assignments_worker_status",
                schema: "jobmate",
                table: "worker_assignments",
                columns: new[] { "worker_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_availability_slots_profile_day",
                schema: "jobmate",
                table: "worker_availability_slots",
                columns: new[] { "worker_profile_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_documents_status",
                schema: "jobmate",
                table: "worker_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_worker_documents_worker_profile_id",
                schema: "jobmate",
                table: "worker_documents",
                column: "worker_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_profiles_availability",
                schema: "jobmate",
                table: "worker_profiles",
                columns: new[] { "is_available", "is_online" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_profiles_user_id",
                schema: "jobmate",
                table: "worker_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_worker_profiles_verification",
                schema: "jobmate",
                table: "worker_profiles",
                column: "verification_status");

            migrationBuilder.CreateIndex(
                name: "ix_worker_skills_name",
                schema: "jobmate",
                table: "worker_skills",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_worker_skills_profile_name",
                schema: "jobmate",
                table: "worker_skills",
                columns: new[] { "worker_profile_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_participants",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "disputes",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "giver_profiles",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "job_skills",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "job_weekdays",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "saved_jobs",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "support_reports",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "user_devices",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "verification_requests",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "worker_assignments",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "worker_availability_slots",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "worker_documents",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "worker_skills",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "job_applications",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "worker_profiles",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "jobmate");

            migrationBuilder.DropTable(
                name: "users",
                schema: "jobmate");
        }
    }
}
