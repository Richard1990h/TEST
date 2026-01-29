
-- Run this in your MariaDB/phpMyAdmin

CREATE TABLE IF NOT EXISTS Users (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(100) NOT NULL,
    Email VARCHAR(255) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Role VARCHAR(50) NOT NULL,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO Users (Username, Email, PasswordHash, Role, FirstName, LastName)
VALUES ('admin', 'admin@example.com', '$2y$10$wF2NvRpryW9pJt/3ONME9uvBHZdOaVErV8Wm6VbgvTVOkY5ITpSm6', 'Admin', 'System', 'Administrator');
