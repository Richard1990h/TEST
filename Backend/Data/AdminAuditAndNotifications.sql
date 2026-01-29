-- ============================================================================
-- ADD-ONLY TABLES for Notifications + Admin Audit Log
-- This project creates these tables at startup in Program.cs as well.
-- ============================================================================

CREATE TABLE IF NOT EXISTS `user_notifications` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` INT NOT NULL,
  `title` VARCHAR(255) NOT NULL,
  `message` TEXT NOT NULL,
  `action_url` VARCHAR(512) NULL,
  `is_read` TINYINT(1) NOT NULL DEFAULT 0,
  `created_utc` DATETIME NOT NULL DEFAULT UTC_TIMESTAMP,
  `read_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  KEY `ix_user_notifications_user_id` (`user_id`),
  KEY `ix_user_notifications_user_read` (`user_id`,`is_read`),
  CONSTRAINT `fk_user_notifications_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `admin_audit_log` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `admin_user_id` INT NOT NULL,
  `action` VARCHAR(64) NOT NULL,
  `entity` VARCHAR(64) NOT NULL,
  `entity_id` VARCHAR(128) NULL,
  `details` TEXT NOT NULL,
  `created_utc` DATETIME NOT NULL DEFAULT UTC_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_admin_audit_created` (`created_utc`),
  KEY `ix_admin_audit_admin` (`admin_user_id`),
  CONSTRAINT `fk_admin_audit_user` FOREIGN KEY (`admin_user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
