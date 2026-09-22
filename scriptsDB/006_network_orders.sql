-- Apply manually to the selected development database, after scripts 001-005.
-- Additive migration; does not alter existing access_sessions or plans.
CREATE TABLE IF NOT EXISTS network_orders (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    user_id BIGINT UNSIGNED NOT NULL,
    payment_id VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NULL,
    document LONGTEXT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY ux_network_payment (payment_id),
    KEY ix_network_user (user_id),
    CONSTRAINT fk_network_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS payment_notifications (
    payment_id VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    revision BIGINT NOT NULL DEFAULT 1,
    due_at DATETIME NOT NULL,
    KEY ix_notifications_due (due_at)
) ENGINE = InnoDB;
