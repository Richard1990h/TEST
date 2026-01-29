-- Performance indexes for LittleHelperAI (MariaDB/MySQL)
-- Safe to run multiple times.

-- --------------------------------------------------------
-- Chat history: fast per-user history fetches
-- --------------------------------------------------------
SET @idx := (SELECT COUNT(1) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'chathistory'
               AND INDEX_NAME = 'IX_chathistory_UserId_Timestamp');
SET @sql := IF(@idx = 0,
    'CREATE INDEX IX_chathistory_UserId_Timestamp ON chathistory (UserId, Timestamp)',
    'SELECT "IX_chathistory_UserId_Timestamp already exists"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- --------------------------------------------------------
-- Knowledge base: fast exact lookups on question
-- (prefix index to keep it compatible with TEXT/LONGTEXT)
-- --------------------------------------------------------
SET @idx := (SELECT COUNT(1) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'knowledge'
               AND INDEX_NAME = 'IX_knowledge_question');
SET @sql := IF(@idx = 0,
    'CREATE INDEX IX_knowledge_question ON knowledge (question(190))',
    'SELECT "IX_knowledge_question already exists"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- --------------------------------------------------------
-- Users: speed up credit checks
-- --------------------------------------------------------
SET @idx := (SELECT COUNT(1) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users'
               AND INDEX_NAME = 'IX_users_id_credits');
SET @sql := IF(@idx = 0,
    'CREATE INDEX IX_users_id_credits ON users (id, Credits)',
    'SELECT "IX_users_id_credits already exists"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
