USE asterlink_portal;

-- A velocidade é somente um dado comercial exibido pelo portal.
-- Este script não altera perfis nem limites do MikroTik RouterOS.
SET
    @speed_column_exists = (
        SELECT
            COUNT(*)
        FROM
            information_schema.columns
        WHERE
            table_schema = DATABASE()
            AND table_name = 'internet_plans'
            AND column_name = 'speed_mbps'
    );

SET
    @add_speed_sql = IF(
        @speed_column_exists = 0,
        'ALTER TABLE internet_plans ADD COLUMN speed_mbps INT UNSIGNED NULL AFTER name',
        'SELECT 1'
    );

PREPARE add_speed_statement
FROM
    @add_speed_sql;

EXECUTE add_speed_statement;

DEALLOCATE PREPARE add_speed_statement;

-- Planos antigos são preservados para manter referências históricas, mas deixam
-- de aparecer no portal. Os quatro planos abaixo formam o novo catálogo ativo.
UPDATE internet_plans
SET
    is_active = FALSE;

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
