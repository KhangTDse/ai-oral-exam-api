-- =====================================================================
-- AIVES - AI-powered Viva Exam System
-- 01_schema.sql : creates the tables for PostgreSQL (>= 13)
-- Based on the Conceptual ERD (Figure 8) + attributes from the Class Diagram (Figure 9)
--   15 ERD entities + 1 join table course_lecturers (N-N "teaches" relationship)
-- Enums are stored as VARCHAR + CHECK so they map easily to EF Core (string conversion).
-- =====================================================================

-- Re-runnable: drop old tables first (reverse dependency order)
DROP TABLE IF EXISTS score_reviews, ai_evaluations, interview_turns, question_responses,
    interview_attempts, participants, rubric_criteria, rubrics, questions, exam_sessions,
    course_lecturers, courses, ai_service_configs, audit_logs,
    email_link_tokens, user_accounts, users, roles CASCADE;

-- ---------------------------------------------------------------------
-- F7 - Access Control
-- ---------------------------------------------------------------------

-- ERD: ROLE  (Class diagram: enum UserRole)
CREATE TABLE roles (
    id          SMALLINT     PRIMARY KEY,
    name        VARCHAR(20)  NOT NULL UNIQUE
                CHECK (name IN ('Student', 'Lecturer', 'Administrator'))
);

-- ERD: USER  (profile data; credentials live in user_accounts)
CREATE TABLE users (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    full_name       VARCHAR(150) NOT NULL,
    role_id         SMALLINT     NOT NULL REFERENCES roles(id),
    student_code    VARCHAR(20)  UNIQUE,   -- Student only
    lecturer_code   VARCHAR(20)  UNIQUE,   -- Lecturer only
    is_active       BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_users_role_codes CHECK (
        (role_id = 1 AND student_code IS NOT NULL AND length(btrim(student_code)) > 0 AND lecturer_code IS NULL)
        OR (role_id = 2 AND student_code IS NULL AND lecturer_code IS NOT NULL AND length(btrim(lecturer_code)) > 0)
        OR (role_id = 3 AND student_code IS NULL AND lecturer_code IS NULL)
    )
);
CREATE INDEX ix_users_role ON users(role_id);

CREATE TABLE user_accounts (
    id              UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID          NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    account_name    VARCHAR(50),
    email           VARCHAR(255)  NOT NULL,
    email_verified  BOOLEAN       NOT NULL DEFAULT FALSE,
    password_hash   VARCHAR(512),
    password_salt   VARCHAR(128),
    google_linked   BOOLEAN       NOT NULL DEFAULT FALSE,
    CHECK (password_salt IS NULL OR password_hash IS NOT NULL)
);
CREATE UNIQUE INDEX ux_user_accounts_email ON user_accounts (lower(email));
CREATE UNIQUE INDEX ux_user_accounts_account_name ON user_accounts (lower(account_name));

CREATE TABLE email_link_tokens (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_account_id  UUID         NOT NULL REFERENCES user_accounts(id) ON DELETE CASCADE,
    token_hash       VARCHAR(128) NOT NULL UNIQUE,
    purpose          VARCHAR(20)  NOT NULL CHECK (purpose IN ('AccountSetup', 'PasswordReset')),
    expires_at       TIMESTAMPTZ  NOT NULL,
    used_at          TIMESTAMPTZ,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CHECK (expires_at > created_at)
);
CREATE INDEX ix_email_link_tokens_account ON email_link_tokens(user_account_id);

