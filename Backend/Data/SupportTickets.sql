-- ============================================================================
-- SUPPORT TICKETS / COMPLAINT SYSTEM
-- ============================================================================

CREATE TABLE IF NOT EXISTS `support_tickets` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `user_id` INT NOT NULL,
  `subject` VARCHAR(255) NOT NULL,
  `message` TEXT NOT NULL,
  `status` VARCHAR(32) NOT NULL DEFAULT 'open',
  `priority` VARCHAR(32) NOT NULL DEFAULT 'normal',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NULL,
  `resolved_at` DATETIME NULL,
  `assigned_admin_id` INT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_support_tickets_user_id` (`user_id`),
  KEY `ix_support_tickets_status` (`status`),
  CONSTRAINT `fk_support_tickets_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS `support_ticket_replies` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `ticket_id` INT NOT NULL,
  `sender_id` INT NOT NULL,
  `is_admin_reply` TINYINT(1) NOT NULL DEFAULT 0,
  `message` TEXT NOT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_support_ticket_replies_ticket_id` (`ticket_id`),
  CONSTRAINT `fk_support_ticket_replies_ticket` FOREIGN KEY (`ticket_id`)
    REFERENCES `support_tickets`(`id`) ON DELETE CASCADE
);
