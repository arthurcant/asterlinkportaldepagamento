USE asterlink_portal;

CREATE TABLE IF NOT EXISTS internet_plans (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name VARCHAR(80) NOT NULL,
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
    CONSTRAINT chk_internet_plans_duration CHECK (duration_minutes > 0)
) ENGINE = InnoDB;

INSERT INTO
    internet_plans (
        name,
        duration_minutes,
        price,
        is_popular,
        is_active,
        display_order
    )
VALUES
    ('1 Hora', 60, 5.00, FALSE, TRUE, 1),
    ('2 Horas', 120, 10.00, TRUE, TRUE, 2),
    ('3 Horas', 180, 15.00, FALSE, TRUE, 3)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    price = VALUES(price),
    is_popular = VALUES(is_popular),
    is_active = VALUES(is_active),
    display_order = VALUES(display_order);
