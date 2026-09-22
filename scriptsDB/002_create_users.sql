USE asterlink_portal;

CREATE TABLE IF NOT EXISTS users (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    name VARCHAR(120) NOT NULL,
    username VARCHAR(80) NOT NULL,
    email VARCHAR(190) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY ux_users_username (username),
    UNIQUE KEY ux_users_email (email)
) ENGINE = InnoDB;

-- Compatibilidade com bancos onde a tabela foi criada antes do campo username.
SET
    @username_column_exists = (
        SELECT
            COUNT(*)
        FROM
            information_schema.columns
        WHERE
            table_schema = DATABASE()
            AND table_name = 'users'
            AND column_name = 'username'
    );

SET
    @add_username_sql = IF(
        @username_column_exists = 0,
        'ALTER TABLE users ADD COLUMN username VARCHAR(80) NULL AFTER name',
        'SELECT 1'
    );

PREPARE add_username_statement
FROM
    @add_username_sql;

EXECUTE add_username_statement;

DEALLOCATE PREPARE add_username_statement;

UPDATE users
SET
    username = CONCAT('usuario_', id)
WHERE
    username IS NULL
    OR username = '';

ALTER TABLE users
MODIFY COLUMN username VARCHAR(80) NOT NULL;

SET
    @username_index_exists = (
        SELECT
            COUNT(*)
        FROM
            information_schema.statistics
        WHERE
            table_schema = DATABASE()
            AND table_name = 'users'
            AND index_name = 'ux_users_username'
    );

SET
    @add_username_index_sql = IF(
        @username_index_exists = 0,
        'ALTER TABLE users ADD UNIQUE KEY ux_users_username (username)',
        'SELECT 1'
    );

PREPARE add_username_index_statement
FROM
    @add_username_index_sql;

EXECUTE add_username_index_statement;

DEALLOCATE PREPARE add_username_index_statement;
