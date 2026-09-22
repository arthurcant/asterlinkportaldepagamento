USE asterlink_portal;

INSERT INTO
    users (name, username, email, password_hash, is_active)
VALUES
    (
        'Usuário de Teste',
        'teste',
        'teste@asterlink.com.br',
        'pbkdf2_sha256$210000$ZhueMYJGKgThKha3NZJgsg==$A/IvaiIsYO7rvZkABg7oPp2rtXP0rAVDZJLAvq3QjOg=',
        TRUE
    )
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    email = VALUES(email),
    password_hash = VALUES(password_hash),
    is_active = TRUE;
