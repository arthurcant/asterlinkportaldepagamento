CREATE DATABASE IF NOT EXISTS asterlink_portal CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE asterlink_portal;


CREATE TABLE IF NOT EXISTS users (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    name VARCHAR(120) NOT NULL,
    username VARCHAR(80) NOT NULL,
    email VARCHAR(190) NOT NULL,
    cpf CHAR(11) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY ux_users_username (username),
    UNIQUE KEY ux_users_email (email),
    UNIQUE KEY ux_users_cpf (cpf)
) ENGINE = InnoDB;


INSERT INTO users (name, username, email, password_hash, is_active)
VALUES (
    'Usuário de Teste',
    'teste',
    'teste@aster.local',
    'pbkdf2_sha256$210000$ZhueMYJGKgThKha3NZJgsg==$A/IvaiIsYO7rvZkABg7oPp2rtXP0rAVDZJLAvq3QjOg=',
    TRUE
)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    password_hash = VALUES(password_hash),
    is_active = TRUE;
    

USE asterlink_portal;

drop table internet_plans;

CREATE TABLE IF NOT EXISTS internet_plans (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name VARCHAR(80) NOT NULL,
    speed_mbps INT UNSIGNED NOT NULL,
    duration_minutes INT UNSIGNED NOT NULL,
    price DECIMAL(10, 2) NOT NULL,
    is_popular BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY ux_internet_plans_duration (duration_minutes),
    CONSTRAINT chk_internet_plans_price CHECK (price >= 0),
    CONSTRAINT chk_internet_plans_speed CHECK (speed_mbps > 0),
    CONSTRAINT chk_internet_plans_duration CHECK (duration_minutes > 0)
) ENGINE = InnoDB;

INSERT INTO
    internet_plans (
        name,
        speed_mbps,
        duration_minutes,
        price,
        is_popular,
        is_active,
        display_order
    )
VALUES
    ('Básico', 10, 30, 5.00, FALSE, TRUE, 1),
    ('Essencial', 20, 60, 10.00, FALSE, TRUE, 2),
    ('Plus', 30, 120, 15.00, FALSE, TRUE, 3),
    ('Premium', 50, 240, 20.00, TRUE, TRUE, 4)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    speed_mbps = VALUES(speed_mbps),
    price = VALUES(price),
    is_popular = VALUES(is_popular),
    is_active = VALUES(is_active),
    display_order = VALUES(display_order);


CREATE TABLE IF NOT EXISTS access_sessions (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id BIGINT UNSIGNED NOT NULL,
    plan_id INT UNSIGNED NOT NULL,
    mercado_pago_payment_id VARCHAR(80) NOT NULL,
    started_at DATETIME NOT NULL,
    expires_at DATETIME NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY ux_access_sessions_payment (mercado_pago_payment_id),
    KEY ix_access_sessions_user_expires (user_id, expires_at),
    CONSTRAINT fk_access_sessions_user FOREIGN KEY (user_id) REFERENCES users(id),
    CONSTRAINT fk_access_sessions_plan FOREIGN KEY (plan_id) REFERENCES internet_plans(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS network_orders (
 id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 user_id BIGINT UNSIGNED NOT NULL,
 payment_id VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NULL,
 document LONGTEXT NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE KEY ux_network_payment (payment_id),
 KEY ix_network_user (user_id),
 CONSTRAINT fk_network_user FOREIGN KEY (user_id) REFERENCES users(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS payment_notifications (
 payment_id VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 revision BIGINT NOT NULL DEFAULT 1,
 due_at DATETIME NOT NULL,
 KEY ix_notifications_due (due_at)
) ENGINE=InnoDB;


SET
    @cpf_column_exists = (
        SELECT
            COUNT(*)
        FROM
            information_schema.columns
        WHERE
            table_schema = DATABASE()
            AND table_name = 'users'
            AND column_name = 'cpf'
    );

SET
    @add_cpf_sql = IF(
        @cpf_column_exists = 0,
        'ALTER TABLE users ADD COLUMN cpf CHAR(11) NULL AFTER email',
        'SELECT 1'
    );

PREPARE add_cpf_statement
FROM
    @add_cpf_sql;

EXECUTE add_cpf_statement;

DEALLOCATE PREPARE add_cpf_statement;

SET
    @cpf_index_exists = (
        SELECT
            COUNT(*)
        FROM
            information_schema.statistics
        WHERE
            table_schema = DATABASE()
            AND table_name = 'users'
            AND index_name = 'ux_users_cpf'
    );

SET
    @add_cpf_index_sql = IF(
        @cpf_index_exists = 0,
        'ALTER TABLE users ADD UNIQUE KEY ux_users_cpf (cpf)',
        'SELECT 1'
    );

PREPARE add_cpf_index_statement
FROM
    @add_cpf_index_sql;

EXECUTE add_cpf_index_statement;

DEALLOCATE PREPARE add_cpf_index_statement;



-- Adiciona speed_mbps somente se a coluna ainda não existir.
SET @speed_column_exists = (
    SELECT COUNT(*)
    FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name = 'internet_plans'
      AND column_name = 'speed_mbps'
);

SET @add_speed_column_sql = IF(
    @speed_column_exists = 0,
    'ALTER TABLE internet_plans
        ADD COLUMN speed_mbps INT UNSIGNED NULL AFTER name',
    'SELECT 1'
);

PREPARE add_speed_column_statement
FROM @add_speed_column_sql;

EXECUTE add_speed_column_statement;

DEALLOCATE PREPARE add_speed_column_statement;

-- Desativa o catálogo anterior.
-- O WHERE utiliza a chave primária para funcionar com SQL_SAFE_UPDATES.
UPDATE internet_plans
SET is_active = FALSE
WHERE id > 0;

-- Insere ou atualiza os quatro planos.
INSERT INTO internet_plans (
    name,
    speed_mbps,
    duration_minutes,
    price,
    is_popular,
    is_active,
    display_order
)
VALUES
    ('Básico', 10, 30, 5.00, FALSE, TRUE, 1),
    ('Essencial', 20, 60, 10.00, FALSE, TRUE, 2),
    ('Plus', 30, 120, 15.00, FALSE, TRUE, 3),
    ('Premium', 50, 240, 20.00, TRUE, TRUE, 4)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    speed_mbps = VALUES(speed_mbps),
    price = VALUES(price),
    is_popular = VALUES(is_popular),
    is_active = VALUES(is_active),
    display_order = VALUES(display_order);

-- Preenche a velocidade de registros antigos que não foram atualizados.
UPDATE internet_plans
SET speed_mbps = 1
WHERE id > 0
  AND speed_mbps IS NULL;

-- Depois de preencher todos os registros, torna a coluna obrigatória.
ALTER TABLE internet_plans
MODIFY COLUMN speed_mbps INT UNSIGNED NOT NULL;

-- Confirma o resultado.
SELECT
    id,
    name,
    speed_mbps,
    duration_minutes,
    price,
    is_popular,
    is_active,
    display_order
FROM internet_plans
ORDER BY display_order, id;


