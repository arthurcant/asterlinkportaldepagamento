USE asterlink_portal;

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
    CONSTRAINT fk_access_sessions_user FOREIGN KEY (user_id) REFERENCES users (id),
    CONSTRAINT fk_access_sessions_plan FOREIGN KEY (plan_id) REFERENCES internet_plans (id)
) ENGINE = InnoDB;
