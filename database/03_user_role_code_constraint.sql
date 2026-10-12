BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM users
        WHERE NOT (
            (role_id = 1 AND student_code IS NOT NULL AND length(btrim(student_code)) > 0 AND lecturer_code IS NULL)
            OR (role_id = 2 AND student_code IS NULL AND lecturer_code IS NOT NULL AND length(btrim(lecturer_code)) > 0)
            OR (role_id = 3 AND student_code IS NULL AND lecturer_code IS NULL)
        )
    ) THEN
        RAISE EXCEPTION 'Existing users violate the role/code requirements; correct those rows before adding ck_users_role_codes.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_users_role_codes'
          AND conrelid = 'users'::regclass
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT ck_users_role_codes CHECK (
                (role_id = 1 AND student_code IS NOT NULL AND length(btrim(student_code)) > 0 AND lecturer_code IS NULL)
                OR (role_id = 2 AND student_code IS NULL AND lecturer_code IS NOT NULL AND length(btrim(lecturer_code)) > 0)
                OR (role_id = 3 AND student_code IS NULL AND lecturer_code IS NULL)
            );
    END IF;
END;
$$;

COMMIT;
