USE asterlink_portal;

-- Migração segura para bancos existentes. Novos cadastros exigem CPF na aplicação,
-- mas usuários antigos permanecem com NULL até que seus CPFs reais sejam informados.
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