-- ERD: AUDIT LOG  (USER 1 --< 0..* "performs")
CREATE TABLE audit_logs (
    id             UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID         NOT NULL REFERENCES users(id),
    action         VARCHAR(100) NOT NULL,
    target_entity  VARCHAR(100) NOT NULL,
    target_id      UUID,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_user ON audit_logs(user_id);
CREATE INDEX ix_audit_created ON audit_logs(created_at DESC);

-- ERD: AI SERVICE CONFIG  (USER 1 --< 0..* "configures")
CREATE TABLE ai_service_configs (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    configured_by      UUID         NOT NULL REFERENCES users(id),
    service_type       VARCHAR(10)  NOT NULL CHECK (service_type IN ('STT', 'TTS', 'LLM')),
    provider_name      VARCHAR(100) NOT NULL,
    endpoint_url       VARCHAR(500) NOT NULL,
    api_key_encrypted  TEXT         NOT NULL,
    language           VARCHAR(20)  NOT NULL DEFAULT 'Vietnamese'
                       CHECK (language IN ('Vietnamese', 'English')),
    is_active          BOOLEAN      NOT NULL DEFAULT TRUE,
    updated_at         TIMESTAMPTZ  NOT NULL DEFAULT now()
);
-- Only one active configuration per service type
CREATE UNIQUE INDEX ux_ai_config_active ON ai_service_configs(service_type) WHERE is_active;

-- ---------------------------------------------------------------------
-- F7 - Exam Configuration
-- ---------------------------------------------------------------------

-- ERD: COURSE
CREATE TABLE courses (
    id    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    code  VARCHAR(20)  NOT NULL UNIQUE,
    name  VARCHAR(200) NOT NULL
);

-- ERD: COURSE >o--|< USER "teaches"  (N-N -> join table)
CREATE TABLE course_lecturers (
    course_id    UUID        NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    lecturer_id  UUID        NOT NULL REFERENCES users(id)   ON DELETE CASCADE,
    assigned_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (course_id, lecturer_id)
);
CREATE INDEX ix_course_lecturers_lecturer ON course_lecturers(lecturer_id);

-- ERD: EXAM SESSION  (COURSE 1 --< 0..*, USER 1 --< 0..* "creates")
CREATE TABLE exam_sessions (
    id                       UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    course_id                UUID         NOT NULL REFERENCES courses(id),
    created_by               UUID         NOT NULL REFERENCES users(id),
    title                    VARCHAR(200) NOT NULL,
    start_time               TIMESTAMPTZ  NOT NULL,
    end_time                 TIMESTAMPTZ  NOT NULL,
    time_limit_per_answer_seconds  INT    NOT NULL CHECK (time_limit_per_answer_seconds > 0),  -- seconds
    max_follow_ups           INT          NOT NULL DEFAULT 2 CHECK (max_follow_ups >= 0),
    status                   VARCHAR(20)  NOT NULL DEFAULT 'Draft'
                             CHECK (status IN ('Draft', 'Published', 'Closed')),
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CHECK (end_time > start_time)
);
CREATE INDEX ix_exam_sessions_course ON exam_sessions(course_id);

-- ERD: QUESTION  (EXAM SESSION 1 --< 1..*)
CREATE TABLE questions (
    id               UUID  PRIMARY KEY DEFAULT gen_random_uuid(),
    exam_session_id  UUID  NOT NULL REFERENCES exam_sessions(id) ON DELETE CASCADE,
    order_no         INT   NOT NULL CHECK (order_no > 0),
    content          TEXT  NOT NULL,
    UNIQUE (exam_session_id, order_no)
);

-- ERD: RUBRIC  (QUESTION 1 -- 1)
CREATE TABLE rubrics (
    id           UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    question_id  UUID          NOT NULL UNIQUE REFERENCES questions(id) ON DELETE CASCADE,
    max_score    NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (max_score >= 0)
);

CREATE TABLE rubric_criteria (
    id           UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    rubric_id    UUID          NOT NULL REFERENCES rubrics(id) ON DELETE CASCADE,
    order_no     INT           NOT NULL CHECK (order_no > 0),
    description  TEXT          NOT NULL CHECK (length(btrim(description)) > 0),
    max_points   NUMERIC(5,2)  NOT NULL CHECK (max_points >= 0),
    CONSTRAINT uq_rubric_criteria_order UNIQUE (rubric_id, order_no) DEFERRABLE INITIALLY DEFERRED
);

CREATE OR REPLACE FUNCTION update_rubric_max_score_from_criteria() RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' OR (TG_OP = 'UPDATE' AND OLD.rubric_id <> NEW.rubric_id) THEN
        UPDATE rubrics
        SET max_score = COALESCE((SELECT SUM(max_points) FROM rubric_criteria WHERE rubric_id = OLD.rubric_id), 0)
        WHERE id = OLD.rubric_id;
    END IF;

    IF TG_OP <> 'DELETE' THEN
        UPDATE rubrics
        SET max_score = COALESCE((SELECT SUM(max_points) FROM rubric_criteria WHERE rubric_id = NEW.rubric_id), 0)
        WHERE id = NEW.rubric_id;
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_rubric_max_score
AFTER INSERT OR UPDATE OR DELETE ON rubric_criteria
FOR EACH ROW EXECUTE FUNCTION update_rubric_max_score_from_criteria();

CREATE OR REPLACE FUNCTION assert_rubric_has_criteria(p_rubric_id UUID) RETURNS VOID AS $$
DECLARE
    criterion_count BIGINT;
    calculated_max_score NUMERIC;
    stored_max_score NUMERIC(5,2);
BEGIN
    SELECT max_score INTO stored_max_score FROM rubrics WHERE id = p_rubric_id;
    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT COUNT(*), COALESCE(SUM(max_points), 0)
    INTO criterion_count, calculated_max_score
    FROM rubric_criteria
    WHERE rubric_id = p_rubric_id;

    IF criterion_count = 0 THEN
        RAISE EXCEPTION 'Rubric % must have at least one criterion.', p_rubric_id
            USING ERRCODE = '23514';
    END IF;
    IF calculated_max_score <= 0 THEN
        RAISE EXCEPTION 'Rubric % must have a positive total max score.', p_rubric_id
            USING ERRCODE = '23514';
    END IF;
    IF stored_max_score IS DISTINCT FROM calculated_max_score THEN
        RAISE EXCEPTION 'Rubric % max_score does not match its criteria.', p_rubric_id
            USING ERRCODE = '23514';
    END IF;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION check_rubric_criteria_constraints() RETURNS TRIGGER AS $$
BEGIN
    IF TG_TABLE_NAME = 'rubrics' THEN
        IF TG_OP <> 'DELETE' THEN
            PERFORM assert_rubric_has_criteria(NEW.id);
        END IF;
    ELSE
        IF TG_OP <> 'INSERT' THEN
            PERFORM assert_rubric_has_criteria(OLD.rubric_id);
        END IF;
        IF TG_OP <> 'DELETE' THEN
            PERFORM assert_rubric_has_criteria(NEW.rubric_id);
        END IF;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE CONSTRAINT TRIGGER trg_rubrics_require_criteria
AFTER INSERT OR UPDATE OR DELETE ON rubrics
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION check_rubric_criteria_constraints();

CREATE CONSTRAINT TRIGGER trg_rubric_criteria_require_parent
AFTER INSERT OR UPDATE OR DELETE ON rubric_criteria
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION check_rubric_criteria_constraints();

-- ---------------------------------------------------------------------
-- F3 - AI Viva Interview
-- ---------------------------------------------------------------------

-- ERD: PARTICIPANT  (EXAM SESSION 1 --< 0..*, USER 1 --< 0..* "joins as")
CREATE TABLE participants (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    exam_session_id  UUID        NOT NULL REFERENCES exam_sessions(id) ON DELETE CASCADE,
    student_id       UUID        NOT NULL REFERENCES users(id),
    joined_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    status           VARCHAR(20) NOT NULL DEFAULT 'Registered'
                     CHECK (status IN ('Registered', 'InProgress', 'Completed')),
    UNIQUE (exam_session_id, student_id)
);
CREATE INDEX ix_participants_student ON participants(student_id);

-- ERD: INTERVIEW ATTEMPT  (PARTICIPANT 1 --< 0..* "makes")
CREATE TABLE interview_attempts (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    participant_id  UUID        NOT NULL REFERENCES participants(id) ON DELETE CASCADE,
    started_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at    TIMESTAMPTZ,
    status          VARCHAR(20) NOT NULL DEFAULT 'InProgress'
                    CHECK (status IN ('InProgress', 'PendingReview', 'Finalized'))
);
CREATE INDEX ix_attempts_participant ON interview_attempts(participant_id);

-- ERD: QUESTION RESPONSE  (ATTEMPT 1 --< 1..* "contains", QUESTION 1 --< 0..* "is answered in")
CREATE TABLE question_responses (
    id               UUID     PRIMARY KEY DEFAULT gen_random_uuid(),
    attempt_id       UUID     NOT NULL REFERENCES interview_attempts(id) ON DELETE CASCADE,
    question_id      UUID     NOT NULL REFERENCES questions(id),
    follow_up_count  INT      NOT NULL DEFAULT 0 CHECK (follow_up_count >= 0),
    is_finished      BOOLEAN  NOT NULL DEFAULT FALSE,
    UNIQUE (attempt_id, question_id)
);
CREATE INDEX ix_responses_question ON question_responses(question_id);

-- ERD: INTERVIEW TURN  (QUESTION RESPONSE 1 --< 1..* "consists of")
CREATE TABLE interview_turns (
    id                    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    question_response_id  UUID         NOT NULL REFERENCES question_responses(id) ON DELETE CASCADE,
    turn_no               INT          NOT NULL CHECK (turn_no > 0),
    type                  VARCHAR(10)  NOT NULL CHECK (type IN ('Main', 'FollowUp')),
    question_text         TEXT         NOT NULL,   -- exact text of the question asked (follow-ups too)
    answer_transcript     TEXT,                    -- NULL when time ran out without an answer
    audio_url             VARCHAR(500),            -- the audio file lives in object storage
    asked_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    answered_at           TIMESTAMPTZ,
    UNIQUE (question_response_id, turn_no)
);

-- ERD: AI EVALUATION  (QUESTION RESPONSE 1 -- 0..1 "receives")
CREATE TABLE ai_evaluations (
    id                    UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    question_response_id  UUID          NOT NULL UNIQUE REFERENCES question_responses(id) ON DELETE CASCADE,
    suggested_score       NUMERIC(5,2)  NOT NULL CHECK (suggested_score >= 0),
    feedback              TEXT          NOT NULL,
    evaluated_at          TIMESTAMPTZ   NOT NULL DEFAULT now()
);

-- ERD: SCORE REVIEW  (ATTEMPT 1 -- 0..1 "is finalized by", USER 1 --< 0..* "reviews")
CREATE TABLE score_reviews (
    id           UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    attempt_id   UUID          NOT NULL UNIQUE REFERENCES interview_attempts(id) ON DELETE CASCADE,
    reviewer_id  UUID          NOT NULL REFERENCES users(id),
    final_score  NUMERIC(5,2)  NOT NULL CHECK (final_score >= 0),
    comment      TEXT,
    status       VARCHAR(20)   NOT NULL DEFAULT 'Pending' CHECK (status IN ('Pending', 'Approved')),
    reviewed_at  TIMESTAMPTZ
);
CREATE INDEX ix_score_reviews_reviewer ON score_reviews(reviewer_id);
