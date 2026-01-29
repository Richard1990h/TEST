-- ============================================
-- REFERRAL SYSTEM DATABASE SCHEMA
-- Add to your MySQL/MariaDB database
-- ============================================

-- Add referral columns to users table
ALTER TABLE `users` 
ADD COLUMN `referral_code` VARCHAR(20) NULL AFTER `updated_at`,
ADD COLUMN `referred_by_user_id` INT NULL AFTER `referral_code`,
ADD UNIQUE INDEX `ix_users_referral_code` (`referral_code`);

-- Create referral_settings table (singleton)
CREATE TABLE IF NOT EXISTS `referral_settings` (
  `id` INT NOT NULL DEFAULT 1,
  `referrer_credits` DOUBLE NOT NULL DEFAULT 50,
  `referee_credits` DOUBLE NOT NULL DEFAULT 25,
  `is_enabled` TINYINT(1) NOT NULL DEFAULT 1,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Insert default settings
INSERT INTO `referral_settings` (`id`, `referrer_credits`, `referee_credits`, `is_enabled`)
VALUES (1, 50, 25, 1)
ON DUPLICATE KEY UPDATE `id` = `id`;

-- Create referral_transactions table
CREATE TABLE IF NOT EXISTS `referral_transactions` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `referrer_id` INT NOT NULL,
  `referee_id` INT NOT NULL,
  `referral_code` VARCHAR(20) NOT NULL,
  `referrer_credits_awarded` DOUBLE NOT NULL DEFAULT 0,
  `referee_credits_awarded` DOUBLE NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  INDEX `ix_referral_transactions_referrer_id` (`referrer_id`),
  INDEX `ix_referral_transactions_referee_id` (`referee_id`),
  FOREIGN KEY (`referrer_id`) REFERENCES `users`(`id`) ON DELETE CASCADE,
  FOREIGN KEY (`referee_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Generate referral codes for existing users
-- This will create a code like: JOHN-XXXX for each user
UPDATE `users` 
SET `referral_code` = CONCAT(
    UPPER(LEFT(username, 4)),
    '-',
    UPPER(SUBSTRING(MD5(CONCAT(id, NOW())), 1, 4))
)
WHERE `referral_code` IS NULL OR `referral_code` = '';
