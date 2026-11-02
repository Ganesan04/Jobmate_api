DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'jobmate') THEN
        CREATE SCHEMA jobmate;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS jobmate.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'jobmate') THEN
        CREATE SCHEMA jobmate;
    END IF;
END $EF$;

CREATE TABLE jobmate.otp_challenges (
    otp_challenge_id uuid NOT NULL,
    phone_number character varying(20) NOT NULL,
    code_hash character varying(128) NOT NULL,
    purpose character varying(30) NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    attempt_count integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 5,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_otp_challenges PRIMARY KEY (otp_challenge_id),
    CONSTRAINT chk_otp_challenges_purpose CHECK ("purpose" IN ('PhoneVerification', 'PasswordReset', 'EmailVerification'))
);

CREATE TABLE jobmate.users (
    user_id uuid NOT NULL,
    first_name character varying(60) NOT NULL,
    last_name character varying(60) NOT NULL,
    email character varying(254),
    phone_number character varying(20) NOT NULL,
    password_hash character varying(255) NOT NULL,
    role character varying(20) NOT NULL,
    profile_image_url character varying(500),
    is_active boolean NOT NULL DEFAULT TRUE,
    is_verified boolean NOT NULL DEFAULT FALSE,
    last_login_at timestamptz,
    verification_status character varying(20) NOT NULL,
    area character varying(120) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (user_id),
    CONSTRAINT chk_users_role CHECK ("role" IN ('Worker', 'WorkGiver', 'Admin')),
    CONSTRAINT chk_users_verification_status CHECK ("verification_status" IN ('NotVerified', 'Pending', 'Verified', 'Rejected'))
);

CREATE TABLE jobmate.giver_profiles (
    giver_profile_id uuid NOT NULL,
    user_id uuid NOT NULL,
    kind character varying(20) NOT NULL,
    business_name character varying(150) NOT NULL,
    business_type character varying(100),
    description character varying(2000),
    location character varying(200),
    latitude double precision,
    longitude double precision,
    contact_person character varying(120),
    contact_phone character varying(20),
    gst_number character varying(20),
    verification_status character varying(20) NOT NULL,
    average_rating numeric(3,2) NOT NULL DEFAULT 0.0,
    rating_count integer NOT NULL DEFAULT 0,
    total_jobs_posted integer NOT NULL DEFAULT 0,
    total_hired integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_giver_profiles PRIMARY KEY (giver_profile_id),
    CONSTRAINT chk_giver_profiles_kind CHECK ("kind" IN ('Individual', 'Business')),
    CONSTRAINT chk_giver_profiles_verification_status CHECK ("verification_status" IN ('NotVerified', 'Pending', 'Verified', 'Rejected')),
    CONSTRAINT fk_giver_profiles_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.jobs (
    job_id uuid NOT NULL,
    giver_user_id uuid NOT NULL,
    company character varying(150) NOT NULL,
    title character varying(150) NOT NULL,
    category character varying(30) NOT NULL,
    description character varying(4000) NOT NULL,
    workers_needed integer NOT NULL DEFAULT 1,
    workers_filled integer NOT NULL DEFAULT 0,
    experience character varying(500),
    job_date date NOT NULL,
    start_time time NOT NULL,
    end_time time NOT NULL,
    report_time time NOT NULL,
    break_minutes integer NOT NULL DEFAULT 30,
    job_type character varying(20) NOT NULL,
    is_recurring boolean NOT NULL DEFAULT FALSE,
    weekdays integer[] NOT NULL,
    pay integer NOT NULL,
    pay_type character varying(20) NOT NULL,
    overtime_rate integer,
    travel_allowance integer NOT NULL DEFAULT 0,
    food_allowance integer NOT NULL DEFAULT 0,
    payment_date date,
    payment_method character varying(20) NOT NULL,
    area character varying(120) NOT NULL,
    address character varying(400) NOT NULL,
    latitude double precision,
    longitude double precision,
    workplace_type character varying(20) NOT NULL,
    location_visible boolean NOT NULL DEFAULT TRUE,
    contact_person character varying(120),
    safety_information character varying(1000),
    physical_requirements character varying(500),
    dress_code character varying(200),
    vehicle_required boolean NOT NULL DEFAULT FALSE,
    emergency_contact character varying(200),
    is_urgent boolean NOT NULL DEFAULT FALSE,
    status character varying(20) NOT NULL,
    application_deadline date,
    published_at timestamptz,
    cancelled_at timestamptz,
    cancellation_reason character varying(500),
    completed_at timestamptz,
    started_at timestamptz,
    deleted_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_jobs PRIMARY KEY (job_id),
    CONSTRAINT chk_jobs_category CHECK ("category" IN ('Retail', 'Events', 'Delivery', 'Cleaning', 'Security', 'Office', 'Hospitality', 'Construction', 'Teaching', 'Healthcare', 'ItSupport', 'Other')),
    CONSTRAINT chk_jobs_job_type CHECK ("job_type" IN ('OneTime', 'Recurring')),
    CONSTRAINT chk_jobs_pay_type CHECK ("pay_type" IN ('Fixed', 'Hourly', 'Daily', 'PerPiece')),
    CONSTRAINT chk_jobs_payment_method CHECK ("payment_method" IN ('Upi', 'Bank', 'Cash')),
    CONSTRAINT chk_jobs_status CHECK ("status" IN ('Draft', 'Pending', 'Active', 'InProgress', 'Completed', 'Cancelled', 'Expired')),
    CONSTRAINT chk_jobs_workers_filled CHECK ("workers_filled" >= 0),
    CONSTRAINT chk_jobs_workers_needed CHECK ("workers_needed" > 0),
    CONSTRAINT chk_jobs_workplace_type CHECK ("workplace_type" IN ('OnSite', 'Remote', 'Hybrid')),
    CONSTRAINT fk_jobs_giver_user_id FOREIGN KEY (giver_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.notifications (
    notification_id uuid NOT NULL,
    user_id uuid NOT NULL,
    type character varying(30) NOT NULL,
    title character varying(150) NOT NULL,
    message character varying(500) NOT NULL,
    reference_type character varying(30) NOT NULL,
    reference_id uuid,
    route character varying(200),
    is_read boolean NOT NULL DEFAULT FALSE,
    read_at timestamptz,
    push_dispatched_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_notifications PRIMARY KEY (notification_id),
    CONSTRAINT chk_notifications_reference_type CHECK ("reference_type" IN ('Job', 'Application', 'Assignment', 'Payment', 'Conversation', 'Dispute', 'Verification', 'None')),
    CONSTRAINT chk_notifications_type CHECK ("type" IN ('ApplicationUpdate', 'JobAccepted', 'JobShortlisted', 'Payment', 'JobReminder', 'NewApplicant', 'System')),
    CONSTRAINT fk_notifications_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.password_reset_tokens (
    password_reset_token_id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_password_reset_tokens PRIMARY KEY (password_reset_token_id),
    CONSTRAINT fk_password_reset_tokens_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.refresh_tokens (
    refresh_token_id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    revoked_reason character varying(120),
    replaced_by_refresh_token_id uuid,
    created_by_ip character varying(45),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_refresh_tokens PRIMARY KEY (refresh_token_id),
    CONSTRAINT fk_refresh_tokens_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.support_reports (
    support_report_id uuid NOT NULL,
    user_id uuid NOT NULL,
    reason character varying(30) NOT NULL,
    details character varying(2000) NOT NULL,
    related_job_id text,
    is_resolved boolean NOT NULL DEFAULT FALSE,
    resolved_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_support_reports PRIMARY KEY (support_report_id),
    CONSTRAINT chk_support_reports_reason CHECK ("reason" IN ('UnsafeWorkplace', 'NotPaid', 'AbusiveBehaviour', 'FakeJob', 'NoShow', 'Other')),
    CONSTRAINT fk_support_reports_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.user_devices (
    user_device_id uuid NOT NULL,
    user_id uuid NOT NULL,
    token character varying(512) NOT NULL,
    platform character varying(20) NOT NULL,
    device_name character varying(120),
    app_version character varying(30),
    is_active boolean NOT NULL DEFAULT TRUE,
    last_seen_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_user_devices PRIMARY KEY (user_device_id),
    CONSTRAINT chk_user_devices_platform CHECK ("platform" IN ('Android', 'Ios', 'Web')),
    CONSTRAINT fk_user_devices_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.verification_requests (
    verification_request_id uuid NOT NULL,
    user_id uuid NOT NULL,
    type character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    document_name character varying(255),
    document_url character varying(500),
    note character varying(500),
    rejection_reason character varying(500),
    reviewed_at timestamptz,
    reviewed_by_user_id uuid,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_verification_requests PRIMARY KEY (verification_request_id),
    CONSTRAINT chk_verification_requests_status CHECK ("status" IN ('Pending', 'Approved', 'Rejected', 'Withdrawn')),
    CONSTRAINT chk_verification_requests_type CHECK ("type" IN ('Identity', 'Phone', 'Email', 'Skills', 'Address')),
    CONSTRAINT fk_verification_requests_reviewed_by_user_id FOREIGN KEY (reviewed_by_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_verification_requests_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.worker_profiles (
    worker_profile_id uuid NOT NULL,
    user_id uuid NOT NULL,
    bio character varying(2000),
    date_of_birth date,
    gender character varying(40),
    location character varying(200),
    latitude double precision,
    longitude double precision,
    experience character varying(1000),
    availability character varying(200),
    expected_pay numeric(12,2),
    expected_pay_type character varying(20),
    verification_status character varying(20) NOT NULL,
    average_rating numeric(3,2) NOT NULL DEFAULT 0.0,
    rating_count integer NOT NULL DEFAULT 0,
    total_completed_jobs integer NOT NULL DEFAULT 0,
    total_applications integer NOT NULL DEFAULT 0,
    is_available boolean NOT NULL DEFAULT TRUE,
    is_online boolean NOT NULL DEFAULT FALSE,
    last_active_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_worker_profiles PRIMARY KEY (worker_profile_id),
    CONSTRAINT chk_worker_profiles_expected_pay_type CHECK ("expected_pay_type" IN ('Fixed', 'Hourly', 'Daily', 'PerPiece')),
    CONSTRAINT chk_worker_profiles_verification_status CHECK ("verification_status" IN ('NotVerified', 'Pending', 'Verified', 'Rejected')),
    CONSTRAINT fk_worker_profiles_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.conversations (
    conversation_id uuid NOT NULL,
    job_id uuid,
    last_message_at timestamptz,
    last_message_preview character varying(200),
    last_message_sender_user_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_conversations PRIMARY KEY (conversation_id),
    CONSTRAINT fk_conversations_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.disputes (
    dispute_id uuid NOT NULL,
    job_id uuid NOT NULL,
    raised_by_user_id uuid NOT NULL,
    against_user_id uuid NOT NULL,
    reason character varying(30) NOT NULL,
    description character varying(2000) NOT NULL,
    status character varying(20) NOT NULL,
    amount integer NOT NULL DEFAULT 0,
    resolution character varying(1000),
    resolved_by_user_id uuid,
    resolved_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_disputes PRIMARY KEY (dispute_id),
    CONSTRAINT chk_disputes_reason CHECK ("reason" IN ('UnsafeWorkplace', 'NotPaid', 'AbusiveBehaviour', 'FakeJob', 'NoShow', 'Other')),
    CONSTRAINT chk_disputes_status CHECK ("status" IN ('Open', 'UnderReview', 'Resolved', 'Rejected')),
    CONSTRAINT fk_disputes_against_user_id FOREIGN KEY (against_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_disputes_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE,
    CONSTRAINT fk_disputes_raised_by_user_id FOREIGN KEY (raised_by_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_disputes_resolved_by_user_id FOREIGN KEY (resolved_by_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.job_applications (
    job_application_id uuid NOT NULL,
    job_id uuid NOT NULL,
    worker_user_id uuid NOT NULL,
    message character varying(1000) NOT NULL,
    expected_pay integer,
    available_from character varying(120) NOT NULL,
    status character varying(20) NOT NULL,
    applied_at timestamptz NOT NULL,
    viewed_at timestamptz,
    shortlisted_at timestamptz,
    rejected_at timestamptz,
    withdrawn_at timestamptz,
    accepted_at timestamptz,
    rejection_reason character varying(500),
    proposed_pay integer,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_job_applications PRIMARY KEY (job_application_id),
    CONSTRAINT chk_job_applications_status CHECK ("status" IN ('Applied', 'Viewed', 'Shortlisted', 'Accepted', 'Rejected', 'Withdrawn', 'Expired')),
    CONSTRAINT fk_job_applications_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE,
    CONSTRAINT fk_job_applications_worker_user_id FOREIGN KEY (worker_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.job_skills (
    job_skill_id uuid NOT NULL,
    job_id uuid NOT NULL,
    name character varying(60) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_job_skills PRIMARY KEY (job_skill_id),
    CONSTRAINT fk_job_skills_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.job_weekdays (
    job_weekday_id uuid NOT NULL,
    job_id uuid NOT NULL,
    day_of_week integer NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_job_weekdays PRIMARY KEY (job_weekday_id),
    CONSTRAINT chk_job_weekdays_day_of_week CHECK ("day_of_week" BETWEEN 1 AND 7),
    CONSTRAINT fk_job_weekdays_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.payments (
    payment_id uuid NOT NULL,
    job_id uuid NOT NULL,
    worker_user_id uuid NOT NULL,
    giver_user_id uuid NOT NULL,
    worker_assignment_id uuid NOT NULL,
    amount integer NOT NULL,
    overtime_amount integer NOT NULL DEFAULT 0,
    platform_fee integer NOT NULL DEFAULT 0,
    total_amount integer NOT NULL,
    currency character varying(3) NOT NULL DEFAULT 'INR',
    payment_method character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    transaction_reference character varying(120),
    idempotency_key character varying(120) NOT NULL,
    failure_reason character varying(500),
    paid_at timestamptz,
    refunded_at timestamptz,
    agreed_pay_snapshot integer NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_payments PRIMARY KEY (payment_id),
    CONSTRAINT chk_payments_payment_method CHECK ("payment_method" IN ('Upi', 'Bank', 'Cash')),
    CONSTRAINT chk_payments_status CHECK ("status" IN ('Pending', 'Processing', 'Paid', 'Disputed', 'Failed', 'Refunded', 'Cancelled')),
    CONSTRAINT chk_payments_total_amount CHECK ("total_amount" = "amount" + "overtime_amount" + "platform_fee"),
    CONSTRAINT fk_payments_giver_user_id FOREIGN KEY (giver_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_payments_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE RESTRICT,
    CONSTRAINT fk_payments_worker_user_id FOREIGN KEY (worker_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.ratings (
    rating_id uuid NOT NULL,
    job_id uuid NOT NULL,
    from_user_id uuid NOT NULL,
    to_user_id uuid NOT NULL,
    stars integer NOT NULL,
    comment character varying(1000) NOT NULL,
    worker_assignment_id uuid,
    rating_direction character varying(20) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_ratings PRIMARY KEY (rating_id),
    CONSTRAINT chk_ratings_rating_direction CHECK ("rating_direction" IN ('GiverToWorker', 'WorkerToGiver')),
    CONSTRAINT chk_ratings_stars CHECK ("stars" BETWEEN 1 AND 5),
    CONSTRAINT fk_ratings_from_user_id FOREIGN KEY (from_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_ratings_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE,
    CONSTRAINT fk_ratings_to_user_id FOREIGN KEY (to_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.saved_jobs (
    saved_job_id uuid NOT NULL,
    user_id uuid NOT NULL,
    job_id uuid NOT NULL,
    deleted_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_saved_jobs PRIMARY KEY (saved_job_id),
    CONSTRAINT fk_saved_jobs_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE,
    CONSTRAINT fk_saved_jobs_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.worker_availability_slots (
    worker_availability_slot_id uuid NOT NULL,
    worker_profile_id uuid NOT NULL,
    day_of_week integer NOT NULL,
    start_time time NOT NULL,
    end_time time NOT NULL,
    is_available boolean NOT NULL DEFAULT TRUE,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_worker_availability_slots PRIMARY KEY (worker_availability_slot_id),
    CONSTRAINT chk_worker_availability_slots_day_of_week CHECK ("day_of_week" BETWEEN 1 AND 7),
    CONSTRAINT fk_worker_availability_slots_worker_profile_id FOREIGN KEY (worker_profile_id) REFERENCES jobmate.worker_profiles (worker_profile_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.worker_documents (
    worker_document_id uuid NOT NULL,
    worker_profile_id uuid NOT NULL,
    type character varying(20) NOT NULL,
    file_name character varying(255) NOT NULL,
    url character varying(500),
    status character varying(20) NOT NULL,
    reviewed_at timestamptz,
    reviewed_by_user_id uuid,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_worker_documents PRIMARY KEY (worker_document_id),
    CONSTRAINT chk_worker_documents_status CHECK ("status" IN ('NotVerified', 'Pending', 'Verified', 'Rejected')),
    CONSTRAINT chk_worker_documents_type CHECK ("type" IN ('Identity', 'Phone', 'Email', 'Skills', 'Address')),
    CONSTRAINT fk_worker_documents_worker_profile_id FOREIGN KEY (worker_profile_id) REFERENCES jobmate.worker_profiles (worker_profile_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.worker_skills (
    worker_skill_id uuid NOT NULL,
    worker_profile_id uuid NOT NULL,
    name character varying(60) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_worker_skills PRIMARY KEY (worker_skill_id),
    CONSTRAINT fk_worker_skills_worker_profile_id FOREIGN KEY (worker_profile_id) REFERENCES jobmate.worker_profiles (worker_profile_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.conversation_participants (
    conversation_participant_id uuid NOT NULL,
    conversation_id uuid NOT NULL,
    user_id uuid NOT NULL,
    unread_count integer NOT NULL DEFAULT 0,
    last_read_at timestamptz,
    is_archived boolean NOT NULL DEFAULT FALSE,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_conversation_participants PRIMARY KEY (conversation_participant_id),
    CONSTRAINT fk_conversation_participants_conversation_id FOREIGN KEY (conversation_id) REFERENCES jobmate.conversations (conversation_id) ON DELETE CASCADE,
    CONSTRAINT fk_conversation_participants_user_id FOREIGN KEY (user_id) REFERENCES jobmate.users (user_id) ON DELETE CASCADE
);

CREATE TABLE jobmate.messages (
    message_id uuid NOT NULL,
    conversation_id uuid NOT NULL,
    sender_user_id uuid NOT NULL,
    message_text character varying(4000) NOT NULL,
    message_type character varying(20) NOT NULL,
    is_read boolean NOT NULL DEFAULT FALSE,
    sent_at timestamptz NOT NULL,
    read_at timestamptz,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_messages PRIMARY KEY (message_id),
    CONSTRAINT chk_messages_message_type CHECK ("message_type" IN ('Text', 'System')),
    CONSTRAINT fk_messages_conversation_id FOREIGN KEY (conversation_id) REFERENCES jobmate.conversations (conversation_id) ON DELETE CASCADE,
    CONSTRAINT fk_messages_sender_user_id FOREIGN KEY (sender_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE TABLE jobmate.worker_assignments (
    worker_assignment_id uuid NOT NULL,
    job_id uuid NOT NULL,
    worker_user_id uuid NOT NULL,
    giver_user_id uuid NOT NULL,
    job_application_id uuid NOT NULL,
    agreed_pay integer NOT NULL,
    overtime_amount integer,
    status character varying(20) NOT NULL,
    check_in_code character varying(20),
    confirmed_at timestamptz NOT NULL,
    checked_in_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    paid_at timestamptz,
    payment_id uuid,
    row_version bigint NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT pk_worker_assignments PRIMARY KEY (worker_assignment_id),
    CONSTRAINT chk_worker_assignments_status CHECK ("status" IN ('Pending', 'Active', 'CheckedIn', 'Completed', 'Cancelled')),
    CONSTRAINT fk_worker_assignments_giver_user_id FOREIGN KEY (giver_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_worker_assignments_job_application_id FOREIGN KEY (job_application_id) REFERENCES jobmate.job_applications (job_application_id) ON DELETE RESTRICT,
    CONSTRAINT fk_worker_assignments_job_id FOREIGN KEY (job_id) REFERENCES jobmate.jobs (job_id) ON DELETE CASCADE,
    CONSTRAINT fk_worker_assignments_payment_id FOREIGN KEY (payment_id) REFERENCES jobmate.payments (payment_id) ON DELETE RESTRICT,
    CONSTRAINT fk_worker_assignments_worker_user_id FOREIGN KEY (worker_user_id) REFERENCES jobmate.users (user_id) ON DELETE RESTRICT
);

CREATE INDEX ix_conversation_participants_user_unread ON jobmate.conversation_participants (user_id, is_archived, unread_count);

CREATE UNIQUE INDEX ux_conversation_participants_conversation_user ON jobmate.conversation_participants (conversation_id, user_id);

CREATE INDEX ix_conversations_job_id ON jobmate.conversations (job_id);

CREATE INDEX ix_disputes_against_user_id ON jobmate.disputes (against_user_id);

CREATE INDEX ix_disputes_job_status ON jobmate.disputes (job_id, status);

CREATE INDEX ix_disputes_raised_by_user_id ON jobmate.disputes (raised_by_user_id);

CREATE INDEX ix_disputes_resolved_by_user_id ON jobmate.disputes (resolved_by_user_id);

CREATE INDEX ix_disputes_status_created ON jobmate.disputes (status, created_at);

CREATE INDEX ix_giver_profiles_verification ON jobmate.giver_profiles (verification_status);

CREATE UNIQUE INDEX ux_giver_profiles_user_id ON jobmate.giver_profiles (user_id);

CREATE INDEX ix_job_applications_job_status ON jobmate.job_applications (job_id, status);

CREATE INDEX ix_job_applications_worker_status ON jobmate.job_applications (worker_user_id, status);

CREATE UNIQUE INDEX ux_job_applications_job_worker ON jobmate.job_applications (job_id, worker_user_id);

CREATE INDEX ix_job_skills_name ON jobmate.job_skills (name);

CREATE UNIQUE INDEX ux_job_skills_job_name ON jobmate.job_skills (job_id, name);

CREATE UNIQUE INDEX ux_job_weekdays_job_day ON jobmate.job_weekdays (job_id, day_of_week);

CREATE INDEX ix_jobs_area ON jobmate.jobs (area);

CREATE INDEX ix_jobs_category_status_date ON jobmate.jobs (category, status, job_date);

CREATE INDEX ix_jobs_giver_status ON jobmate.jobs (giver_user_id, status);

CREATE INDEX ix_jobs_status_date ON jobmate.jobs (status, job_date);

CREATE INDEX ix_messages_conversation_sent ON jobmate.messages (conversation_id, sent_at DESC);

CREATE INDEX ix_messages_sender_user_id ON jobmate.messages (sender_user_id);

CREATE INDEX ix_notifications_user_read_created ON jobmate.notifications (user_id, is_read, created_at DESC);

CREATE INDEX ix_otp_challenges_expires ON jobmate.otp_challenges (expires_at);

CREATE INDEX ix_otp_challenges_phone_purpose_created ON jobmate.otp_challenges (phone_number, purpose, created_at);

CREATE INDEX ix_password_reset_tokens_user_id ON jobmate.password_reset_tokens (user_id);

CREATE UNIQUE INDEX ux_password_reset_tokens_token_hash ON jobmate.password_reset_tokens (token_hash);

CREATE INDEX ix_payments_giver_status_created ON jobmate.payments (giver_user_id, status, created_at);

CREATE INDEX ix_payments_job_id ON jobmate.payments (job_id);

CREATE INDEX ix_payments_worker_status_created ON jobmate.payments (worker_user_id, status, created_at);

CREATE UNIQUE INDEX ux_payments_assignment ON jobmate.payments (worker_assignment_id);

CREATE UNIQUE INDEX ux_payments_giver_idempotency ON jobmate.payments (giver_user_id, idempotency_key);

CREATE INDEX ix_ratings_from_user_id ON jobmate.ratings (from_user_id);

CREATE INDEX ix_ratings_to_user_created ON jobmate.ratings (to_user_id, created_at);

CREATE UNIQUE INDEX ux_ratings_job_from_to ON jobmate.ratings (job_id, from_user_id, to_user_id);

CREATE INDEX ix_refresh_tokens_user_revoked ON jobmate.refresh_tokens (user_id, revoked_at);

CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON jobmate.refresh_tokens (token_hash);

CREATE INDEX ix_saved_jobs_job_id ON jobmate.saved_jobs (job_id);

CREATE UNIQUE INDEX ux_saved_jobs_user_job ON jobmate.saved_jobs (user_id, job_id) WHERE deleted_at IS NULL;

CREATE INDEX ix_support_reports_resolved_created ON jobmate.support_reports (is_resolved, created_at);

CREATE INDEX ix_support_reports_user_id ON jobmate.support_reports (user_id);

CREATE INDEX ix_user_devices_user_active ON jobmate.user_devices (user_id, is_active);

CREATE UNIQUE INDEX ux_user_devices_token ON jobmate.user_devices (token);

CREATE INDEX ix_users_area ON jobmate.users (area);

CREATE INDEX ix_users_role_active ON jobmate.users (role, is_active);

CREATE UNIQUE INDEX ux_users_email ON jobmate.users (email) WHERE email IS NOT NULL;

CREATE UNIQUE INDEX ux_users_phone_number ON jobmate.users (phone_number);

CREATE INDEX ix_verification_requests_reviewed_by_user_id ON jobmate.verification_requests (reviewed_by_user_id);

CREATE INDEX ix_verification_requests_status_created ON jobmate.verification_requests (status, created_at);

CREATE INDEX ix_verification_requests_user_type_status ON jobmate.verification_requests (user_id, type, status);

CREATE INDEX ix_worker_assignments_giver_user_id ON jobmate.worker_assignments (giver_user_id);

CREATE INDEX ix_worker_assignments_job_status ON jobmate.worker_assignments (job_id, status);

CREATE INDEX ix_worker_assignments_worker_status ON jobmate.worker_assignments (worker_user_id, status);

CREATE UNIQUE INDEX ux_worker_assignments_application ON jobmate.worker_assignments (job_application_id);

CREATE UNIQUE INDEX ux_worker_assignments_payment_id ON jobmate.worker_assignments (payment_id);

CREATE INDEX ix_worker_availability_slots_profile_day ON jobmate.worker_availability_slots (worker_profile_id, day_of_week);

CREATE INDEX ix_worker_documents_status ON jobmate.worker_documents (status);

CREATE INDEX ix_worker_documents_worker_profile_id ON jobmate.worker_documents (worker_profile_id);

CREATE INDEX ix_worker_profiles_availability ON jobmate.worker_profiles (is_available, is_online);

CREATE INDEX ix_worker_profiles_verification ON jobmate.worker_profiles (verification_status);

CREATE UNIQUE INDEX ux_worker_profiles_user_id ON jobmate.worker_profiles (user_id);

CREATE INDEX ix_worker_skills_name ON jobmate.worker_skills (name);

CREATE UNIQUE INDEX ux_worker_skills_profile_name ON jobmate.worker_skills (worker_profile_id, name);

INSERT INTO jobmate.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20260929174703_InitialSchema', '9.0.4');

DROP TABLE jobmate.disputes;

DROP INDEX jobmate.ix_worker_profiles_verification;

ALTER TABLE jobmate.worker_profiles DROP CONSTRAINT chk_worker_profiles_verification_status;

DROP INDEX jobmate.ix_verification_requests_status_created;

DROP INDEX jobmate.ix_users_role_active;

ALTER TABLE jobmate.users DROP CONSTRAINT chk_users_role;

ALTER TABLE jobmate.payments DROP CONSTRAINT chk_payments_status;

ALTER TABLE jobmate.notifications DROP CONSTRAINT chk_notifications_reference_type;

DROP INDEX jobmate.ix_giver_profiles_verification;

ALTER TABLE jobmate.giver_profiles DROP CONSTRAINT chk_giver_profiles_verification_status;

ALTER TABLE jobmate.worker_profiles DROP COLUMN verification_status;

ALTER TABLE jobmate.giver_profiles DROP COLUMN verification_status;

ALTER TABLE jobmate.users RENAME COLUMN role TO active_role;

UPDATE jobmate.verification_requests SET reviewed_by_user_id = NULL
WHERE reviewed_by_user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

UPDATE jobmate.worker_documents SET reviewed_by_user_id = NULL
WHERE reviewed_by_user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

DELETE FROM jobmate.refresh_tokens
WHERE user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

DELETE FROM jobmate.user_devices
WHERE user_id IN (SELECT user_id FROM jobmate.users WHERE active_role = 'Admin');

DELETE FROM jobmate.users WHERE active_role = 'Admin';

UPDATE jobmate.payments SET status = 'Paid', updated_at = now()
WHERE status = 'Disputed';

UPDATE jobmate.notifications
SET reference_type = 'None', reference_id = NULL, route = NULL, updated_at = now()
WHERE reference_type = 'Dispute';

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

ALTER TABLE jobmate.users ADD CONSTRAINT chk_users_active_role CHECK ("active_role" IN ('Worker', 'WorkGiver'));

ALTER TABLE jobmate.payments ADD CONSTRAINT chk_payments_status CHECK ("status" IN ('Pending', 'Processing', 'Paid', 'Failed', 'Refunded', 'Cancelled'));

ALTER TABLE jobmate.notifications ADD CONSTRAINT chk_notifications_reference_type CHECK ("reference_type" IN ('Job', 'Application', 'Assignment', 'Payment', 'Conversation', 'Verification', 'None'));

INSERT INTO jobmate.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261102152755_DualRoleWithoutAdmin', '9.0.4');

COMMIT;

